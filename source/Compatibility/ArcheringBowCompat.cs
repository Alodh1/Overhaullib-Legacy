using CombatOverhaul.Integration;
using CombatOverhaul.RangedSystems;
using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Collections;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.Client.NoObf;

namespace CombatOverhaul.Compatibility;

internal sealed class ArcheringBowCompat
{
    private const string ModId = "archering";
    private const string ModSystemTypeName = "Archering.ArcheringModSystem";
    private const float VanillaDrawTime = 0.65f;

    private static readonly Dictionary<string, float> DefaultBowDamageByKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["bow-crude"] = 2f,
        ["bow-simple"] = 5f,
        ["bow-long"] = 15.9f,
        ["bow-recurve"] = 10.8f,
        ["bowsbykanahaku:hornybow-nomad*"] = 15.9f,
        ["bowsbykanahaku:hornybow-yagermaster*"] = 10.8f,
        ["bowsbykanahaku:expandedbow-ranger*"] = 15.9f,
        ["bowsbykanahaku:expandedbow-yager*"] = 10.8f
    };

    private readonly ICoreAPI _api;
    private bool _warnedMissingSystem;
    private Type? _modSystemType;
    private object? _modSystem;

    public ArcheringBowCompat(ICoreAPI api)
    {
        _api = api;
        Enabled = api.ModLoader.IsModEnabled(ModId);
    }

    public bool Enabled { get; }

    public ArcheringBowAimingSettings? GetSettings(Item item)
    {
        if (!Enabled || item.Code == null) return null;

        object? config = GetConfig();
        if (config == null) return null;

        IDictionary? bowSettings = GetValue(config, "BowSettings") as IDictionary;
        if (bowSettings == null) return null;

        (object? bowStats, string matchedKey) = ResolveBowStats(item.Code, bowSettings);
        if (bowStats == null) return null;

        IDictionary? arrowBreakChances = GetValue(config, "ArrowBreakChances") as IDictionary;

        return new ArcheringBowAimingSettings
        {
            MatchedKey = matchedKey,
            Damage = Math.Max(0f, GetFloat(bowStats, "Damage", 0f)),
            DefaultDamage = ResolveDefaultDamage(item.Code, matchedKey),
            DamageMult = Math.Max(0f, GetFloat(config, "DamageMult", 1f)),
            DrawTime = Math.Max(0.05f, GetFloat(bowStats, "DrawTime", 1.2f)),
            DrawWeight = Math.Max(0.05f, GetFloat(bowStats, "DrawWeight", 1.5f)),
            SwayAmplitude = Math.Max(0f, GetFloat(config, "SwayAmplitude", 1f)),
            SwaySpeed = Math.Max(0f, GetFloat(config, "SwaySpeed", 1f)),
            ChargeAmplitude = Math.Max(0f, GetFloat(config, "ChargeAmplitude", 1f)),
            RunPenaltyMultiplier = Math.Max(1f, GetFloat(config, "RunPenaltyMultiplier", 9.5f)),
            HoldBreathMultiplier = Math.Max(0f, GetFloat(config, "HoldBreathMultiplier", 0.5f)),
            BreathRecoveryMultiplier = Math.Max(0f, GetFloat(config, "BreathRecoveryMultiplier", 1.67f)),
            BreathDuration = Math.Max(0.1f, GetFloat(config, "BreathDuration", 2f)),
            BreathRecoveryDuration = Math.Max(0.1f, GetFloat(config, "BreathRecoveryDuration", 5f)),
            DrawWeightDropPower = GetFloat(config, "DrawWeightDropPower", 0.65f),
            DrawWeightChargePower = GetFloat(config, "DrawWeightChargePower", 0.85f),
            EnableSwaying = GetBool(config, "EnableSwaying", true),
            EnableBowDamageTweaks = GetBool(config, "EnableBowDamageTweaks", true),
            EnableDrawTimeTweaks = GetBool(config, "EnableDrawTimeTweaks", true),
            EnableDrawTimeArrowDamageTweaks = GetBool(config, "EnableDrawTimeArrowDamageTweaks", true),
            EnableHoldBreath = GetBool(config, "EnableHoldBreath", true),
            EnableDenockingWithLeftClick = GetBool(config, "EnableDenockingWithLeftClick", true),
            EnableArcheringChargedReticle = GetBool(config, "EnableArcheringChargedReticle", true),
            EnableSpeedIncreasesWithDrawWeight = GetBool(config, "EnableSpeedIncreasesWithDrawWeight", true),
            EnableArrowBreakTweaks = GetBool(config, "EnableArrowBreakTweaks", true),
            DisableVanillaReticle = GetBool(config, "DisableVanillaReticle", true),
            ArrowBreakChances = CopyFloatRules(arrowBreakChances)
        };
    }

    public float GetVelocityMultiplier(Item item)
    {
        ArcheringBowAimingSettings? settings = GetSettings(item);
        if (settings?.EnableSpeedIncreasesWithDrawWeight != true) return 1f;

        return settings.DrawWeightSpeedMultiplier;
    }

    public float GetGravityFactorMultiplier(Item item)
    {
        ArcheringBowAimingSettings? settings = GetSettings(item);
        if (settings == null || settings.EnableSpeedIncreasesWithDrawWeight) return 1f;

        float speedMultiplier = settings.DrawWeightSpeedMultiplier;
        return Math.Max(0.01f, 1f / (speedMultiplier * speedMultiplier));
    }

    public float GetBowDamageMultiplier(Item item)
    {
        ArcheringBowAimingSettings? settings = GetSettings(item);
        if (settings?.EnableBowDamageTweaks != true) return 1f;

        float multiplier = settings.DamageMult;
        if (settings.Damage > 0f && settings.DefaultDamage > 0f)
        {
            multiplier *= settings.Damage / settings.DefaultDamage;
        }

        return Math.Max(0f, multiplier);
    }

    public float GetDrawTimeArrowDamageMultiplier(Item item)
    {
        ArcheringBowAimingSettings? settings = GetSettings(item);
        if (settings?.EnableDrawTimeArrowDamageTweaks != true) return 1f;

        return Math.Max(0f, settings.DrawTime / VanillaDrawTime);
    }

    public void ApplyArrowBreakChance(Item bowItem, Item projectileItem, ProjectileStats stats)
    {
        ArcheringBowAimingSettings? settings = GetSettings(bowItem);
        if (settings?.EnableArrowBreakTweaks != true || projectileItem.Code == null) return;

        float? breakChance = ResolveFloatRule(projectileItem.Code, settings.ArrowBreakChances);
        if (breakChance == null) return;

        stats.DropChance = Math.Clamp(1f - breakChance.Value, 0f, 1f);
    }

    private object? GetConfig()
    {
        object? system = GetModSystem();
        if (system == null) return null;

        return GetValue(system, "Config");
    }

    private object? GetModSystem()
    {
        if (_modSystem != null) return _modSystem;

        _modSystemType ??= AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .FirstOrDefault(type => type.FullName == ModSystemTypeName);

        if (_modSystemType == null)
        {
            WarnMissingSystem("Archering is loaded, but its mod system type was not found.");
            return null;
        }

        MethodInfo? method = _api.ModLoader.GetType()
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(methodInfo => methodInfo.Name == "GetModSystem" && methodInfo.IsGenericMethodDefinition)
            .OrderBy(methodInfo => methodInfo.GetParameters().Length)
            .FirstOrDefault(methodInfo =>
            {
                ParameterInfo[] parameters = methodInfo.GetParameters();
                return parameters.Length == 0 || (parameters.Length == 1 && parameters[0].ParameterType == typeof(bool));
            });

        if (method == null)
        {
            WarnMissingSystem("Archering is loaded, but ModLoader.GetModSystem<T>() was not found.");
            return null;
        }

        try
        {
            MethodInfo genericMethod = method.MakeGenericMethod(_modSystemType);
            object?[]? arguments = genericMethod.GetParameters().Length == 0 ? null : [false];
            _modSystem = genericMethod.Invoke(_api.ModLoader, arguments);
        }
        catch (Exception exception)
        {
            WarnMissingSystem($"Could not read Archering mod system: {exception.GetBaseException().Message}");
        }

        return _modSystem;
    }

    private void WarnMissingSystem(string message)
    {
        if (_warnedMissingSystem) return;

        _warnedMissingSystem = true;
        LoggerUtil.Warn(_api, typeof(ArcheringBowCompat), message);
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
        catch
        {
            return [];
        }
    }

    private static (object? stats, string matchedKey) ResolveBowStats(AssetLocation bowCode, IDictionary bowSettings)
    {
        string bowPath = bowCode.Path;
        string fullCode = bowCode.ToString();

        foreach (DictionaryEntry entry in bowSettings)
        {
            if (entry.Key is not string key) continue;
            if (key.Equals(bowPath, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(fullCode, StringComparison.OrdinalIgnoreCase))
            {
                return (entry.Value, key);
            }
        }

        foreach (DictionaryEntry entry in bowSettings)
        {
            if (entry.Key is not string key) continue;
            if (!key.Contains('*') && !key.Contains('?')) continue;

            if (MatchesRule(key, bowCode))
            {
                return (entry.Value, key);
            }
        }

        return bowSettings.Contains("bow-generic") ? (bowSettings["bow-generic"], "bow-generic") : (null, "");
    }

    private static float ResolveDefaultDamage(AssetLocation bowCode, string matchedKey)
    {
        if (DefaultBowDamageByKey.TryGetValue(matchedKey, out float exactDamage))
        {
            return exactDamage;
        }

        foreach ((string key, float damage) in DefaultBowDamageByKey)
        {
            if (MatchesRule(key, bowCode))
            {
                return damage;
            }
        }

        return 0f;
    }

    private static Dictionary<string, float> CopyFloatRules(IDictionary? source)
    {
        Dictionary<string, float> result = new(StringComparer.OrdinalIgnoreCase);
        if (source == null) return result;

        foreach (DictionaryEntry entry in source)
        {
            if (entry.Key is not string key) continue;
            result[key] = ToFloat(entry.Value, 0f);
        }

        return result;
    }

    private static float? ResolveFloatRule(AssetLocation code, IReadOnlyDictionary<string, float> rules)
    {
        string path = code.Path;
        string full = code.ToString();

        foreach ((string key, float value) in rules)
        {
            if (key.Equals(path, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(full, StringComparison.OrdinalIgnoreCase))
            {
                return value;
            }
        }

        foreach ((string key, float value) in rules)
        {
            if ((key.Contains('*') || key.Contains('?')) && MatchesRule(key, code))
            {
                return value;
            }
        }

        return null;
    }

    private static bool MatchesRule(string key, AssetLocation code)
    {
        if (key.Contains(':'))
        {
            return WildcardUtil.Match(new AssetLocation(key), code);
        }

        return WildcardUtil.Match(key, code.Path) || WildcardUtil.Match(key, code.ToString());
    }

    private static object? GetValue(object instance, string name)
    {
        Type type = instance.GetType();
        const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        return type.GetField(name, flags)?.GetValue(instance)
            ?? type.GetProperty(name, flags)?.GetValue(instance);
    }

    private static float GetFloat(object instance, string name, float fallback)
    {
        return ToFloat(GetValue(instance, name), fallback);
    }

    private static float ToFloat(object? value, float fallback)
    {
        return value switch
        {
            float floatValue => floatValue,
            double doubleValue => (float)doubleValue,
            int intValue => intValue,
            long longValue => longValue,
            _ => fallback
        };
    }

    private static bool GetBool(object instance, string name, bool fallback)
    {
        object? value = GetValue(instance, name);
        return value is bool boolValue ? boolValue : fallback;
    }
}

internal sealed class ArcheringBowAimingSettings
{
    public string MatchedKey { get; init; } = "";
    public float Damage { get; init; }
    public float DefaultDamage { get; init; }
    public float DamageMult { get; init; } = 1f;
    public float DrawTime { get; init; } = 1.2f;
    public float DrawWeight { get; init; } = 1.5f;
    public float SwayAmplitude { get; init; } = 1f;
    public float SwaySpeed { get; init; } = 1f;
    public float ChargeAmplitude { get; init; } = 1f;
    public float RunPenaltyMultiplier { get; init; } = 9.5f;
    public float HoldBreathMultiplier { get; init; } = 0.5f;
    public float BreathRecoveryMultiplier { get; init; } = 1.67f;
    public float BreathDuration { get; init; } = 2f;
    public float BreathRecoveryDuration { get; init; } = 5f;
    public float DrawWeightDropPower { get; init; } = 0.65f;
    public float DrawWeightChargePower { get; init; } = 0.85f;
    public bool EnableSwaying { get; init; } = true;
    public bool EnableBowDamageTweaks { get; init; } = true;
    public bool EnableDrawTimeTweaks { get; init; } = true;
    public bool EnableDrawTimeArrowDamageTweaks { get; init; } = true;
    public bool EnableHoldBreath { get; init; } = true;
    public bool EnableDenockingWithLeftClick { get; init; } = true;
    public bool EnableArcheringChargedReticle { get; init; } = true;
    public bool EnableSpeedIncreasesWithDrawWeight { get; init; } = true;
    public bool EnableArrowBreakTweaks { get; init; } = true;
    public bool DisableVanillaReticle { get; init; } = true;
    public IReadOnlyDictionary<string, float> ArrowBreakChances { get; init; } = new Dictionary<string, float>();

    public float DrawWeightSpeedMultiplier => Math.Max(0.01f, MathF.Pow(DrawWeight, DrawWeightDropPower));
}

internal sealed class ArcheringBowAimingSession : IRenderer
{
    private readonly ICoreClientAPI _api;
    private readonly Random _random = new();
    private readonly LoadedTexture _chargedReticle;
    private readonly ArcheringBowBreathHud _breathHud;
    private ArcheringBowAimingSettings? _settings;
    private EntityPlayer? _player;
    private Vector2 _oldPos;
    private Vector2 _targetOffset;
    private Vector2 _controlOffset;
    private long _startMs;
    private long _flashUntilMs;
    private float _stanceMultiplier = 1f;
    private float _currentBreath = 1f;
    private bool _isRecovering;
    private bool _isHoldingBreath;
    private bool _charged;
    private bool _reticleRegistered;
    private bool _cameraHookRegistered;
    private Func<bool>? _shouldContinue;

    public ArcheringBowAimingSession(ICoreClientAPI api)
    {
        _api = api;
        _chargedReticle = new(api);
        api.Render.GetOrLoadTexture(new AssetLocation("combatoverhaul", "gui/aiming/default-full.png"), ref _chargedReticle);
        _breathHud = new(api);
        _breathHud.TryOpen();
    }

    public double RenderOrder => 0.98;
    public int RenderRange => 9999;
    public bool Active => _settings != null;

    public void Start(ArcheringBowAimingSettings settings, EntityPlayer player, Func<bool> shouldContinue)
    {
        Stop();

        _settings = settings;
        _player = player;
        _shouldContinue = shouldContinue;
        _charged = false;
        _flashUntilMs = 0;
        _startMs = _api.World.ElapsedMilliseconds;
        _oldPos = Vector2.Zero;
        _stanceMultiplier = 0f;

        float drawWeight = Math.Max(settings.DrawWeight, 0.05f);
        float intensity = 3f * Rational(settings.DrawTime, 2.5f, 3.75f) * 0.015f
            * MathF.Pow(drawWeight + 0.5f, settings.DrawWeightChargePower)
            * 1.35f
            * settings.ChargeAmplitude;

        float minAngle = MathF.PI * 7f / 30f;
        float maxAngle = MathF.PI * 4f / 15f;
        float angle = minAngle + (float)_random.NextDouble() * (maxAngle - minAngle);

        _targetOffset = new(-MathF.Cos(angle) * intensity, -MathF.Sin(angle) * intensity);
        _controlOffset = new(
            _targetOffset.X / 2f + _targetOffset.Y * 1.2f * 0.9f,
            _targetOffset.Y / 2f - _targetOffset.X * 1.2f * 1.2f * MathF.Sqrt(drawWeight + 1.5f) * 1.1f);

        AimingPatches.UpdateCameraYawPitch += OnCameraYawPitch;
        _cameraHookRegistered = true;

        if (settings.EnableArcheringChargedReticle)
        {
            _api.Event.RegisterRenderer(this, EnumRenderStage.Ortho);
            _reticleRegistered = true;
        }
    }

    public void Stop()
    {
        if (_cameraHookRegistered)
        {
            AimingPatches.UpdateCameraYawPitch -= OnCameraYawPitch;
            _cameraHookRegistered = false;
        }

        if (_reticleRegistered)
        {
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            _reticleRegistered = false;
        }

        _settings = null;
        _player = null;
        _shouldContinue = null;
        _isHoldingBreath = false;
        _charged = false;
        _breathHud.UpdateBar(1f, 1f, isRecovering: false, visible: false);
    }

    public void Dispose()
    {
        Stop();
        _chargedReticle.Dispose();
        _breathHud.TryClose();
        _breathHud.Dispose();
    }

    public void SetCharged()
    {
        if (_charged) return;

        _charged = true;
        _flashUntilMs = _api.World.ElapsedMilliseconds + 250;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_settings == null || _player == null || stage != EnumRenderStage.Ortho) return;

        RenderChargeFlash();
    }

    private void OnCameraYawPitch(ClientMain __instance,
        ref double mouseDeltaX,
        ref double mouseDeltaY,
        ref double delayedMouseDeltaX,
        ref double delayedMouseDeltaY,
        float dt)
    {
        if (_settings == null || _player == null) return;

        if (_api.IsGamePaused || _shouldContinue?.Invoke() != true)
        {
            Stop();
            return;
        }

        ApplyHoldBreath(dt);
        ApplyCameraSway(dt);
    }

    private void RenderChargeFlash()
    {
        if (_api.World.ElapsedMilliseconds > _flashUntilMs) return;

        float scale = RuntimeEnv.GUIScale;
        _api.Render.Render2DTexture(
            _chargedReticle.TextureId,
            _api.Render.FrameWidth / 2f - _chargedReticle.Width * scale / 2f,
            _api.Render.FrameHeight / 2f - _chargedReticle.Height * scale / 2f,
            _chargedReticle.Width * scale,
            _chargedReticle.Height * scale,
            10000f);
    }

    private void ApplyHoldBreath(float deltaTime)
    {
        if (_settings == null || _player == null || !_settings.EnableHoldBreath)
        {
            _isHoldingBreath = false;
            _breathHud.UpdateBar(1f, 1f, isRecovering: false, visible: false);
            return;
        }

        bool wantsHoldBreath = _player.Controls.Sprint || _player.Controls.Sneak;
        _isRecovering = _currentBreath != 1f && _isRecovering;

        if (_player.Controls.RightMouseDown && wantsHoldBreath && _currentBreath > 0f && !_isRecovering && _charged)
        {
            _isHoldingBreath = true;
            _currentBreath = Math.Max(0f, _currentBreath - deltaTime / _settings.BreathDuration);
            _breathHud.UpdateBar(_currentBreath, 1f, _isRecovering, visible: _currentBreath < 1f);
            return;
        }

        _isHoldingBreath = false;
        _isRecovering = _currentBreath == 0f || _isRecovering;
        _currentBreath = Math.Min(1f, _currentBreath + deltaTime / _settings.BreathRecoveryDuration);
        if (_currentBreath >= 1f) _isRecovering = false;

        _breathHud.UpdateBar(_currentBreath, 1f, _isRecovering, visible: _currentBreath < 1f);
    }

    private void ApplyCameraSway(float deltaTime)
    {
        if (_settings == null || _player == null || !_settings.EnableSwaying) return;

        float elapsedSec = (_api.World.ElapsedMilliseconds - _startMs) / 1000f;
        float worldTimeSec = _api.World.ElapsedMilliseconds / 1000f;
        float charge = Math.Min(elapsedSec / _settings.DrawTime, 1f);

        float targetMultiplier = 1f;
        if (_player.Controls.TriesToMove || _player.Controls.Jump)
        {
            targetMultiplier *= _settings.RunPenaltyMultiplier + (_player.Controls.Sprint ? 2.5f : 0f);
        }
        else if (_isHoldingBreath)
        {
            targetMultiplier *= _settings.HoldBreathMultiplier;
        }
        else if (_isRecovering)
        {
            targetMultiplier *= _settings.BreathRecoveryMultiplier;
        }

        Vector2 chargeDisplacement = GetChargeBezierDisplacement(elapsedSec);
        Vector2 sway = GetLissajousCurveDisplacement(worldTimeSec) * MathF.Sqrt(Math.Max(_settings.DrawWeight - 0.5f, 0.01f)) * 1.2f;

        _stanceMultiplier = GameMath.Lerp(_stanceMultiplier, targetMultiplier, deltaTime * 5f);

        const float swayStartCharge = 0.3f;
        float stanceAtCharge = GameMath.SmoothStep(Math.Clamp((charge - swayStartCharge) / (1f - swayStartCharge), 0f, 1f));
        float movingPenalty = Math.Max(0f, _stanceMultiplier - 1f) * (_settings.RunPenaltyMultiplier / Math.Max(0.001f, _settings.RunPenaltyMultiplier - 1f));
        float swayMultiplier = GameMath.Lerp(movingPenalty, _stanceMultiplier, stanceAtCharge);

        Vector2 nextPos = chargeDisplacement + sway / 1.5f * swayMultiplier;
        Vector2 delta = nextPos - _oldPos;

        _api.Input.MouseYaw += delta.X;
        _api.Input.MousePitch += delta.Y;
        _player.Pos.Yaw += delta.X;
        _player.Pos.Pitch += delta.Y;

        _oldPos = nextPos;
    }

    private Vector2 GetChargeBezierDisplacement(float elapsedSec)
    {
        if (_settings == null) return Vector2.Zero;

        float rawCharge = Math.Min(elapsedSec / _settings.DrawTime, 1f);
        float smoothCharge = GameMath.SmoothStep(rawCharge);
        float charge = GameMath.Lerp(rawCharge, smoothCharge, smoothCharge);
        float inverse = 1f - charge;

        return new(
            2f * inverse * charge * _controlOffset.X + charge * charge * _targetOffset.X,
            2f * inverse * charge * _controlOffset.Y + charge * charge * _targetOffset.Y);
    }

    private Vector2 GetLissajousCurveDisplacement(float timeSec)
    {
        if (_settings == null) return Vector2.Zero;

        float speed = 0.5f * _settings.SwaySpeed;
        float amplitude = 0.05f * _settings.SwayAmplitude;

        return new(
            MathF.Sin(4f * speed * timeSec + MathF.PI / 2f) * amplitude,
            MathF.Sin(3f * speed * timeSec) * amplitude);
    }

    private static float Rational(float x, float k, float max = 1f) => x / (x + k) * max;
}

