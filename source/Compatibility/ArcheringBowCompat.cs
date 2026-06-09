using CombatOverhaul.Utils;
using OpenTK.Mathematics;
using System.Collections;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace CombatOverhaul.Compatibility;

internal sealed class ArcheringBowCompat
{
    private const string ModId = "archering";
    private const string ModSystemTypeName = "Archering.ArcheringModSystem";

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

        object? bowStats = ResolveBowStats(item.Code, bowSettings);
        if (bowStats == null) return null;

        return new ArcheringBowAimingSettings
        {
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
            EnableDrawTimeTweaks = GetBool(config, "EnableDrawTimeTweaks", true),
            EnableHoldBreath = GetBool(config, "EnableHoldBreath", true),
            EnableArcheringChargedReticle = GetBool(config, "EnableArcheringChargedReticle", true),
            EnableSpeedIncreasesWithDrawWeight = GetBool(config, "EnableSpeedIncreasesWithDrawWeight", true),
            DisableVanillaReticle = GetBool(config, "DisableVanillaReticle", true),
        };
    }

    public float GetVelocityMultiplier(Item item)
    {
        ArcheringBowAimingSettings? settings = GetSettings(item);
        if (settings?.EnableSpeedIncreasesWithDrawWeight != true) return 1f;

        return Math.Max(0.01f, MathF.Pow(settings.DrawWeight, settings.DrawWeightDropPower));
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

    private static object? ResolveBowStats(AssetLocation bowCode, IDictionary bowSettings)
    {
        string bowPath = bowCode.Path;
        string fullCode = bowCode.ToString();

        foreach (DictionaryEntry entry in bowSettings)
        {
            if (entry.Key is not string key) continue;
            if (key.Equals(bowPath, StringComparison.OrdinalIgnoreCase) ||
                key.Equals(fullCode, StringComparison.OrdinalIgnoreCase))
            {
                return entry.Value;
            }
        }

        foreach (DictionaryEntry entry in bowSettings)
        {
            if (entry.Key is not string key) continue;
            if (!key.Contains('*') && !key.Contains('?')) continue;

            if (key.Contains(':'))
            {
                if (WildcardUtil.Match(new AssetLocation(key), bowCode)) return entry.Value;
            }
            else if (WildcardUtil.Match(key, bowPath) || WildcardUtil.Match(key, fullCode))
            {
                return entry.Value;
            }
        }

        return bowSettings.Contains("bow-generic") ? bowSettings["bow-generic"] : null;
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
        object? value = GetValue(instance, name);
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
    public bool EnableDrawTimeTweaks { get; init; } = true;
    public bool EnableHoldBreath { get; init; } = true;
    public bool EnableArcheringChargedReticle { get; init; } = true;
    public bool EnableSpeedIncreasesWithDrawWeight { get; init; } = true;
    public bool DisableVanillaReticle { get; init; } = true;
}

internal sealed class ArcheringBowAimingController : IRenderer
{
    private readonly ICoreClientAPI _api;
    private readonly Random _random = new();
    private LoadedTexture _chargedReticle;
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
    private bool _swayRegistered;
    private bool _hudRegistered;
    private Func<bool>? _shouldContinue;

    public ArcheringBowAimingController(ICoreClientAPI api)
    {
        _api = api;
        _chargedReticle = new(api);
        api.Render.GetOrLoadTexture(new AssetLocation("combatoverhaul", "gui/aiming/default-full.png"), ref _chargedReticle);
    }

    public double RenderOrder => 0.98;
    public int RenderRange => 9999;

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

        if (settings.EnableSwaying)
        {
            _api.Event.RegisterRenderer(this, EnumRenderStage.Before, "CombatOverhaulArcheringBowSway");
            _swayRegistered = true;
        }

        if (settings.EnableArcheringChargedReticle)
        {
            _api.Event.RegisterRenderer(this, EnumRenderStage.Ortho);
            _hudRegistered = true;
        }
    }

    public void Stop()
    {
        if (_swayRegistered)
        {
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Before);
            _swayRegistered = false;
        }

        if (_hudRegistered)
        {
            _api.Event.UnregisterRenderer(this, EnumRenderStage.Ortho);
            _hudRegistered = false;
        }

        _settings = null;
        _player = null;
        _shouldContinue = null;
        _isHoldingBreath = false;
        _charged = false;
    }


    public void Dispose()
    {
        Stop();
        _chargedReticle.Dispose();
    }
    public void SetCharged()
    {
        if (_charged) return;

        _charged = true;
        _flashUntilMs = _api.World.ElapsedMilliseconds + 250;
    }

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage)
    {
        if (_settings == null || _player == null) return;

        if (stage == EnumRenderStage.Ortho)
        {
            RenderChargeFlash();
            return;
        }

        if (_api.IsGamePaused || _shouldContinue?.Invoke() != true)
        {
            Stop();
            return;
        }

        ApplyHoldBreath(deltaTime);
        ApplyCameraSway(deltaTime);
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
            return;
        }

        bool wantsHoldBreath = _player.Controls.Sprint || _player.Controls.Sneak;
        _isRecovering = _currentBreath != 1f && _isRecovering;

        if (_player.Controls.RightMouseDown && wantsHoldBreath && _currentBreath > 0f && !_isRecovering && _charged)
        {
            _isHoldingBreath = true;
            _currentBreath = Math.Max(0f, _currentBreath - deltaTime / _settings.BreathDuration);
            return;
        }

        _isHoldingBreath = false;
        _isRecovering = _currentBreath == 0f || _isRecovering;
        _currentBreath = Math.Min(1f, _currentBreath + deltaTime / _settings.BreathRecoveryDuration);
        if (_currentBreath >= 1f) _isRecovering = false;
    }

    private void ApplyCameraSway(float deltaTime)
    {
        if (_settings == null || _player == null) return;

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
