using CombatOverhaul.Integration;
using CombatOverhaul.Integration.Transpilers;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace CombatOverhaul.Animations;

public sealed class FirstPersonAnimationsBehavior : EntityBehavior, IDisposable
{
    public FirstPersonAnimationsBehavior(Entity entity) : base(entity)
    {
        if (entity is not EntityPlayer player) throw new ArgumentException("Only for players");

        _player = player;
        _api = player.Api as ICoreClientAPI ?? throw new ArgumentException("Only client side");
        _animationsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().PlayerAnimationsManager ?? throw new Exception();
        _vanillaAnimationsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ClientVanillaAnimations ?? throw new Exception();
        _settings = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;

        SoundsSynchronizerClient soundsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().ClientSoundsSynchronizer ?? throw new Exception();
        ParticleEffectsManager particleEffectsManager = player.Api.ModLoader.GetModSystem<CombatOverhaulAnimationsSystem>().ParticleEffectsManager ?? throw new Exception();
        _composer = new(soundsManager, particleEffectsManager, player);

        _MainHandIdleAnimationsController = new(player, request => PlayImpl(request, mainHand: true), () => Stop("main"), () => _player.RightHandItemSlot, mainHand: true);
        _OffHandIdleAnimationsController = new(player, request => PlayImpl(request, mainHand: false), () => Stop("mainOffhand"), () => _player.LeftHandItemSlot, mainHand: false);

        TryActivateMainPlayer();
    }

    public override string PropertyName() => "CombatOverhaul:FirstPersonAnimations";

    public override void AfterInitialized(bool onFirstSpawn)
    {
        TryActivateMainPlayer();
        if (!_mainPlayer) return;

        ResolveThirdPersonAnimations();
    }

    public override void OnGameTick(float deltaTime)
    {
        TryActivateMainPlayer();
        if (!_mainPlayer || _player.RightHandItemSlot == null || _player.LeftHandItemSlot == null) return;

#if DEBUG
        if (DebugWindowManager.DebugPoseFreezeActive)
        {
            _MainHandIdleAnimationsController.Pause();
            _OffHandIdleAnimationsController.Pause();
            _playRequests.Clear();
            _composer.StopAll();
            _lastFrame = FrameOverride ?? PlayerItemFrame.Zero;
            return;
        }
#endif

        int mainHandItemId = _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        int offhandItemId = _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;

        if (_mainHandItemId != mainHandItemId)
        {
            _mainHandItemId = mainHandItemId;
            InHandItemChanged(mainHand: true);
        }

        if (_offHandItemId != offhandItemId)
        {
            _offHandItemId = offhandItemId;
            InHandItemChanged(mainHand: false);
        }

        _MainHandIdleAnimationsController.Update();
        _OffHandIdleAnimationsController.Update();

        foreach ((AnimationRequest request, bool mainHand, bool skip, int itemId) in _playRequests)
        {
            if (!skip) PlayRequest(request, mainHand);
        }
        _playRequests.Clear();

        _settingsUpdateTimeSec += deltaTime;
        if (_settingsUpdateTimeSec > _settingsUpdatePeriodSec)
        {
            _settingsUpdateTimeSec = 0;
            _settingsFOV = ClientSettings.FieldOfView;
            _settingsHandsFOV = ClientSettings.FieldOfView;
        }

        if (_api != null && _ownerEntityId == 0)
        {
            _ownerEntityId = _api.World?.Player?.Entity?.EntityId ?? 0;
        }
    }

    public void OnFrame(Entity entity, ElementPose pose, AnimatorBase animator)
    {
        _frameApplied = true;

#if DEBUG
        bool debugFreeze = DebugWindowManager.DebugPoseFreezeActive && entity.EntityId == _ownerEntityId;
#else
        const bool debugFreeze = false;
#endif

        //if (IsImmersiveFirstPerson(entity)) return;
        if (!DebugWindowManager.PlayAnimationsInThirdPerson && !IsFirstPerson(entity) && !debugFreeze) return;
        if (!_composer.AnyActiveAnimations() && FrameOverride == null && !debugFreeze)
        {
            if (_resetFov)
            {
                SetFov(1, false);
                _player.HeadBobbingAmplitude /= _previousHeadBobbingAmplitudeFactor;
                _previousHeadBobbingAmplitudeFactor = 1;
                _resetFov = false;
            }
            return;
        }

        if (FrameOverride != null)
        {
            ApplyFrame(FrameOverride.Value, pose, animator, debugFreeze);
        }
        else if (debugFreeze)
        {
            ApplyFrame(PlayerItemFrame.Zero, pose, animator, clearPose: true);
        }
        else
        {
            ApplyFrame(_lastFrame, pose, animator);
        }
    }