internal sealed class ArcheringBowBreathHud : HudElement
{
    private readonly double[] _breathColor = [0.2, 0.7, 0.8, 0.8];
    private bool _visible;

    public ArcheringBowBreathHud(ICoreClientAPI capi) : base(capi)
    {
        ElementBounds dialogBounds = ElementBounds.Fixed(EnumDialogArea.CenterBottom, 0.0, -150.0, 200.0, 10.0);
        ElementBounds barBounds = ElementBounds.Fixed(0.0, 0.0, 200.0, 10.0);
        SingleComposer = capi.Gui.CreateCompo("combatOverhaulArcheringBreathHud", dialogBounds)
            .AddStatbar(barBounds, _breathColor, "breathBar")
            .Compose();
    }

    public override bool Focusable => false;
    public override bool ShouldReceiveMouseEvents() => false;
    public override bool ShouldReceiveKeyboardEvents() => false;

    public void UpdateBar(float currentBreath, float maxBreath, bool isRecovering, bool visible)
    {
        _visible = visible;

        if (isRecovering)
        {
            _breathColor[0] = 0.8;
            _breathColor[1] = 0.2;
            _breathColor[2] = 0.2;
            _breathColor[3] = 0.8;
        }
        else
        {
            _breathColor[0] = 0.2;
            _breathColor[1] = 0.7;
            _breathColor[2] = 0.8;
            _breathColor[3] = 0.8;
        }

        SingleComposer.GetStatbar("breathBar")?.SetValues(currentBreath, 0f, maxBreath);
    }

    public override void OnRenderGUI(float deltaTime)
    {
        if (!_visible) return;

        base.OnRenderGUI(deltaTime);
    }
}
