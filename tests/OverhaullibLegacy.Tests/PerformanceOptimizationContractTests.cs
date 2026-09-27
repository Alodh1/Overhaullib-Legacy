using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using CombatOverhaul;
using CombatOverhaul.Colliders;
using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using CombatOverhaul.MeleeSystems;
using CombatOverhaul.RangedSystems;
using OpenTK.Mathematics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace OverhaullibLegacy.Tests;

public sealed class PerformanceOptimizationContractTests
{
    [Fact]
    public void ActionsManagerPlayerBehavior_CachesBehaviorTickListenersPerHeldItemId()
    {
        Item item = new(101);
        TickBehavior tickBehavior = new(item);
        item.CollectibleBehaviors =
        [
            new PassiveBehavior(item),
            tickBehavior
        ];
        ItemStack stack = new(item);

        int cachedItemId = -1;
        IOnGameTick[] cachedListeners = [];

        IOnGameTick[] first = InvokeGetBehaviorTickListeners(stack, ref cachedItemId, ref cachedListeners);

        Assert.Equal(item.ItemId, cachedItemId);
        Assert.Same(first, cachedListeners);
        Assert.Single(first);
        Assert.Same(tickBehavior, first[0]);

        IOnGameTick[] second = InvokeGetBehaviorTickListeners(stack, ref cachedItemId, ref cachedListeners);

        Assert.Same(first, second);
        Assert.Same(first, cachedListeners);

        Item itemWithoutTickListeners = new(202);
        ItemStack stackWithoutTickListeners = new(itemWithoutTickListeners);

        IOnGameTick[] empty = InvokeGetBehaviorTickListeners(stackWithoutTickListeners, ref cachedItemId, ref cachedListeners);

        Assert.Equal(itemWithoutTickListeners.ItemId, cachedItemId);
        Assert.Empty(empty);
        Assert.Same(empty, cachedListeners);
    }

    [Fact]
    public void ActionListener_ReusesActiveActionSnapshotUntilActionStateChanges()
    {
        ActionListener listener = (ActionListener)RuntimeHelpers.GetUninitializedObject(typeof(ActionListener));
        Dictionary<EnumEntityAction, ActionState> states = new();
        foreach (EnumEntityAction action in Enum.GetValues<EnumEntityAction>())
        {
            states[action] = ActionState.Inactive;
        }

        SetInstanceField(listener, "_actionStates", states);
        SetInstanceField(listener, "_activeActionsSnapshot", Array.Empty<EnumEntityAction>());
        SetInstanceField(listener, "_activeActionsSnapshotDirty", true);

        EnumEntityAction[] first = InvokeGetActiveActions(listener);
        EnumEntityAction[] second = InvokeGetActiveActions(listener);

        Assert.Same(first, second);
        Assert.Empty(first);

        InvokeSetActionState(listener, EnumEntityAction.LeftMouseDown, ActionState.Active);

        EnumEntityAction[] third = InvokeGetActiveActions(listener);
        EnumEntityAction[] fourth = InvokeGetActiveActions(listener);

        Assert.NotSame(first, third);
        Assert.Same(third, fourth);
        Assert.Equal([EnumEntityAction.LeftMouseDown], third);
    }

    [Fact]
    public void CollidersEntityBehavior_ReusesTransformedColliderGeometryWhenInputsAreUnchanged()
    {
        EntityPlayer entity = new();
        CollidersEntityBehavior behavior = CreateColliderBehaviorForReflectionTests(entity);

        ClientAnimator animator = (ClientAnimator)RuntimeHelpers.GetUninitializedObject(typeof(ClientAnimator));
        animator.TransformationMatrices = [];

        Assert.True(InvokeShouldRecalculateColliders(behavior, animator));
        SetInstanceField(behavior, "_collidersTransformed", true);
        Assert.False(InvokeShouldRecalculateColliders(behavior, animator));

        entity.Pos.X = 1;

        Assert.True(InvokeShouldRecalculateColliders(behavior, animator));
        Assert.False(InvokeShouldRecalculateColliders(behavior, animator));
    }

