using CombatOverhaul.Armor;
using CombatOverhaul.Inputs;
using CombatOverhaul.Implementations;
using CombatOverhaul.Utils;
using CombatOverhaul.Vanity;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class HarmonyPatches
{
    public static Settings ClientSettings { get; set; } = new();
    public static Settings ServerSettings { get; set; } = new();

    // Set to true only while testing. It can spam client-main.log.
    private static readonly bool DebugWearableLights = false;

    private static ICoreAPI? _api;
    internal static readonly HashSet<long> _reportedEntities = new();
    private static Type? _offhandSlotType;

    private static readonly System.Func<Vintagestory.API.Common.AnimationManager, Entity?> _animationManagerEntityGetter = CreateAnimationManagerEntityGetter();
    private static readonly FieldInfo? _smoothedBodyYaw = typeof(EntityPlayerShapeRenderer).GetField("smoothedBodyYaw", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly Dictionary<long, WearableLightHsvCacheEntry> _wearableLightHsvCache = [];
    private const long _wearableLightHsvCacheDurationMs = 250;
    private const int _wearableLightHsvCachePruneThreshold = 256;
    private static readonly List<long> _wearableLightHsvPruneScratch = [];
    private static bool _reportedWearableLightHsvException;

    private const string _fallDamageThresholdMultiplierStat = "fallDamageThreshold";
    private const float _fallDamageMultiplier = 0.2f;
    private const float _fallDamageSpeedThreshold = 0.1f;
    private const double _newFallDistance = 4.5;

    public static void Patch(string harmonyId, ICoreAPI api)
    {
        _api = api;
        _reportedEntities.Clear();
        _wearableLightHsvCache.Clear();

        Harmony harmony = new(harmonyId);

        harmony.Patch(
            typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(CreateColliders)))
        );

        harmony.Patch(
            typeof(EntityPlayerShapeRenderer).GetMethod("smoothCameraTurning", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(SmoothCameraTurning)))
        );

        if (!api.ModLoader.IsModEnabled("svanaxfdc"))
        {
            harmony.Patch(
                typeof(EntityBehaviorHealth).GetMethod("OnFallToGround", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(OnFallToGround)))
            );
        }

        harmony.Patch(
            typeof(BagInventory).GetMethod("ReloadBagInventory", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(ReloadBagInventory)))
        );

        MethodInfo? lightHsvGetter = GetEntityPlayerLightHsvGetter();

        if (lightHsvGetter == null)
        {
            api.Logger.Warning("[OverhaullibLegacyCompat] Could not find EntityPlayer.LightHsv getter. Wearable lights will not work.");
        }
        else
        {
            api.Logger.Notification("[OverhaullibLegacyCompat] Patching EntityPlayer.LightHsv getter for wearable lights.");

            harmony.Patch(
                lightHsvGetter,
                postfix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(LightHsv)))
            );
        }

        harmony.Patch(
            typeof(BagInventory).GetMethod("SaveSlotIntoBag", AccessTools.all),
            prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(BagInventory_SaveSlotIntoBag)))
        );

        TryPatchStopRaiseShieldAnim(harmony, api);
        TryPatchVanillaShieldIdle(harmony, api);
        TryPatchVanillaShieldTooltip(harmony, api);
        TryPatchOffhandDaggerSlot(harmony, api);
        TryPatchVanillaArmorStandInteract(harmony, api);
        TryPatchVanillaBowInteract(harmony, api);

        // BehaviorHealingItem was removed/renamed in newer VS versions.
        // Skip that old patch; it is unrelated to wearable lights.
    }

    public static void Unpatch(string harmonyId, ICoreAPI api)
    {
        Harmony harmony = new(harmonyId);

        harmony.Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        harmony.Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("smoothCameraTurning", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        harmony.Unpatch(typeof(BagInventory).GetMethod("ReloadBagInventory", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);

        MethodInfo? lightHsvGetter = GetEntityPlayerLightHsvGetter();
        if (lightHsvGetter != null)
        {
            harmony.Unpatch(lightHsvGetter, HarmonyPatchType.Postfix, harmonyId);
        }

        harmony.Unpatch(typeof(BagInventory).GetMethod("SaveSlotIntoBag", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        TryUnpatchStopRaiseShieldAnim(harmony, harmonyId);
        TryUnpatchVanillaShieldIdle(harmony, harmonyId);
        TryUnpatchVanillaShieldTooltip(harmony, harmonyId);
        TryUnpatchOffhandDaggerSlot(harmony, harmonyId);
        TryUnpatchVanillaArmorStandInteract(harmony, harmonyId);
        TryUnpatchVanillaBowInteract(harmony, harmonyId);
        // Old BehaviorHealingItem patch skipped; nothing to unpatch here.

        if (!api.ModLoader.IsModEnabled("svanaxfdc"))
        {
            harmony.Unpatch(typeof(EntityBehaviorHealth).GetMethod("OnFallToGround", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        }

        _api = null;
        _wearableLightHsvCache.Clear();
    }

    private static void TryPatchVanillaBowInteract(Harmony harmony, ICoreAPI api)
    {
        MethodInfo? method = AccessTools.Method(
            typeof(ItemBow),
            nameof(ItemBow.OnHeldInteractStart),
            [typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(bool), typeof(EnumHandHandling).MakeByRefType()]
        );

        if (method == null)
        {
            api.Logger.Warning("[OverhaullibLegacyCompat] Could not find vanilla ItemBow.OnHeldInteractStart. Vanilla-style bows may ignore the bow two-handed setting.");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(VanillaBow_OnHeldInteractStart_Prefix))));
    }

    private static void TryUnpatchVanillaBowInteract(Harmony harmony, string harmonyId)
    {
        MethodInfo? method = AccessTools.Method(
            typeof(ItemBow),
            nameof(ItemBow.OnHeldInteractStart),
            [typeof(ItemSlot), typeof(EntityAgent), typeof(BlockSelection), typeof(EntitySelection), typeof(bool), typeof(EnumHandHandling).MakeByRefType()]
        );

        if (method != null)
        {
            harmony.Unpatch(method, HarmonyPatchType.Prefix, harmonyId);
        }
    }

    private static bool VanillaBow_OnHeldInteractStart_Prefix(ItemSlot slot, EntityAgent byEntity, ref EnumHandHandling handling)
    {
        Settings settings = byEntity.World.Side == EnumAppSide.Client ? ClientSettings : ServerSettings;
        if (!settings.BowTwoHanded || byEntity is not EntityPlayer player) return true;

        bool mainHand = IsSameHandSlot(slot, player.RightHandItemSlot);
        bool offHand = IsSameHandSlot(slot, player.LeftHandItemSlot);

        if (!mainHand && !offHand)
        {
            mainHand = true;
        }

        if (mainHand && !player.LeftHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(typeof(ItemBow), "offhandShouldBeEmpty", Lang.Get("combatoverhaul:message-offhand-empty"));
            handling = EnumHandHandling.PreventDefault;
            return false;
        }

        if (offHand && !player.RightHandItemSlot.Empty)
        {
            (player.World.Api as ICoreClientAPI)?.TriggerIngameError(typeof(ItemBow), "mainHandShouldBeEmpty", Lang.Get("combatoverhaul:message-mainhand-empty"));
            handling = EnumHandHandling.PreventDefault;
            return false;
        }

        return true;
    }

    private static bool IsSameHandSlot(ItemSlot slot, ItemSlot handSlot)
    {
        return ReferenceEquals(slot, handSlot) || (slot.Itemstack != null && ReferenceEquals(slot.Itemstack, handSlot.Itemstack));
    }


    private static void TryPatchVanillaArmorStandInteract(Harmony harmony, ICoreAPI api)
    {
        MethodInfo? method = GetVanillaArmorStandOnInteractMethod();
        if (method == null)
        {
            api.Logger.Warning("[OverhaullibLegacyCompat] Could not find vanilla EntityArmorStand.OnInteract. Ruin armor stands may keep vanilla-only interaction.");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(VanillaArmorStand_OnInteract_Prefix))));
    }

    private static void TryUnpatchVanillaArmorStandInteract(Harmony harmony, string harmonyId)
    {
        MethodInfo? method = GetVanillaArmorStandOnInteractMethod();
        if (method != null)
        {
            harmony.Unpatch(method, HarmonyPatchType.Prefix, harmonyId);
        }
    }

    private static MethodInfo? GetVanillaArmorStandOnInteractMethod()
    {
        return AccessTools.Method(
            typeof(EntityArmorStand),
            nameof(Entity.OnInteract),
            [typeof(EntityAgent), typeof(ItemSlot), typeof(Vec3d), typeof(EnumInteractMode)]
        );
    }

    private static bool VanillaArmorStand_OnInteract_Prefix(EntityArmorStand __instance, EntityAgent byEntity, ItemSlot slot, Vec3d hitPosition, EnumInteractMode mode)
    {
        EntityBehaviorCOArmorStandInventory? behavior = __instance.GetBehavior<EntityBehaviorCOArmorStandInventory>();
        if (behavior == null)
        {
            return true;
        }

        return !behavior.HandleInteract(byEntity, slot, hitPosition, mode);
    }

    private static void TryPatchOffhandDaggerSlot(Harmony harmony, ICoreAPI api)
    {
        _offhandSlotType =
            AccessTools.TypeByName("Vintagestory.GameContent.ItemSlotOffhand")
            ?? AccessTools.TypeByName("Vintagestory.API.Common.ItemSlotOffhand");

        if (_offhandSlotType == null)
        {
            api.Logger.Warning("[OverhaullibLegacyCompat] Could not find offhand item slot type. Offhand daggers may not be placeable.");
            return;
        }

        // ItemSlotOffhand does not implement CanTakeFrom itself in VS 1.22, it inherits the
        // base ItemSlot method. Harmony cannot patch an inherited method through the child
        // type, so patch a declared child method if it exists, otherwise patch the declared
        // base ItemSlot method and gate the prefix to offhand slots only.
        MethodInfo? canTakeFrom = AccessTools.DeclaredMethod(
            _offhandSlotType,
            nameof(ItemSlot.CanTakeFrom),
            [typeof(ItemSlot), typeof(EnumMergePriority)]
        ) ?? AccessTools.Method(
            typeof(ItemSlot),
            nameof(ItemSlot.CanTakeFrom),
            [typeof(ItemSlot), typeof(EnumMergePriority)]
        );

        if (canTakeFrom != null)
        {
            harmony.Patch(canTakeFrom, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(OffhandDaggerSlot_CanTakeFrom_Prefix))));
        }

        MethodInfo? canHold = AccessTools.DeclaredMethod(
            _offhandSlotType,
            nameof(ItemSlot.CanHold),
            [typeof(ItemSlot)]
        ) ?? AccessTools.Method(
            typeof(ItemSlot),
            nameof(ItemSlot.CanHold),
            [typeof(ItemSlot)]
        );

        if (canHold != null)
        {
            harmony.Patch(canHold, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(OffhandDaggerSlot_CanHold_Prefix))));
        }

    }

    private static void TryUnpatchOffhandDaggerSlot(Harmony harmony, string harmonyId)
    {
        MethodInfo? canTakeFrom = (_offhandSlotType == null ? null : AccessTools.DeclaredMethod(
            _offhandSlotType,
            nameof(ItemSlot.CanTakeFrom),
            [typeof(ItemSlot), typeof(EnumMergePriority)]
        )) ?? AccessTools.Method(
            typeof(ItemSlot),
            nameof(ItemSlot.CanTakeFrom),
            [typeof(ItemSlot), typeof(EnumMergePriority)]
        );

        if (canTakeFrom != null)
        {
            harmony.Unpatch(canTakeFrom, HarmonyPatchType.Prefix, harmonyId);
        }

        MethodInfo? canHold = (_offhandSlotType == null ? null : AccessTools.DeclaredMethod(
            _offhandSlotType,
            nameof(ItemSlot.CanHold),
            [typeof(ItemSlot)]
        )) ?? AccessTools.Method(
            typeof(ItemSlot),
            nameof(ItemSlot.CanHold),
            [typeof(ItemSlot)]
        );

        if (canHold != null)
        {
            harmony.Unpatch(canHold, HarmonyPatchType.Prefix, harmonyId);
        }

        _offhandSlotType = null;
    }

    private static bool OffhandDaggerSlot_CanTakeFrom_Prefix(ItemSlot __instance, ItemSlot sourceSlot, ref bool __result)
    {
        if (IsOffhandSlot(__instance) && IsOffhandDagger(sourceSlot?.Itemstack))
        {
            __result = true;
            return false;
        }

        return true;
    }

    private static bool OffhandDaggerSlot_CanHold_Prefix(ItemSlot __instance, ItemSlot sourceSlot, ref bool __result)
    {
        if (IsOffhandSlot(__instance) && IsOffhandDagger(sourceSlot?.Itemstack))
        {
            __result = true;
            return false;
        }

        return true;
    }

    private static bool IsOffhandSlot(ItemSlot slot)
    {
        Type slotType = slot.GetType();

        if (_offhandSlotType != null && _offhandSlotType.IsAssignableFrom(slotType)) return true;

        string fullName = slotType.FullName ?? string.Empty;
        return fullName == "Vintagestory.GameContent.ItemSlotOffhand"
            || fullName == "Vintagestory.API.Common.ItemSlotOffhand"
            || fullName.EndsWith(".ItemSlotOffhand", StringComparison.Ordinal);
    }

    private static bool IsOffhandDagger(ItemStack? stack)
    {
        return CollectibleClassifier.IsDagger(stack);
    }

    private static MethodInfo? GetEntityPlayerLightHsvGetter()
    {
        return AccessTools.PropertyGetter(typeof(EntityPlayer), nameof(EntityPlayer.LightHsv))
            ?? typeof(EntityPlayer).GetProperty(nameof(EntityPlayer.LightHsv), AccessTools.all)?.GetGetMethod(true);
    }

    private static System.Func<Vintagestory.API.Common.AnimationManager, Entity?> CreateAnimationManagerEntityGetter()
    {
        FieldInfo? entityField = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);
        if (entityField == null)
        {
            return _ => null;
        }

        try
        {
            ParameterExpression instance = Expression.Parameter(typeof(Vintagestory.API.Common.AnimationManager), "instance");
            UnaryExpression field = Expression.Convert(Expression.Field(instance, entityField), typeof(Entity));
            return Expression.Lambda<System.Func<Vintagestory.API.Common.AnimationManager, Entity?>>(field, instance).Compile();
        }
        catch
        {
            return instance => entityField.GetValue(instance) as Entity;
        }
    }

    private static bool CreateColliders(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {
        EntityPlayer? entity = _animationManagerEntityGetter(__instance) as EntityPlayer;

        if (entity?.Api?.Side != EnumAppSide.Client) return true;

        ClientAnimator? animator = __instance.Animator as ClientAnimator;
        if (animator == null) return true;

        AnimationPatches.Animators?.Add(animator, entity);

        return true;
    }

    private static bool SmoothCameraTurning(EntityPlayerShapeRenderer __instance, float bodyYaw, float mdt)
    {
        if (!ClientSettings.HandsYawSmoothing)
        {
            _smoothedBodyYaw?.SetValue(__instance, bodyYaw);
            return false;
        }

        return true;
    }

    private static bool OnFallToGround(EntityBehaviorHealth __instance, ref double withYMotion)
    {
        if ((__instance.entity as EntityAgent)?.ServerControls.Gliding == true)
        {
            return true;
        }

        if (__instance.entity is not EntityPlayer player)
        {
            return true;
        }

        Vec3d positionBeforeFalling = __instance.entity.PositionBeforeFalling;
        double fallDistance = (positionBeforeFalling.Y - player.Pos.Y) / Math.Max(player.Stats.GetBlended(_fallDamageThresholdMultiplierStat), 0.001);

        if (fallDistance < _newFallDistance) return false;
        if (Math.Abs(withYMotion) < _fallDamageSpeedThreshold) return false;

        double fallDamage = Math.Max(0, fallDistance - _newFallDistance) * player.Properties.FallDamageMultiplier * _fallDamageMultiplier;

        player.ReceiveDamage(new DamageSource()
        {
            Source = EnumDamageSource.Fall,
            Type = EnumDamageType.Gravity,
            IgnoreInvFrames = true,
        }, (float)fallDamage);

        return false;
    }

    private static double CurrentBelowBlockHeight(EntityAgent player)
    {
        double height = player.SidedPos.Y;
        IBlockAccessor accessor = _api.World.GetBlockAccessor(false, false, false);

        int heightDiff = 1;
        while (heightDiff < height)
        {
            BlockPos blockPos = player.SidedPos.AsBlockPos;
            blockPos.Y -= heightDiff;

            BlockPos bp0 = blockPos.Copy();
            BlockPos bp1 = blockPos.Copy();
            BlockPos bp2 = blockPos.Copy();
            BlockPos bp3 = blockPos.Copy();

            Vec3d entityPosPos = player.SidedPos.XYZ;

            float xDiff = player.CollisionBox.XSize / 2f;
            float zDiff = player.CollisionBox.ZSize / 2f;

            bp0.X = (int)(entityPosPos.X - xDiff);
            bp0.Z = (int)(entityPosPos.Z - zDiff);
            bp1.X = (int)(entityPosPos.X + xDiff);
            bp1.Z = (int)(entityPosPos.Z - zDiff);
            bp2.X = (int)(entityPosPos.X - xDiff);
            bp2.Z = (int)(entityPosPos.Z + zDiff);
            bp3.X = (int)(entityPosPos.X + xDiff);
            bp3.Z = (int)(entityPosPos.Z + zDiff);

            List<Block> blocks = [accessor.GetBlock(bp0)];
            if (bp0 != bp1) blocks.Add(accessor.GetBlock(bp1));
            if (bp0 != bp2) blocks.Add(accessor.GetBlock(bp2));
            if (bp0 != bp3) blocks.Add(accessor.GetBlock(bp3));

            if (blocks.Exists(block => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0))
            {
                return blockPos.Y + blocks
                    .Where(block => block?.CollisionBoxes != null && block.CollisionBoxes.Length > 0)
                    .Select(block => block.CollisionBoxes
                        .Select(box => box.MaxY)
                        .Max())
                    .Max();
            }

            heightDiff++;
        }

        return 0;
    }

    private static void ReloadBagInventory(BagInventory __instance, ref InventoryBase parentinv, ref ItemSlot[] bagSlots)
    {
        if (parentinv is not InventoryBasePlayer inventory) return;

        InvalidateWearableLightHsvCache(inventory.Owner);
        bagSlots = AppendGearInventorySlots(bagSlots, inventory.Owner);

        if (bagSlots.Length == 4)
        {
            bagSlots = Enumerable.Range(0, ArmorInventory._totalSlotsNumber)
                .Select(_ => new DummySlot() as ItemSlot)
                .Concat(bagSlots)
                .ToArray();
        }
    }

    private static ItemSlot[] AppendGearInventorySlots(ItemSlot[] backpackSlots, Entity owner)
    {
        IInventory? inventory = GetGearInventory(owner);

        if (inventory == null) return backpackSlots;
        if (backpackSlots.Any(slot => slot.Inventory == inventory)) return backpackSlots;

        ItemSlot[] gearSlots = inventory.ToArray();
        return gearSlots.Concat(backpackSlots).ToArray();
    }

    private static IInventory? GetGearInventory(Entity entity)
    {
        return (entity as EntityPlayer)?.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.characterInvClassName);
    }

    private static IInventory? GetBackpackInventory(EntityPlayer player)
    {
        return player.Player?.InventoryManager?.GetOwnInventory(GlobalConstants.backpackInvClassName);
    }

    private static bool BagInventory_SaveSlotIntoBag(BagInventory __instance, ItemSlotBagContent slot)
    {
        InvalidateWearableLightHsvCache((slot.Inventory as InventoryBasePlayer)?.Owner);
        ItemStack? backPackStack = __instance.BagSlots[slot.BagIndex]?.Itemstack;

        try
        {
            backPackStack?.Collectible.GetCollectibleInterface<IHeldBag>()?.Store(backPackStack, slot);
        }
        catch (Exception exception)
        {
            LoggerUtil.Error(_api, typeof(HarmonyPatches), $"Error in BagInventory_SaveSlotIntoBag:\n{exception}");
            return true;
        }

        return false;
    }

    private static void TryPatchStopRaiseShieldAnim(Harmony harmony, ICoreAPI api)
    {
        Type? type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemStopRaiseShieldAnim");
        MethodInfo? method = type?.GetMethod("OnGameTick", AccessTools.all, null, new[] { typeof(float) }, null);
        if (method == null)
        {
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(ModSystemStopRaiseShieldAnim_OnGameTick_Prefix))));
    }

    private static void TryUnpatchStopRaiseShieldAnim(Harmony harmony, string harmonyId)
    {
        Type? type = AccessTools.TypeByName("Vintagestory.GameContent.ModSystemStopRaiseShieldAnim");
        MethodInfo? method = type?.GetMethod("OnGameTick", AccessTools.all, null, new[] { typeof(float) }, null);
        if (method == null) return;
        harmony.Unpatch(method, HarmonyPatchType.Prefix, harmonyId);
    }

    private static bool ModSystemStopRaiseShieldAnim_OnGameTick_Prefix()
    {
        if (!ShieldAutoPatcher.IsCombatOverhaulEnabled(_api)) return true;
        if (_api is not Vintagestory.API.Client.ICoreClientAPI capi) return true;
        if (capi.World?.Player?.Entity is not EntityPlayer player) return true;

        // Do not keep the vanilla raiseshield animation alive for CO-patched shields.
        // Those are CombatOverhaul.Implementations.VanillaShield and should animate through
        // Combat Overhaul's BlockAnimation/ReadyAnimation path.
        if (!CollectibleClassifier.IsVanillaItemShield(player.LeftHandItemSlot?.Itemstack?.Item)) return true;

        CombatOverhaulSystem? coSystem = capi.ModLoader.GetModSystem<CombatOverhaulSystem>();
        if (coSystem?.ActionListener == null) return true;

        bool rmbDown =
            coSystem.ActionListener.IsActive(EnumEntityAction.RightMouseDown) ||
            coSystem.ActionListener.IsActive(EnumEntityAction.InWorldRightMouseDown);
        if (!rmbDown) return true;

        // Mirror vanilla shielding intent while RMB is held with an offhand shield.
        // This keeps vanilla shield raise animations alive even when CO drives blocking.
        player.Controls.RightMouseDown = true;
        player.ServerControls.RightMouseDown = true;
        player.Controls.CtrlKey = true;
        player.ServerControls.CtrlKey = true;
        player.StartAnimation("raiseshield-left");
        player.StartAnimation("raiseshield-left-fp");
        return false;
    }

    private static void TryPatchVanillaShieldIdle(Harmony harmony, ICoreAPI api)
    {
        MethodInfo? method = AccessTools.Method(
            typeof(ItemShield),
            nameof(ItemShield.OnHeldIdle),
            [typeof(ItemSlot), typeof(EntityAgent)]
        );

        if (method == null)
        {
            api.Logger.Warning("[OverhaullibLegacyCompat] Could not find vanilla ItemShield.OnHeldIdle. CO-patched shields may keep vanilla idle shield behavior.");
            return;
        }

        harmony.Patch(method, prefix: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(VanillaShield_OnHeldIdle_Prefix))));
    }

    private static void TryUnpatchVanillaShieldIdle(Harmony harmony, string harmonyId)
    {
        MethodInfo? method = AccessTools.Method(
            typeof(ItemShield),
            nameof(ItemShield.OnHeldIdle),
            [typeof(ItemSlot), typeof(EntityAgent)]
        );

        if (method != null)
        {
            harmony.Unpatch(method, HarmonyPatchType.Prefix, harmonyId);
        }
    }

    private static bool VanillaShield_OnHeldIdle_Prefix(ItemSlot slot)
    {
        return !ShieldAutoPatcher.IsCombatOverhaulEnabled(_api)
            || !CollectibleClassifier.HasMeleeWeaponActions(slot.Itemstack?.Collectible);
    }

    private static void TryPatchVanillaShieldTooltip(Harmony harmony, ICoreAPI api)
    {
        MethodInfo? method = AccessTools.Method(
            typeof(ItemShield),
            nameof(ItemShield.GetHeldItemInfo),
            [typeof(ItemSlot), typeof(System.Text.StringBuilder), typeof(IWorldAccessor), typeof(bool)]
        );

        if (method == null)
        {
            api.Logger.Warning("[OverhaullibLegacyCompat] Could not find vanilla ItemShield.GetHeldItemInfo. CO-patched shields may show vanilla shield tooltip stats.");
            return;
        }

        harmony.Patch(method, transpiler: new HarmonyMethod(AccessTools.Method(typeof(HarmonyPatches), nameof(VanillaShield_GetHeldItemInfo_Transpiler))));
    }

    private static void TryUnpatchVanillaShieldTooltip(Harmony harmony, string harmonyId)
    {
        MethodInfo? method = AccessTools.Method(
            typeof(ItemShield),
            nameof(ItemShield.GetHeldItemInfo),
            [typeof(ItemSlot), typeof(System.Text.StringBuilder), typeof(IWorldAccessor), typeof(bool)]
        );

        if (method != null)
        {
            harmony.Unpatch(method, HarmonyPatchType.Transpiler, harmonyId);
        }
    }

    private static IEnumerable<CodeInstruction> VanillaShield_GetHeldItemInfo_Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        MethodInfo? baseTooltip = AccessTools.Method(
            typeof(Item),
            nameof(Item.GetHeldItemInfo),
            [typeof(ItemSlot), typeof(System.Text.StringBuilder), typeof(IWorldAccessor), typeof(bool)]
        );
        MethodInfo shouldSkip = AccessTools.Method(typeof(HarmonyPatches), nameof(ShouldSkipVanillaShieldTooltip))
            ?? throw new MissingMethodException(nameof(ShouldSkipVanillaShieldTooltip));
        Label keepVanillaTooltip = generator.DefineLabel();
        bool injected = false;

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if (baseTooltip != null && !injected && instruction.Calls(baseTooltip))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Call, shouldSkip);
                yield return new CodeInstruction(OpCodes.Brfalse_S, keepVanillaTooltip);
                yield return new CodeInstruction(OpCodes.Ret);
                CodeInstruction keepVanillaInstruction = new(OpCodes.Nop);
                keepVanillaInstruction.labels.Add(keepVanillaTooltip);
                yield return keepVanillaInstruction;
                injected = true;
            }
        }
    }

    private static bool ShouldSkipVanillaShieldTooltip(ItemShield shield, ItemSlot slot)
    {
        return ShieldAutoPatcher.IsCombatOverhaulEnabled(_api)
            && CollectibleClassifier.HasMeleeWeaponActions(slot.Itemstack?.Collectible);
    }

    private static void LightHsv(EntityPlayer __instance, ref byte[] __result)
    {
        if (__instance?.Player == null) return;
        if (!__instance.Alive) return;
        if (__instance.Player.WorldData.CurrentGameMode == EnumGameMode.Spectator) return;

        try
        {
            __result = CopyLightHsvToOutputBuffer(__result);

            if (DebugWearableLights)
            {
                _api?.Logger.Notification("[OverhaullibLegacyCompat] LightHsv postfix called for " + __instance.Player.PlayerName);
            }

            byte[] wearableLightHsv = GetCachedWearableLightHsv(__instance);
            AddLight(ref __result, wearableLightHsv);
        }
        catch (Exception exception)
        {
            ReportWearableLightHsvException(exception);
        }
    }

    private static byte[] GetCachedWearableLightHsv(EntityPlayer player)
    {
        long nowMs = _api?.World?.ElapsedMilliseconds ?? Environment.TickCount64;
        long entityId = player.EntityId;

        if (_wearableLightHsvCache.TryGetValue(entityId, out WearableLightHsvCacheEntry? entry))
        {
            if (entry != null && nowMs < entry.ExpiresAtMs && entry.Hsv is { Length: >= 3 })
            {
                return entry.Hsv;
            }

            _wearableLightHsvCache.Remove(entityId);
        }

        byte[] hsv = [0, 0, 0];
        AddInventoryLights(player, GetGearInventory(player), ref hsv, "gear");
        AddBackpackLights(player, ref hsv);
        _wearableLightHsvCache[entityId] = new WearableLightHsvCacheEntry(hsv, nowMs + _wearableLightHsvCacheDurationMs);
        PruneExpiredWearableLightHsvCache(nowMs);
        return hsv;
    }

    // Invalidate only the affected owner's cached light instead of wiping every entity's entry.
    // A null owner (couldn't resolve the player) falls back to a full clear, which is correct but
    // rare. This both bounds growth for that owner and stops one player's bag edit from defeating
    // the 250 ms cache for every other player on the client.
    private static void InvalidateWearableLightHsvCache(Entity? owner)
    {
        if (owner == null)
        {
            _wearableLightHsvCache.Clear();
            return;
        }

        _wearableLightHsvCache.Remove(owner.EntityId);
    }

    // Entries for entities that stop being queried (despawned / out of view) never refresh and would
    // otherwise linger forever. Only runs once the cache grows past a threshold, so normal play pays
    // nothing; pathological accumulation is bounded.
    private static void PruneExpiredWearableLightHsvCache(long nowMs)
    {
        if (_wearableLightHsvCache.Count < _wearableLightHsvCachePruneThreshold) return;

        long staleBeforeMs = nowMs - _wearableLightHsvCacheDurationMs;
        _wearableLightHsvPruneScratch.Clear();
        foreach ((long id, WearableLightHsvCacheEntry cached) in _wearableLightHsvCache)
        {
            if (cached.ExpiresAtMs < staleBeforeMs)
            {
                _wearableLightHsvPruneScratch.Add(id);
            }
        }

        foreach (long id in _wearableLightHsvPruneScratch)
        {
            _wearableLightHsvCache.Remove(id);
        }
    }

    private static void AddBackpackLights(EntityPlayer player, ref byte[] result)
    {
        IInventory? backpackInventory = GetBackpackInventory(player);
        if (backpackInventory == null) return;

        int count = Math.Min(4, backpackInventory.Count);
        for (int index = 0; index < count; index++)
        {
            AddSlotLight(player, backpackInventory[index], ref result, "backpack");
        }

        AddBackpackStoredLightSlots(player, backpackInventory, ref result);
    }

    private static void AddBackpackStoredLightSlots(EntityPlayer player, IInventory backpackInventory, ref byte[] result)
    {
        foreach (ItemSlot slot in backpackInventory)
        {
            if (slot is not ItemSlotBagContentWithWildcardMatch bagSlot) continue;
            if (bagSlot.Config.StoredItemLightLevelOffset == 0) continue;

            AddSlotLight(player, bagSlot, ref result, "backpack-content");
        }
    }

    private static void AddInventoryLights(EntityPlayer player, IInventory? inventory, ref byte[] result, string source)
    {
        if (inventory == null) return;

        foreach (ItemSlot slot in inventory)
        {
            AddSlotLight(player, slot, ref result, source);
        }
    }

    private static void AddSlotLight(EntityPlayer player, ItemSlot? slot, ref byte[] result, string source)
    {
        if (slot?.Empty != false) return;
        if (slot.Itemstack?.Collectible == null) return;

        CollectibleObject collectible = slot.Itemstack.Collectible;

        IWearableLightSource? wearableLightSource = collectible.GetCollectibleInterface<IWearableLightSource>();
        if (wearableLightSource != null)
        {
            byte[]? hsv = null;
            try
            {
                hsv = wearableLightSource.GetLightHsv(player, slot);
            }
            catch (Exception exception)
            {
                ReportWearableLightHsvException(exception);
            }

            if (DebugWearableLights)
            {
                _api?.Logger.Notification(
                    "[OverhaullibLegacyCompat] " + source + " wearable light " + collectible.Code +
                    " -> " + FormatHsv(ApplySlotLightLevelOffset(slot, hsv))
                );
            }

            AddLight(ref result, ApplySlotLightLevelOffset(slot, hsv));
        }

        byte[]? normalLightHsv = collectible.LightHsv;
        if (normalLightHsv != null && normalLightHsv.Length >= 3 && normalLightHsv[2] > 0)
        {
            byte[] adjustedLightHsv = ApplySlotLightLevelOffset(slot, normalLightHsv);

            if (DebugWearableLights)
            {
                _api?.Logger.Notification(
                    "[OverhaullibLegacyCompat] " + source + " normal light " + collectible.Code +
                    " -> " + FormatHsv(adjustedLightHsv)
                );
            }

            AddLight(ref result, adjustedLightHsv);
        }
    }

    [ThreadStatic]
    private static byte[]? _lightHsvOutputBuffer;

    private static void AddLight(ref byte[] result, byte[]? hsv)
    {
        if (hsv == null || hsv.Length < 3 || hsv[2] <= 0)
        {
            return;
        }

        if (result == null || result.Length < 3)
        {
            result = new byte[] { 0, 0, 0 };
        }

        float totalBrightness = result[2] + hsv[2];
        if (totalBrightness <= 0)
        {
            return;
        }

        float brightnessFraction = hsv[2] / totalBrightness;

        byte oldHue = result[0];
        byte oldSat = result[1];
        byte oldVal = result[2];

        result[0] = (byte)(hsv[0] * brightnessFraction + oldHue * (1 - brightnessFraction));
        result[1] = (byte)(hsv[1] * brightnessFraction + oldSat * (1 - brightnessFraction));
        result[2] = Math.Max(hsv[2], oldVal);
    }

    private static byte[] CopyLightHsvToOutputBuffer(byte[]? hsv)
    {
        byte[] buffer = _lightHsvOutputBuffer ??= new byte[] { 0, 0, 0 };
        if (hsv == null || hsv.Length < 3)
        {
            buffer[0] = 0;
            buffer[1] = 0;
            buffer[2] = 0;
            return buffer;
        }

        buffer[0] = hsv[0];
        buffer[1] = hsv[1];
        buffer[2] = hsv[2];
        return buffer;
    }

    private static byte[] ApplySlotLightLevelOffset(ItemSlot slot, byte[]? hsv)
    {
        if (hsv == null || hsv.Length < 3)
        {
            return CopyLightHsvToOutputBuffer(null);
        }

        int offset = GetStoredItemLightLevelOffset(slot);
        if (offset == 0)
        {
            return hsv;
        }

        byte[] adjusted = CopyLightHsvToOutputBuffer(hsv);
        adjusted[2] = (byte)Math.Clamp(adjusted[2] + offset, 0, byte.MaxValue);
        return adjusted;
    }

    private static int GetStoredItemLightLevelOffset(ItemSlot slot)
    {
        return slot is ItemSlotBagContentWithWildcardMatch bagSlot
            ? bagSlot.Config.StoredItemLightLevelOffset
            : 0;
    }

    private sealed class WearableLightHsvCacheEntry(byte[] hsv, long expiresAtMs)
    {
        public byte[] Hsv { get; } = hsv;
        public long ExpiresAtMs { get; } = expiresAtMs;
    }

    private static string FormatHsv(byte[]? hsv)
    {
        if (hsv == null) return "null";
        if (hsv.Length < 3) return "invalid";
        return hsv[0] + "," + hsv[1] + "," + hsv[2];
    }

    private static void ReportWearableLightHsvException(Exception exception)
    {
        if (_reportedWearableLightHsvException) return;

        _reportedWearableLightHsvException = true;
        LoggerUtil.Error(_api, typeof(HarmonyPatches), $"Error while applying wearable light HSV. Wearable light contribution will be skipped for this render call:\n{exception}");
    }

    private static bool BehaviorHealingItem_OnHeldInteractStart(EntityAgent byEntity)
    {
        if (byEntity is not EntityPlayer player) return true;

        IHasMeleeWeaponActions? item = player.LeftHandItemSlot.Itemstack?.Collectible?.GetCollectibleInterface<IHasMeleeWeaponActions>();
        return !item?.CanBlock(player, false) ?? true;
    }
}