    public PlayerItemFrame? FrameOverride { get; set; } = null;
    public PlayerItemFrame CurrentFrame => FrameOverride ?? _lastFrame;
    public bool HasActiveAnimationFrame => FrameOverride != null || _composer.AnyActiveAnimations();
    public static float CurrentFov { get; set; } = ClientSettings.FieldOfView;

    public void Play(AnimationRequest request, bool mainHand = true)
    {
        TryActivateMainPlayer();
        if (request.Category == GetIdleAnimationCategory(mainHand))
        {
            (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Pause();
        }
        _playRequests.Add((request, mainHand, false, CurrentItemId(mainHand)));
    }
    public void Play(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        Animation? animation = GetAnimationFromRequest(requestByCode);

        if (animation == null) return;

        AnimationRequest request = new(animation, requestByCode);

        Play(request, mainHand);

        _immersiveFpModeSetting = ((entity.Api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] ?? false); // calling here just to reduce number of calls to it
    }
    public void Play(bool mainHand, string animation, string category = "main", float animationSpeed = 1, float weight = 1, System.Func<bool>? callback = null, Action<string>? callbackHandler = null, bool easeOut = true)
    {
        AnimationRequestByCode request = new(animation, animationSpeed, weight, category, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), easeOut, callback, callbackHandler);
        Play(request, mainHand);
    }
    public void PlayReadyAnimation(bool mainHand = true)
    {
        TryActivateMainPlayer();
        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Start();
    }
    public void Stop(string category)
    {
        _composer.Stop(category);
        for (int index = 0; index < _playRequests.Count; index++)
        {
            if (_playRequests[index].request.Category == category)
            {
                _playRequests[index] = (new(), _playRequests[index].mainHand, true, -1);
            }
        }
    }
    public void PlayVanillaAnimation(string code, bool mainHand)
    {
        if (code == "") return;

        _vanillaAnimationsManager?.StartAnimation(code);
        if (mainHand)
        {
            _mainHandVanillaAnimations.Add(code);
        }
        else
        {
            _offhandVanillaAnimations.Add(code);
        }
    }
    public void StopVanillaAnimation(string code, bool mainHand)
    {
        _vanillaAnimationsManager?.StopAnimation(code);
        if (mainHand)
        {
            _mainHandVanillaAnimations.Remove(code);
        }
        else
        {
            _offhandVanillaAnimations.Remove(code);
        }
    }
    public void StopAllVanillaAnimations(bool mainHand)
    {
        HashSet<string> animations = mainHand ? _mainHandVanillaAnimations : _offhandVanillaAnimations;
        foreach (string code in animations)
        {
            _vanillaAnimationsManager?.StopAnimation(code);
        }
    }
    public void SetSpeedModifier(AnimationSpeedModifierDelegate modifier) => _composer.SetSpeedModifier(modifier);
    public void StopSpeedModifier() => _composer.StopSpeedModifier();
    public bool IsSpeedModifierActive() => _composer.IsSpeedModifierActive();