    [Fact]
    public void CollidersEntityBehavior_TransformDirtyTrackingChecksUsedJointMatricesExactly()
    {
        EntityPlayer entity = new();
        ShapeElementCollider collider = (ShapeElementCollider)RuntimeHelpers.GetUninitializedObject(typeof(ShapeElementCollider));
        collider.JointId = 0;

        CollidersEntityBehavior behavior = CreateColliderBehaviorForReflectionTests(entity);
        SetInstanceField(behavior, "<Colliders>k__BackingField", new Dictionary<string, ShapeElementCollider>
        {
            ["Torso"] = collider
        });

        ClientAnimator animator = (ClientAnimator)RuntimeHelpers.GetUninitializedObject(typeof(ClientAnimator));
        animator.TransformationMatrices = new float[16];

        Assert.True(InvokeShouldRecalculateColliders(behavior, animator));
        SetInstanceField(behavior, "_collidersTransformed", true);
        Assert.False(InvokeShouldRecalculateColliders(behavior, animator));

        animator.TransformationMatrices[5] = 1f;

        Assert.True(InvokeShouldRecalculateColliders(behavior, animator));
        Assert.False(InvokeShouldRecalculateColliders(behavior, animator));
        Assert.Null(typeof(CollidersEntityBehavior).GetMethod("GetColliderTransformSignature", BindingFlags.NonPublic | BindingFlags.Instance));
    }

    [Fact]
    public void CollidersEntityBehavior_OnGameTickSkipsServerSideWorkBeforeTimerAndSubscription()
    {
        CollidersEntityBehavior behavior = CreateColliderBehaviorForReflectionTests(new EntityPlayer());

        behavior.OnGameTick(1f);

        Assert.False(GetInstanceField<bool>(behavior, "_subscribed"));
        Assert.Equal(0f, GetInstanceField<float>(behavior, "_timeSinceLastUpdate"));
    }

    [Fact]
    public void CollidersEntityBehavior_RenderAcceptsNonAgentEntities()
    {
        MethodInfo render = typeof(CollidersEntityBehavior).GetMethod(
            nameof(CollidersEntityBehavior.Render),
            [typeof(ICoreClientAPI), typeof(Entity), typeof(EntityShapeRenderer), typeof(int)])
            ?? throw new MissingMethodException(
                typeof(CollidersEntityBehavior).FullName,
                nameof(CollidersEntityBehavior.Render));

        Assert.Equal(typeof(Entity), render.GetParameters()[1].ParameterType);
    }

    [Fact]
    public void CollidersEntityBehavior_BoundsMissingElementResolutionAndApplyConfigResetsRetryState()
    {
        CollidersEntityBehavior behavior = CreateColliderBehaviorForReflectionTests(new EntityPlayer());
        CollidersConfig config = new()
        {
            Elements = new ColliderTypesJson
            {
                Torso = ["MissingColliderElement"]
            }
        };

        InvokeApplyConfig(behavior, config);
        Assert.True(behavior.UnprocessedElementsLeft);

        int maxAttempts = (int)(typeof(CollidersEntityBehavior)
            .GetField("_maxColliderElementResolutionAttempts", BindingFlags.NonPublic | BindingFlags.Static)
            ?.GetRawConstantValue() ?? throw new MissingFieldException(typeof(CollidersEntityBehavior).FullName, "_maxColliderElementResolutionAttempts"));
        SetInstanceField(behavior, "_colliderElementResolutionAttempts", maxAttempts - 1);

        ClientAnimator animator = (ClientAnimator)RuntimeHelpers.GetUninitializedObject(typeof(ClientAnimator));
        animator.RootPoses =
        [
            new ElementPose
            {
                ForElement = new ShapeElement
                {
                    Name = "SomeOtherElement",
                    From = [0, 0, 0],
                    To = [1, 1, 1]
                }
            }
        ];
        animator.TransformationMatrices = [];

        InvokeProcessConfiguredColliderElements(behavior, animator);

        Assert.False(behavior.UnprocessedElementsLeft);
        Assert.Empty(behavior.Colliders);
        Assert.Equal(maxAttempts, GetInstanceField<int>(behavior, "_colliderElementResolutionAttempts"));

        InvokeApplyConfig(behavior, config);

        Assert.True(behavior.UnprocessedElementsLeft);
        Assert.Equal(0, GetInstanceField<int>(behavior, "_colliderElementResolutionAttempts"));
    }

