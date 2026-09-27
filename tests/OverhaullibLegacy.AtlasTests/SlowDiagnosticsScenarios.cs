using System.Linq.Expressions;
using System.Reflection;
using Atlas.XUnit;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class SlowDiagnosticsScenarios : AtlasScenarioBase
{
    private const BindingFlags StaticNonPublic = BindingFlags.Static | BindingFlags.NonPublic;
    private const BindingFlags StaticPublic = BindingFlags.Static | BindingFlags.Public;
    private const BindingFlags InstanceNonPublic = BindingFlags.Instance | BindingFlags.NonPublic;
    private const BindingFlags InstancePublic = BindingFlags.Instance | BindingFlags.Public;
    private const string OverhaullibAssemblyName = "OverhaullibLegacyCompat";

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task ClientRendererDisposal_Should_Dispatch_RendererCleanup_To_MainThread()
    {
        await World.Ticks(5);

        Assembly overhaullib = RequireAssembly(OverhaullibAssemblyName);
        Type cleanupType = RequireType(overhaullib, "CombatOverhaul.Utils.ClientThreadCleanup");
        MethodInfo disposeRenderer = RequireMethod(cleanupType, "DisposeRenderer", StaticPublic);
        MethodInfo criticalDispose = RequireMethod(RequireType(overhaullib, "CombatOverhaul.Integration.CriticalHitFeedback"), "Dispose", StaticPublic);
        MethodInfo combatDispose = RequireMethod(RequireType(overhaullib, "CombatOverhaul.CombatOverhaulSystem"), "Dispose", InstancePublic);

        Assert.True(MethodCalls(criticalDispose, disposeRenderer), "CriticalHitFeedback.Dispose must dispatch renderer cleanup through ClientThreadCleanup.");
        Assert.True(MethodCalls(combatDispose, disposeRenderer), "CombatOverhaulSystem.Dispose must dispatch HUD renderer cleanup through ClientThreadCleanup.");
    }

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task RangedStatusDiagnostics_Should_Record_SlowSubscriber()
    {
        await World.Ticks(5);

        Assembly overhaullib = RequireAssembly(OverhaullibAssemblyName);
        object system = GetCombatOverhaulSystem(overhaullib);
        object settings = RequireProperty(system.GetType(), "Settings", InstancePublic).GetValue(system)
            ?? throw new InvalidOperationException("CombatOverhaulSystem.Settings was null.");
        object rangedSystem = RequireProperty(system.GetType(), "ServerRangedWeaponSystem", InstancePublic).GetValue(system)
            ?? throw new InvalidOperationException("CombatOverhaulSystem.ServerRangedWeaponSystem was null.");

        Type diagnosticsType = RequireType(overhaullib, "CombatOverhaul.SlowDiagnostics");
        EventInfo diagnosticLogEvent = RequireEvent(diagnosticsType, "DiagnosticLogWritten", StaticNonPublic);
        List<string> diagnosticLines = [];
        Action<string> captureDiagnosticLine = diagnosticLines.Add;

        EventInfo rangedStatusEvent = RequireEvent(rangedSystem.GetType(), "RangedWeaponStatusChanged", InstancePublic);
        Delegate slowSubscriber = CreateSlowSubscriber(rangedStatusEvent.EventHandlerType
            ?? throw new InvalidOperationException("RangedWeaponStatusChanged has no event handler type."));

        try
        {
            SetSetting(settings, "SlowDiagnosticsEnabled", true);
            SetSetting(settings, "SlowDiagnosticsRangedStatusThresholdMs", 1);
            SetSetting(settings, "SlowDiagnosticsLogCooldownMs", 0);
            ConfigureDiagnostics(overhaullib, settings);

            AddEventHandler(diagnosticLogEvent, null, captureDiagnosticLine);
            AddEventHandler(rangedStatusEvent, rangedSystem, slowSubscriber);

            var player = await World.JoinPlayer("SlowDiagPlayer");
            object packet = CreateRangedWeaponStatusPacket(overhaullib);
            MethodInfo handlePacket = RequireMethod(rangedSystem.GetType(), "HandleWeaponStatusPacket", InstanceNonPublic);

            handlePacket.Invoke(rangedSystem, [player.Player, packet]);

            Assert.Contains(diagnosticLines, line =>
                line.Contains("Slow ranged status subscriber", StringComparison.Ordinal)
                && line.Contains("status=TriggeredShot", StringComparison.Ordinal)
                && line.Contains("subscriber='", StringComparison.Ordinal));
        }
        finally
        {
            RemoveEventHandler(rangedStatusEvent, rangedSystem, slowSubscriber);
            RemoveEventHandler(diagnosticLogEvent, null, captureDiagnosticLine);

            SetSetting(settings, "SlowDiagnosticsEnabled", false);
            ConfigureDiagnostics(overhaullib, settings);
        }
    }

    [AtlasScenario(TimeoutMs = 120000)]
    public async Task EntityColliders_Should_InitializeAndKeepAabbFallbackAfterReload()
    {
        await World.Ticks(5);

        Assembly overhaullib = RequireAssembly(OverhaullibAssemblyName);
        var player = await World.JoinPlayer("ColFallback");
        EntityPlayer entity = player.Entity;
        object colliders = entity.GetBehavior("CombatOverhaul:EntityColliders")
            ?? throw new InvalidOperationException("Player did not receive CombatOverhaul:EntityColliders.");

        PropertyInfo hasDetailedColliders = RequireProperty(colliders.GetType(), "HasUsableDetailedColliders", InstancePublic);
        Assert.False((bool)hasDetailedColliders.GetValue(colliders)!);
        AssertAabbFallbackCanHitPlayer(colliders, entity);

        object config = CreateMissingElementColliderConfig(overhaullib);
        RequireMethod(colliders.GetType(), "ApplyConfig", InstanceNonPublic).Invoke(colliders, [config]);

        Assert.True((bool)RequireProperty(colliders.GetType(), "UnprocessedElementsLeft", InstancePublic).GetValue(colliders)!);
        AssertAabbFallbackCanHitPlayer(colliders, entity);
    }

    private object GetCombatOverhaulSystem(Assembly overhaullib)
    {
        const string systemName = "CombatOverhaul.CombatOverhaulSystem";
        RequireType(overhaullib, systemName);

        MethodInfo getModSystem = World.Api.ModLoader.GetType().GetMethod("GetModSystem", InstancePublic, [typeof(string)])
            ?? throw new MissingMethodException(World.Api.ModLoader.GetType().FullName, "GetModSystem");

        return getModSystem.Invoke(World.Api.ModLoader, [systemName])
            ?? throw new InvalidOperationException("CombatOverhaulSystem was not loaded.");
    }

    private void ConfigureDiagnostics(Assembly overhaullib, object settings)
    {
        Type patchesManager = RequireType(overhaullib, "CombatOverhaul.HarmonyPatchesManager");
        MethodInfo configure = RequireMethod(patchesManager, "ConfigureDiagnostics", StaticPublic);
        configure.Invoke(null, [World.Api, settings]);
    }

    private static object CreateRangedWeaponStatusPacket(Assembly overhaullib)
    {
        Type packetType = RequireType(overhaullib, "CombatOverhaul.RangedSystems.RangedWeaponStatusPacket");
        Type statusType = RequireType(overhaullib, "CombatOverhaul.RangedSystems.RangedWeaponStatus");
        object packet = Activator.CreateInstance(packetType)
            ?? throw new InvalidOperationException("Could not create RangedWeaponStatusPacket.");

        RequireProperty(packetType, "Status", InstancePublic).SetValue(packet, Enum.Parse(statusType, "TriggeredShot"));
        RequireProperty(packetType, "MainHand", InstancePublic).SetValue(packet, true);
        return packet;
    }

    private static object CreateMissingElementColliderConfig(Assembly overhaullib)
    {
        Type configType = RequireType(overhaullib, "CombatOverhaul.Colliders.CollidersConfig");
        Type elementsType = RequireType(overhaullib, "CombatOverhaul.Colliders.ColliderTypesJson");
        object config = Activator.CreateInstance(configType)
            ?? throw new InvalidOperationException("Could not create CollidersConfig.");
        object elements = Activator.CreateInstance(elementsType)
            ?? throw new InvalidOperationException("Could not create ColliderTypesJson.");

        RequireProperty(elementsType, "Torso", InstancePublic).SetValue(elements, new[] { "AtlasMissingElement" });
        RequireProperty(configType, "Elements", InstancePublic).SetValue(config, elements);
        return config;
    }

    private static void AssertAabbFallbackCanHitPlayer(object colliders, EntityPlayer entity)
    {
        MethodInfo collide = colliders.GetType().GetMethods(InstancePublic)
            .Single(method =>
            {
                if (method.Name != "Collide") return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 6
                    && parameters[2].ParameterType == typeof(float)
                    && parameters[3].ParameterType == typeof(string).MakeByRefType()
                    && parameters[4].ParameterType == typeof(double).MakeByRefType();
            });

        Type vectorType = collide.GetParameters()[0].ParameterType;
        Cuboidf box = entity.CollisionBox;
        EntityPos pos = entity.Pos;
        double centerX = pos.X + (box.X1 + box.X2) / 2;
        double centerY = pos.Y + (box.Y1 + box.Y2) / 2;
        double minZ = pos.Z + Math.Min(box.Z1, box.Z2);
        double maxZ = pos.Z + Math.Max(box.Z1, box.Z2);
        object previous = Activator.CreateInstance(vectorType, centerX, centerY, minZ - 1)
            ?? throw new InvalidOperationException($"Could not create {vectorType.FullName}.");
        object current = Activator.CreateInstance(vectorType, centerX, centerY, maxZ + 1)
            ?? throw new InvalidOperationException($"Could not create {vectorType.FullName}.");
        object intersection = Activator.CreateInstance(vectorType)
            ?? throw new InvalidOperationException($"Could not create {vectorType.FullName}.");
        object?[] args = [current, previous, 0.05f, null, 0d, intersection];

        Assert.True((bool)collide.Invoke(colliders, args)!);
    }

    private static Delegate CreateSlowSubscriber(Type eventHandlerType)
    {
        MethodInfo invoke = eventHandlerType.GetMethod("Invoke")
            ?? throw new MissingMethodException(eventHandlerType.FullName, "Invoke");
        ParameterExpression[] parameters = invoke.GetParameters()
            .Select(parameter => Expression.Parameter(parameter.ParameterType, parameter.Name))
            .ToArray();
        MethodInfo bridge = RequireMethod(typeof(SlowDiagnosticsScenarios), nameof(SlowSubscriberBridge), StaticNonPublic);

        MethodCallExpression body = Expression.Call(
            bridge,
            parameters.Select(parameter => Expression.Convert(parameter, typeof(object))));

        return Expression.Lambda(eventHandlerType, body, parameters).Compile();
    }

    private static void SlowSubscriberBridge(object attacker, object weaponSlot, object status)
    {
        Thread.Sleep(25);
    }

    private static void SetSetting(object settings, string propertyName, object value)
    {
        RequireProperty(settings.GetType(), propertyName, InstancePublic).SetValue(settings, value);
    }

    private static void AddEventHandler(EventInfo eventInfo, object? target, Delegate handler)
    {
        MethodInfo addMethod = eventInfo.GetAddMethod(nonPublic: true)
            ?? throw new MissingMethodException(eventInfo.DeclaringType?.FullName, $"add_{eventInfo.Name}");
        addMethod.Invoke(target, [handler]);
    }

    private static void RemoveEventHandler(EventInfo eventInfo, object? target, Delegate handler)
    {
        MethodInfo removeMethod = eventInfo.GetRemoveMethod(nonPublic: true)
            ?? throw new MissingMethodException(eventInfo.DeclaringType?.FullName, $"remove_{eventInfo.Name}");
        removeMethod.Invoke(target, [handler]);
    }

    private static bool MethodCalls(MethodInfo caller, MethodInfo target)
    {
        byte[] il = caller.GetMethodBody()?.GetILAsByteArray() ?? [];
        Module module = caller.Module;

        for (int index = 0; index < il.Length - sizeof(int); index++)
        {
            if (il[index] is not (0x28 or 0x6F))
            {
                continue;
            }

            int metadataToken = BitConverter.ToInt32(il, index + 1);
            MethodBase? calledMethod;
            try
            {
                calledMethod = module.ResolveMethod(metadataToken);
            }
            catch (ArgumentException)
            {
                continue;
            }

            if (calledMethod?.Module == target.Module && calledMethod.MetadataToken == target.MetadataToken)
            {
                return true;
            }
        }

        return false;
    }

    private static Assembly RequireAssembly(string assemblyName)
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == assemblyName)
            ?? throw new InvalidOperationException($"{assemblyName} was not loaded by Atlas.");
    }

    private static Type RequireType(Assembly assembly, string fullName)
    {
        return assembly.GetType(fullName, throwOnError: false)
            ?? throw new InvalidOperationException($"{fullName} was not found in {assembly.GetName().Name}.");
    }

    private static MethodInfo RequireMethod(Type type, string name, BindingFlags flags)
    {
        return type.GetMethod(name, flags)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static PropertyInfo RequireProperty(Type type, string name, BindingFlags flags)
    {
        return type.GetProperty(name, flags)
            ?? throw new MissingMemberException(type.FullName, name);
    }

    private static EventInfo RequireEvent(Type type, string name, BindingFlags flags)
    {
        return type.GetEvent(name, flags)
            ?? throw new MissingMemberException(type.FullName, name);
    }
}