    public void PlayFpAndTp(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        Animation? animation = GetAnimationFromRequest(requestByCode);

        if (animation == null) return;

        AnimationRequest request = new(animation, requestByCode);

        Play(request, mainHand);

        _immersiveFpModeSetting = ((entity.Api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] ?? false); // calling here just to reduce number of calls to it

        AnimationRequestByCode tpRequest = new(requestByCode, requestByCode.AnimationSpeed);

        _thirdPersonAnimations?.Play(tpRequest, mainHand);
    }
    public void PlayFpAndTp(bool mainHand, string animation, string category = "main", float animationSpeed = 1, float weight = 1, System.Func<bool>? callback = null, Action<string>? callbackHandler = null, bool easeOut = true)
    {
        AnimationRequestByCode request = new(animation, animationSpeed, weight, category, TimeSpan.FromSeconds(0.1), TimeSpan.FromSeconds(0.1), easeOut, callback, callbackHandler);
        Play(request, mainHand);

        AnimationRequestByCode tpRequest = new(request, request.AnimationSpeed);

        _thirdPersonAnimations?.Play(tpRequest, mainHand);
    }
    public void PlayReadyAnimationFpAndTp(bool mainHand = true)
    {
        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Start();
        _thirdPersonAnimations?.PlayReadyAnimation(mainHand);
    }
    public void StopFpAndTp(string category)
    {
        _composer.Stop(category);
        for (int index = 0; index < _playRequests.Count; index++)
        {
            if (_playRequests[index].request.Category == category)
            {
                _playRequests[index] = (new(), _playRequests[index].mainHand, true, -1);
            }
        }

        _thirdPersonAnimations?.Stop(category);
    }

    private readonly Composer _composer;
    private readonly EntityPlayer _player;
    private readonly AnimationsManager _animationsManager;
    private readonly VanillaAnimationsSystemClient _vanillaAnimationsManager;
    private PlayerItemFrame _lastFrame = PlayerItemFrame.Zero;
    private readonly List<string> _offhandCategories = new();
    private readonly List<string> _mainHandCategories = new();
    private readonly HashSet<string> _offhandVanillaAnimations = new();
    private readonly HashSet<string> _mainHandVanillaAnimations = new();
    private bool _mainPlayer = false;
    private bool _registeredFrameHook = false;
    private bool _registeredDisposeHook = false;
    private bool _reportedMainPlayerActivation = false;
    private readonly Settings _settings;
    private readonly IdleAnimationsController _MainHandIdleAnimationsController;
    private readonly IdleAnimationsController _OffHandIdleAnimationsController;
    private ThirdPersonAnimationsBehavior? _thirdPersonAnimations;
    private bool _frameApplied = false;
    private int _offHandItemId = 0;
    private int _mainHandItemId = 0;
    private bool _resetFov = false;
    private readonly ICoreClientAPI _api;
    private readonly List<(AnimationRequest request, bool mainHand, bool skip, int itemId)> _playRequests = new();
    private float _previousHeadBobbingAmplitudeFactor = 1;
    private int _settingsFOV = ClientSettings.FieldOfView;
    private int _settingsHandsFOV = ClientSettings.FpHandsFoV;
    private const float _settingsUpdatePeriodSec = 3f;
    private float _settingsUpdateTimeSec = 0;
    private Animatable? _animatable = null;
    private Vector3 _eyePosition = new();
    private float _eyeHeight = 0;
    private bool _immersiveFpModeSetting = false;
    private long _ownerEntityId = 0;
    private bool _restIfpSetting = false;

    private bool TryActivateMainPlayer()
    {
        if (_mainPlayer)
        {
            return true;
        }

        if (!IsLocalPlayerEntity())
        {
            return false;
        }

        _mainPlayer = true;
        _ownerEntityId = _player.EntityId;
        AnimationPatches.FirstPersonAnimationBehavior = this;
        AnimationPatches.OwnerEntityId = _player.EntityId;

        if (!_registeredFrameHook)
        {
            AnimationPatches.OnBeforeFrame += OnBeforeFrame;
            _registeredFrameHook = true;
        }

        if (!_registeredDisposeHook)
        {
            _player.Api.ModLoader.GetModSystem<CombatOverhaulSystem>().OnDispose += Dispose;
            _registeredDisposeHook = true;
        }

        ResolveThirdPersonAnimations();

        if (!_reportedMainPlayerActivation)
        {
            _reportedMainPlayerActivation = true;
            LoggerUtil.Warn(_api, this, $"First-person animation behavior activated for local player '{_player.PlayerUID}' entity {_player.EntityId}.");
        }

        return true;
    }

    private bool IsLocalPlayerEntity()
    {
        IClientPlayer? localPlayer = _api.World?.Player;
        if (localPlayer?.Entity != null && localPlayer.Entity.EntityId == _player.EntityId)
        {
            return true;
        }

        string? localPlayerUid = localPlayer?.PlayerUID;
        if (string.IsNullOrEmpty(localPlayerUid))
        {
            try
            {
                localPlayerUid = _api.Settings?.String?["playeruid"];
            }
            catch
            {
                localPlayerUid = null;
            }
        }

        return !string.IsNullOrEmpty(localPlayerUid) && _player.PlayerUID == localPlayerUid;
    }