    [Fact]
    public void ShapeElementCollider_RadiusSweepUsesScratchArraysInsteadOfLinqLists()
    {
        Assert.NotNull(typeof(ShapeElementCollider).GetField("_vertexScratch", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(typeof(ShapeElementCollider).GetField("_axesScratch", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(typeof(ShapeElementCollider).GetField("_halfSizeScratch", BindingFlags.NonPublic | BindingFlags.Instance));
        Assert.NotNull(typeof(ShapeElementCollider).GetField("_transformScratch", BindingFlags.NonPublic | BindingFlags.Instance));

        MethodInfo fillScratch = GetInstanceMethod(typeof(ShapeElementCollider), "FillVertexScratch");
        MethodInfo closestPoint = GetInstanceMethod(typeof(ShapeElementCollider), "ClosestPoint");
        MethodInfo radiusSweep = GetShapeElementColliderRadiusSweep(withSegmentClosestPoint: false);
        MethodInfo radiusSweepWithSegmentPoint = GetShapeElementColliderRadiusSweep(withSegmentClosestPoint: true);

        Assert.Contains(GetCalledMethods(radiusSweep), call => call == fillScratch);
        Assert.Contains(GetCalledMethods(radiusSweepWithSegmentPoint), call => call == fillScratch);

        AssertDoesNotCallLinqOrListAllocations(radiusSweep);
        AssertDoesNotCallLinqOrListAllocations(radiusSweepWithSegmentPoint);
        AssertDoesNotCallLinqOrListAllocations(closestPoint);
    }

    [Fact]
    public void AnimationPatches_DispatchesBeforeFrameDirectlyByEntityId()
    {
        Type animationPatches = GetOverhaullibType("CombatOverhaul.Integration.AnimationPatches");
        PropertyInfo animationBehaviors = animationPatches.GetProperty("AnimationBehaviors", BindingFlags.Public | BindingFlags.Static)
            ?? throw new MissingMemberException(animationPatches.FullName, "AnimationBehaviors");

        Assert.Equal(typeof(Dictionary<,>), animationBehaviors.PropertyType.GetGenericTypeDefinition());
        Assert.Equal(typeof(long), animationBehaviors.PropertyType.GetGenericArguments()[0]);

        MethodInfo dispatchBeforeFrame = GetStaticMethod(animationPatches, "DispatchBeforeFrame");
        MethodBase[] calls = GetCalledMethods(dispatchBeforeFrame).ToArray();

        Assert.Contains(calls, call => call.Name == "TryGetValue");
        Assert.DoesNotContain(calls, call => call.Name is "get_Values" or "GetEnumerator");
        AssertDoesNotCallLinqOrListAllocations(dispatchBeforeFrame);
    }

    [Fact]
    public void HarmonyPatches_CachesAnimationManagerEntityFieldAccessor()
    {
        Type harmonyPatches = GetOverhaullibType("CombatOverhaul.Integration.HarmonyPatches");
        FieldInfo getterField = harmonyPatches.GetField("_animationManagerEntityGetter", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(harmonyPatches.FullName, "_animationManagerEntityGetter");

        Assert.Equal(typeof(System.Func<,>), getterField.FieldType.GetGenericTypeDefinition());
        Assert.Equal(typeof(Vintagestory.API.Common.AnimationManager), getterField.FieldType.GetGenericArguments()[0]);
        Assert.Equal(typeof(Entity), getterField.FieldType.GetGenericArguments()[1]);

        MethodInfo createColliders = GetStaticMethod(harmonyPatches, "CreateColliders");
        MethodBase[] calls = GetCalledMethods(createColliders).ToArray();

        Assert.Contains(calls, call => call.Name == "Invoke" && call.DeclaringType?.IsGenericType == true && call.DeclaringType.GetGenericTypeDefinition() == typeof(System.Func<,>));
        Assert.DoesNotContain(calls, call => call.DeclaringType == typeof(FieldInfo) && call.Name == nameof(FieldInfo.GetValue));
    }

    [Fact]
    public void HarmonyPatches_CachesWearableLightHsvAggregation()
    {
        Type harmonyPatches = GetOverhaullibType("CombatOverhaul.Integration.HarmonyPatches");
        FieldInfo cacheField = harmonyPatches.GetField("_wearableLightHsvCache", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(harmonyPatches.FullName, "_wearableLightHsvCache");
        FieldInfo cacheDurationField = harmonyPatches.GetField("_wearableLightHsvCacheDurationMs", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(harmonyPatches.FullName, "_wearableLightHsvCacheDurationMs");

        Assert.Equal(typeof(Dictionary<,>), cacheField.FieldType.GetGenericTypeDefinition());
        Assert.Equal(typeof(long), cacheField.FieldType.GetGenericArguments()[0]);
        Assert.Equal(250L, cacheDurationField.GetRawConstantValue());

        MethodInfo lightHsv = GetStaticMethod(harmonyPatches, "LightHsv");
        MethodInfo getCachedWearableLightHsv = GetStaticMethod(harmonyPatches, "GetCachedWearableLightHsv");
        MethodBase[] cachedCalls = GetCalledMethods(getCachedWearableLightHsv).ToArray();

        Assert.Contains(GetCalledMethods(lightHsv), call => call == getCachedWearableLightHsv);
        Assert.Contains(cachedCalls, call => call.Name == "TryGetValue");
        Assert.Contains(cachedCalls, call => call.Name == "AddInventoryLights");
        Assert.Contains(cachedCalls, call => call.Name == "AddBackpackLights");
    }

    [Fact]
    public void CombatOverhaulSystem_CustomIconsLazyLoadAndCacheSvgAssets()
    {
        FieldInfo missingSvgIcons = typeof(CombatOverhaulSystem).GetField("_reportedMissingSvgIcons", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(CombatOverhaulSystem).FullName, "_reportedMissingSvgIcons");
        MethodInfo ensureLoaded = GetInstanceMethod(typeof(CombatOverhaulSystem), "EnsureLoadedSvgAsset");
        MethodInfo tryGetLoaded = GetStaticMethod(typeof(CombatOverhaulSystem), "TryGetLoadedSvgAsset");

        Assert.Equal(typeof(HashSet<>), missingSvgIcons.FieldType.GetGenericTypeDefinition());

        MethodBase[] tryGetCalls = GetCalledMethods(tryGetLoaded).ToArray();
        MethodBase[] ensureCalls = GetCalledMethods(ensureLoaded).ToArray();
        MethodInfo[] generatedIconMethods = typeof(CombatOverhaulSystem)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
            .Concat(typeof(CombatOverhaulSystem)
                .GetNestedTypes(BindingFlags.NonPublic)
                .SelectMany(type => type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)))
            .Where(method => method.Name.Contains("RegisterCustomIcon", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains(tryGetCalls, call => call.Name == "TryGet");
        Assert.Contains(tryGetCalls, call => call.Name == "IsLoaded");
        Assert.Contains(ensureCalls, call => call == tryGetLoaded);
        Assert.Contains(generatedIconMethods, method => GetCalledMethods(method).Contains(ensureLoaded));
    }

    [Fact]
    public void ProjectileCollisionRequests_UseCandidateIdsBeforePartitionAndWorldScans()
    {
        ProjectileCollisionCheckRequest request = new();

        Assert.NotNull(request.CandidateEntityIds);
        Assert.Empty(request.CandidateEntityIds);

        MethodInfo handleRequest = GetInstanceMethod(typeof(ProjectileSystemClient), "HandleRequest");
        MethodBase[] calls = GetCalledMethods(handleRequest).ToArray();

        int candidateGetterIndex = IndexOfCall(calls, "get_CandidateEntityIds");
        int getEntityByIdIndex = IndexOfCall(calls, "GetEntityById");
        int partitionIndex = IndexOfCall(calls, "TryCollidePartitionedEntities");
        int worldScanIndex = IndexOfCall(calls, "GetEntitiesAround");

        Assert.True(candidateGetterIndex >= 0);
        Assert.True(getEntityByIdIndex > candidateGetterIndex);
        Assert.True(partitionIndex > getEntityByIdIndex);
        Assert.True(worldScanIndex > partitionIndex);
    }

    [Fact]
    public void ProjectileSystemServer_BoundsProjectileCandidateIdsWithScratchList()
    {
        FieldInfo scratch = typeof(ProjectileSystemServer).GetField("_projectileCandidateScratch", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(typeof(ProjectileSystemServer).FullName, "_projectileCandidateScratch");
        FieldInfo maxCandidates = typeof(ProjectileSystemServer).GetField("MaxFirearmsProjectileCollisionCandidates", BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingFieldException(typeof(ProjectileSystemServer).FullName, "MaxFirearmsProjectileCollisionCandidates");
        MethodInfo tryCollide = typeof(ProjectileSystemServer).GetMethod(nameof(ProjectileSystemServer.TryCollide), BindingFlags.Public | BindingFlags.Instance)
            ?? throw new MissingMethodException(typeof(ProjectileSystemServer).FullName, nameof(ProjectileSystemServer.TryCollide));
        MethodInfo getCandidates = GetInstanceMethod(typeof(ProjectileSystemServer), "GetProjectileCollisionCandidateEntityIds");

        Assert.Equal(typeof(List<>), scratch.FieldType.GetGenericTypeDefinition());
        Assert.Equal(16, maxCandidates.GetRawConstantValue());

        MethodBase[] calls = GetCalledMethods(tryCollide).ToArray();
        int getCandidatesIndex = IndexOfCall(calls, getCandidates.Name);
        int candidateSetterIndex = IndexOfCall(calls, "set_CandidateEntityIds");

        Assert.True(getCandidatesIndex >= 0);
        Assert.True(candidateSetterIndex > getCandidatesIndex);
    }

    [Fact]
    public void ServerMeleeAttackExtension_AllowsProviderAttacksToBringTheirOwnStackStats()
    {
        ServerMeleeAttackStats contributedAttack = new(
            new MeleeAttackStats(),
            new ItemStackMeleeWeaponStats(
                damageMultiplier: 1.5f,
                damageBonus: 2,
                damageTierBonus: 3,
                attackSpeed: 1,
                blockTierBonus: 0,
                parryTierBonus: 0,
                thrownDamageMultiplier: 1,
                thrownDamageTierBonus: 0,
                thrownAimingDifficulty: 1,
                thrownProjectileSpeedMultiplier: 1,
                knockbackMultiplier: 2,
                armorPiercingBonus: 4));

        Assert.NotNull(contributedAttack.Attack);
        Assert.Equal(1.5f, contributedAttack.StackStats.DamageMultiplier);
        Assert.Equal(3, contributedAttack.StackStats.DamageTierBonus);
        Assert.Equal(4, contributedAttack.StackStats.ArmorPiercingBonus);

        Type builderType = typeof(MeleeSystemServer).GetNestedType("MeleeAttackLimitsBuilder", BindingFlags.NonPublic)
            ?? throw new MissingMemberException(typeof(MeleeSystemServer).FullName, "MeleeAttackLimitsBuilder");
        MethodInfo providerAdder = typeof(MeleeSystemServer)
            .GetMethods(BindingFlags.NonPublic | BindingFlags.Static)
            .Single(method =>
            {
                if (method.Name != "AddServerMeleeAttacks") return false;
                ParameterInfo[] parameters = method.GetParameters();
                return parameters.Length == 5 && parameters[4].ParameterType == typeof(IHasServerMeleeAttacks);
            });

        Assert.NotNull(builderType.GetMethod("Add", BindingFlags.Public | BindingFlags.Instance, [typeof(MeleeAttackStats), typeof(ItemStackMeleeWeaponStats)]));

        MethodBase[] calls = GetCalledMethods(providerAdder).ToArray();
        Assert.Contains(calls, call => call.Name == nameof(IHasServerMeleeAttacks.GetServerMeleeAttacks));
        Assert.Contains(calls, call => call.DeclaringType == builderType && call.Name == "Add" && call.GetParameters().Length == 2);
    }

    private static IOnGameTick[] InvokeGetBehaviorTickListeners(ItemStack stack, ref int cachedItemId, ref IOnGameTick[] cachedListeners)
    {
        MethodInfo method = GetStaticMethod(typeof(ActionsManagerPlayerBehavior), "GetBehaviorTickListeners");
        object?[] args = [stack, cachedItemId, cachedListeners];

        IOnGameTick[] result = (IOnGameTick[])method.Invoke(null, args)!;

        cachedItemId = (int)args[1]!;
        cachedListeners = (IOnGameTick[])args[2]!;
        return result;
    }

    private static EnumEntityAction[] InvokeGetActiveActions(ActionListener listener)
    {
        return (EnumEntityAction[])GetInstanceMethod(typeof(ActionListener), "GetActiveActions").Invoke(listener, null)!;
    }

    private static void InvokeSetActionState(ActionListener listener, EnumEntityAction action, ActionState state)
    {
        GetInstanceMethod(typeof(ActionListener), "SetActionState").Invoke(listener, [action, state]);
    }

    private static bool InvokeShouldRecalculateColliders(CollidersEntityBehavior behavior, ClientAnimator animator)
    {
        return (bool)GetInstanceMethod(typeof(CollidersEntityBehavior), "ShouldRecalculateColliders").Invoke(behavior, [animator])!;
    }

    private static void InvokeApplyConfig(CollidersEntityBehavior behavior, CollidersConfig config)
    {
        GetInstanceMethod(typeof(CollidersEntityBehavior), "ApplyConfig").Invoke(behavior, [config]);
    }

    private static void InvokeProcessConfiguredColliderElements(CollidersEntityBehavior behavior, ClientAnimator animator)
    {
        GetInstanceMethod(typeof(CollidersEntityBehavior), "ProcessConfiguredColliderElements").Invoke(behavior, [animator]);
    }

    private static MethodInfo GetShapeElementColliderRadiusSweep(bool withSegmentClosestPoint)
    {
        Type[] parameters = withSegmentClosestPoint
            ? [typeof(Vector3d), typeof(Vector3d), typeof(double), typeof(double).MakeByRefType(), typeof(Vector3d).MakeByRefType(), typeof(Vector3d).MakeByRefType()]
            : [typeof(Vector3d), typeof(Vector3d), typeof(double), typeof(double).MakeByRefType(), typeof(Vector3d).MakeByRefType()];

        return typeof(ShapeElementCollider).GetMethod(nameof(ShapeElementCollider.Collide), BindingFlags.Public | BindingFlags.Instance, parameters)
            ?? throw new MissingMethodException(typeof(ShapeElementCollider).FullName, nameof(ShapeElementCollider.Collide));
    }

    private static MethodInfo GetInstanceMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static MethodInfo GetStaticMethod(Type type, string name)
    {
        return type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException(type.FullName, name);
    }

    private static Type GetOverhaullibType(string name)
    {
        return typeof(ActionListener).Assembly.GetType(name, throwOnError: true)!;
    }

    private static void SetInstanceField(object instance, string fieldName, object? value, Type? declaringType = null)
    {
        Type type = declaringType ?? instance.GetType();
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        field.SetValue(instance, value);
    }

    private static T GetInstanceField<T>(object instance, string fieldName, Type? declaringType = null)
    {
        Type type = declaringType ?? instance.GetType();
        FieldInfo field = type.GetField(fieldName, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingFieldException(type.FullName, fieldName);
        return (T)field.GetValue(instance)!;
    }

    private static CollidersEntityBehavior CreateColliderBehaviorForReflectionTests(Entity entity)
    {
        CollidersEntityBehavior behavior = (CollidersEntityBehavior)RuntimeHelpers.GetUninitializedObject(typeof(CollidersEntityBehavior));
        SetInstanceField(behavior, "entity", entity, typeof(EntityBehavior));
        SetInstanceField(behavior, "<Colliders>k__BackingField", new Dictionary<string, ShapeElementCollider>());
        SetInstanceField(behavior, "<CollidersTypes>k__BackingField", new Dictionary<string, ColliderTypes>());
        SetInstanceField(behavior, "<ShapeElementsToProcess>k__BackingField", new HashSet<string>());
        SetInstanceField(behavior, "_lastColliderJointIds", Array.Empty<int>());
        SetInstanceField(behavior, "_lastColliderJointMatrices", Array.Empty<float>());
        return behavior;
    }

    private static void AssertDoesNotCallLinqOrListAllocations(MethodInfo method)
    {
        MethodBase[] calls = GetCalledMethods(method).ToArray();

        Assert.DoesNotContain(calls, call => call.DeclaringType == typeof(Enumerable));
        Assert.DoesNotContain(calls, call => call is ConstructorInfo && call.DeclaringType?.IsGenericType == true && call.DeclaringType.GetGenericTypeDefinition() == typeof(List<>));
    }

    private static int IndexOfCall(IReadOnlyList<MethodBase> calls, string methodName)
    {
        for (int index = 0; index < calls.Count; index++)
        {
            if (calls[index].Name == methodName) return index;
        }

        return -1;
    }

    private static IEnumerable<MethodBase> GetCalledMethods(MethodInfo method)
    {
        byte[] il = method.GetMethodBody()?.GetILAsByteArray() ?? [];
        Module module = method.Module;
        Type[] typeArguments = method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : Type.EmptyTypes;
        Type[] methodArguments = method.IsGenericMethod ? method.GetGenericArguments() : Type.EmptyTypes;

        for (int offset = 0; offset < il.Length;)
        {
            OpCode opcode = ReadOpCode(il, ref offset);

            if (opcode.OperandType == OperandType.InlineMethod)
            {
                int metadataToken = BitConverter.ToInt32(il, offset);
                offset += 4;

                MethodBase? calledMethod = ResolveMethod(module, metadataToken, typeArguments, methodArguments);
                if (calledMethod != null)
                {
                    yield return calledMethod;
                }

                continue;
            }

            offset += GetOperandSize(opcode, il, offset);
        }
    }

    private static MethodBase? ResolveMethod(Module module, int metadataToken, Type[] typeArguments, Type[] methodArguments)
    {
        try
        {
            return module.ResolveMethod(metadataToken, typeArguments, methodArguments);
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (BadImageFormatException)
        {
            return null;
        }
    }

    private static OpCode ReadOpCode(byte[] il, ref int offset)
    {
        byte value = il[offset++];
        if (value != 0xFE)
        {
            return SingleByteOpCodes[value];
        }

        return MultiByteOpCodes[il[offset++]];
    }

    private static int GetOperandSize(OpCode opcode, byte[] il, int offset)
    {
        return opcode.OperandType switch
        {
            OperandType.InlineNone => 0,
            OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
            OperandType.InlineVar => 2,
            OperandType.InlineBrTarget or OperandType.InlineField or OperandType.InlineI or OperandType.InlineMethod or OperandType.InlineSig or OperandType.InlineString or OperandType.InlineTok or OperandType.InlineType or OperandType.ShortInlineR => 4,
            OperandType.InlineI8 or OperandType.InlineR => 8,
            OperandType.InlineSwitch => 4 + BitConverter.ToInt32(il, offset) * 4,
            _ => throw new NotSupportedException($"Unsupported operand type {opcode.OperandType}")
        };
    }

    private static readonly OpCode[] SingleByteOpCodes = new OpCode[0x100];
    private static readonly OpCode[] MultiByteOpCodes = new OpCode[0x100];

    static PerformanceOptimizationContractTests()
    {
        foreach (FieldInfo field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is not OpCode opcode) continue;

            ushort value = (ushort)opcode.Value;
            if (value < 0x100)
            {
                SingleByteOpCodes[value] = opcode;
            }
            else if ((value & 0xFF00) == 0xFE00)
            {
                MultiByteOpCodes[value & 0xFF] = opcode;
            }
        }
    }

    private sealed class TickBehavior(CollectibleObject collectible) : CollectibleBehavior(collectible), IOnGameTick
    {
        public void OnGameTick(ItemSlot slot, EntityPlayer player, ref int state, bool mainHand)
        {
        }
    }

    private sealed class PassiveBehavior(CollectibleObject collectible) : CollectibleBehavior(collectible)
    {
    }
}