    private void ResolveThirdPersonAnimations()
    {
        _thirdPersonAnimations ??= entity.GetBehavior<ThirdPersonAnimationsBehavior>();
    }

    private void OnBeforeFrame(Entity targetEntity, float dt)
    {
        if (!IsOwner(targetEntity)) return;

        _lastFrame = _composer.Compose(TimeSpan.FromSeconds(dt));

        if (_composer.AnyActiveAnimations())
        {
            if (_lastFrame.Player.FovMultiplier != 1) SetFov(_lastFrame.Player.FovMultiplier, true);
            _resetFov = true;

            _animatable = (targetEntity as EntityAgent)?.RightHandItemSlot?.Itemstack?.Item?.GetCollectibleBehavior(typeof(Animatable), true) as Animatable;
            _eyePosition = new((float)targetEntity.LocalEyePos.X, (float)targetEntity.LocalEyePos.Y, (float)targetEntity.LocalEyePos.Z);
            _eyeHeight = (float)targetEntity.Properties.EyeHeight;

            if (Math.Abs(_lastFrame.Player.PitchFollow - PlayerFrame.DefaultPitchFollow) >= PlayerFrame.Epsilon)
            {
                if (targetEntity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
                {
                    renderer.HeldItemPitchFollowOverride = _lastFrame.Player.PitchFollow;
                }
            }
            else
            {
                if (targetEntity.Properties.Client.Renderer is EntityPlayerShapeRenderer renderer)
                {
                    renderer.HeldItemPitchFollowOverride = null;
                }
            }

            if (_settings.SwitchFromImmersiveFirstPerson && entity.Api is ICoreClientAPI clientApi && clientApi.Settings.Bool["immersiveFpMode"])
            {
                clientApi.Settings.Bool["immersiveFpMode"] = false;
                _restIfpSetting = true;
            }
        }
        else
        {
            if (entity.Api is ICoreClientAPI clientApi && _restIfpSetting)
            {
                clientApi.Settings.Bool["immersiveFpMode"] = true;
                _restIfpSetting = false;
            }
        }

        _frameApplied = false;
    }

    private void PlayImpl(AnimationRequestByCode requestByCode, bool mainHand = true)
    {
        TryActivateMainPlayer();
        Animation? animation = GetAnimationFromRequest(requestByCode);

        if (animation == null) return;

        AnimationRequest request = new(animation, requestByCode);

        _playRequests.Add((request, mainHand, false, CurrentItemId(mainHand)));
    }

    private void ApplyFrame(PlayerItemFrame frame, ElementPose pose, AnimatorBase animator, bool clearPose = false)
    {
        EnumAnimatedElement element;

        ExtendedElementPose? extendedPoseValue = null;
        if (pose is ExtendedElementPose extendedPose)
        {
            element = extendedPose.ElementNameEnum;
            extendedPoseValue = extendedPose;
        }
        else
        {
            if (!Enum.TryParse(pose.ForElement.Name, out element)) // Cant cache ElementPose because they are new each frame
            {
                element = EnumAnimatedElement.Unknown;
            }
        }

        if (clearPose && element != EnumAnimatedElement.Unknown)
        {
            pose.Clear();
        }

        if (element == EnumAnimatedElement.Unknown)
        {
            frame.Apply(pose, element, _eyePosition, _eyeHeight);
            return;
        }

        if (element == EnumAnimatedElement.LowerTorso && IsImmersiveFirstPerson(_player) && !clearPose)
        {
            return;
        }

        if (IsImmersiveFirstPerson(_player) && !clearPose)
        {
            PlayerRenderingPatches.SetOffset(0);
        }
        else
        {
            //PlayerRenderingPatches.ResetOffset();
        }

        if (extendedPoseValue != null)
        {
            frame.Apply(extendedPoseValue, element, _eyePosition, _eyeHeight);
        }
        else
        {
            frame.Apply(pose, element, _eyePosition, _eyeHeight);
        }

        _player.HeadBobbingAmplitude /= _previousHeadBobbingAmplitudeFactor;
        _previousHeadBobbingAmplitudeFactor = frame.Player.BobbingAmplitude;
        _player.HeadBobbingAmplitude *= _previousHeadBobbingAmplitudeFactor;

        if (_animatable != null && frame.DetachedAnchor)
        {
            _animatable.DetachedAnchor = true;
        }

        if (_animatable != null && frame.SwitchArms)
        {
            _animatable.SwitchArms = true;
        }

        _resetFov = true;
    }
    private static bool IsOwner(Entity entity) => (entity.Api as ICoreClientAPI)?.World.Player.Entity.EntityId == entity.EntityId;
    private bool IsFirstPerson(Entity entity)
    {
        bool owner = _ownerEntityId == entity.EntityId;
        if (!owner) return false;

        bool firstPerson = entity.Api is ICoreClientAPI { World.Player.CameraMode: EnumCameraMode.FirstPerson };

        return firstPerson;
    }
    private bool IsImmersiveFirstPerson(Entity entity)
    {
        return _immersiveFpModeSetting && IsFirstPerson(entity);
    }
    public static void SetFirstPersonHandsPitch(IClientPlayer player, float value)
    {
        if (player.Entity.Properties.Client.Renderer is not EntityPlayerShapeRenderer renderer) return;

        renderer.HeldItemPitchFollowOverride = 0.8f * value;
    }
    private void SetFov(float multiplier, bool equalizeFov = true)
    {
        ClientMain? client = _api?.World as ClientMain;
        if (client == null) return;

        PlayerCamera? camera = client.MainCamera;
        if (camera == null) return;

        float equalizeMultiplier = MathF.Sqrt(_settingsFOV / (float)_settingsHandsFOV);

        PlayerRenderingPatches.HandsFovMultiplier = multiplier * (equalizeFov ? equalizeMultiplier : 1);
        camera.Fov = _settingsFOV * GameMath.DEG2RAD * multiplier;

        CurrentFov = _settingsFOV * multiplier;
    }
    private void PlayRequest(AnimationRequest request, bool mainHand = true)
    {
        _composer.Play(request);
        if (mainHand)
        {
            _mainHandCategories.Add(request.Category);
        }
        else
        {
            _offhandCategories.Add(request.Category);
        }
    }
    private void StopRequestFromPreviousItem(bool mainHand)
    {
        int currentItem = CurrentItemId(mainHand);
        for (int index = 0; index < _playRequests.Count; index++)
        {
            if (_playRequests[index].itemId != currentItem)
            {
                _playRequests[index] = (new(), _playRequests[index].mainHand, true, -1);
            }
        }
    }

    private string GetIdleAnimationCategory(bool mainHand) => mainHand ? "main" : "mainOffhand";

    private void InHandItemChanged(bool mainHand)
    {
        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Stop();

        string readyCategory = GetIdleAnimationCategory(mainHand);

        List<string> categories = mainHand ? _mainHandCategories : _offhandCategories;
        foreach (string category in categories.Where(element => element != readyCategory))
        {
            _composer.Stop(category);
        }
        StopRequestFromPreviousItem(mainHand);
        categories.Clear();

        (mainHand ? _MainHandIdleAnimationsController : _OffHandIdleAnimationsController).Start();
    }

    private int CurrentItemId(bool mainHand)
    {
        if (mainHand)
        {
            return _player.RightHandItemSlot.Itemstack?.Item?.Id ?? 0;
        }
        else
        {
            return _player.LeftHandItemSlot.Itemstack?.Item?.Id ?? 0;
        }
    }

    private Animation? GetAnimationFromRequest(AnimationRequestByCode request)
    {
        if (_animationsManager == null) return null;

        if (!_animationsManager.GetAnimation(out Animation? animation, request.Animation, _player, firstPerson: true))
        {
            LoggerUtil.Verbose(_api, this, $"Animation '{request.Animation}' was not found");
            Debug.WriteLine($"Animation '{request.Animation}' was not found");
            return null;
        }

        return animation;
    }

    public void Dispose()
    {
        if (_registeredFrameHook)
        {
            AnimationPatches.OnBeforeFrame -= OnBeforeFrame;
            _registeredFrameHook = false;
        }

        if (AnimationPatches.FirstPersonAnimationBehavior == this)
        {
            AnimationPatches.FirstPersonAnimationBehavior = null;
        }
    }
}
