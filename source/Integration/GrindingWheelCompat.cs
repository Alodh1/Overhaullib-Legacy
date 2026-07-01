using CombatOverhaul.Implementations;
using CombatOverhaul.Inputs;
using HarmonyLib;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

public static class GrindingWheelCompat
{
    public delegate float ExtraWeaponCriticalHitChanceDelegate(ItemStack weaponStack, Entity target, float damage);

    private const string GrindingWheelCritStatCode = "critchance";
    private const string GrindingWheelSharpenedBuffCode = "sharpened";
    private const float CriticalHitDamageMultiplier = 2f;
    private static ICoreAPI? _api;

    public static event ExtraWeaponCriticalHitChanceDelegate? ExtraWeaponCriticalHitChance;

    public static void SetApi(ICoreAPI api)
    {
        _api = api;
        CriticalHitFeedback.Register(api);
    }

    public static void Dispose()
    {
        CriticalHitFeedback.Dispose();
        _api = null;
    }

    public static void EnsureWeaponBuffableBehavior(ICoreAPI api)
    {
        SetApi(api);

        int eligible = 0;
        int patched = 0;

        foreach (Item item in api.World.Items)
        {
            if (item?.Code == null || !IsSharpenableWeapon(api, item))
            {
                continue;
            }

            eligible++;

            if (item.GetCollectibleBehavior<CollectibleBehaviorBuffable>(true) != null)
            {
                continue;
            }

            AddBuffableBehavior(item, api);
            patched++;
        }

        if (patched > 0)
        {
            api.Logger.Notification($"[OverhaullibLegacyCompat] Enabled vanilla grinding wheel sharpening for {eligible} weapon items; added Buffable to {patched} missing items.");
        }
    }

    public static float ApplyBuffableDamage(ItemStack? weaponStack, Entity target, float damage)
    {
        return ApplyBuffableDamage(weaponStack, target, damage, null, allowCriticalHit: true);
    }

    public static float ApplyBuffableDamage(ItemStack? weaponStack, Entity target, float damage, DamageSource? damageSource)
    {
        return ApplyBuffableDamage(weaponStack, target, damage, damageSource, allowCriticalHit: true);
    }

    public static float ApplyBuffableDamage(ItemStack? weaponStack, Entity target, float damage, DamageSource? damageSource, bool allowCriticalHit)
    {
        if (damage <= 0 || weaponStack?.Collectible == null)
        {
            return damage;
        }

        CollectibleBehaviorBuffable? behavior = weaponStack.Collectible.GetCollectibleBehavior<CollectibleBehaviorBuffable>(true);

        float criticalHitChance = allowCriticalHit ? GetCombinedCriticalHitChance(weaponStack, target, damage, behavior) : 0f;
        if (criticalHitChance > 0 && target.World.Rand.NextDouble() < criticalHitChance)
        {
            damage *= CriticalHitDamageMultiplier;
            CriticalHitFeedback.Trigger(damageSource);
        }

        if (behavior == null) return damage;

        return ApplyBuffableDamageWithoutGrindingWheelCriticalRoll(weaponStack, target, damage, behavior);
    }

    private static float ApplyBuffableDamageWithoutGrindingWheelCriticalRoll(ItemStack weaponStack, Entity target, float damage, CollectibleBehaviorBuffable behavior)
    {
        List<AppliedCollectibleBuff> originalBuffs;
        try
        {
            originalBuffs = CloneBuffs(behavior.GetItemBuffs(weaponStack));
        }
        catch
        {
            bool fallbackCriticalHit = false;
            return weaponStack.Collectible.GetDamageToEntity(damage, target, weaponStack, ref fallbackCriticalHit);
        }

        if (!originalBuffs.Any(IsGrindingWheelCriticalHitBuff))
        {
            bool nonCriticalHit = false;
            return weaponStack.Collectible.GetDamageToEntity(damage, target, weaponStack, ref nonCriticalHit);
        }

        List<AppliedCollectibleBuff> nonRollingBuffs = CloneBuffs(originalBuffs);
        foreach (AppliedCollectibleBuff buff in nonRollingBuffs)
        {
            if (IsGrindingWheelCriticalHitBuff(buff))
            {
                buff.Multiplier = buff.Multiplier > 1f ? 1f : 0f;
            }
        }

        try
        {
            behavior.StoreItemBuffs(weaponStack, nonRollingBuffs);
            bool nonCriticalHit = false;
            return weaponStack.Collectible.GetDamageToEntity(damage, target, weaponStack, ref nonCriticalHit);
        }
        finally
        {
            behavior.StoreItemBuffs(weaponStack, originalBuffs);
        }
    }

    private static float GetCombinedCriticalHitChance(ItemStack weaponStack, Entity target, float damage, CollectibleBehaviorBuffable? behavior)
    {
        float chance = GetGrindingWheelCriticalHitChance(weaponStack, behavior);

        ExtraWeaponCriticalHitChanceDelegate? handlers = ExtraWeaponCriticalHitChance;
        if (handlers != null)
        {
            foreach (ExtraWeaponCriticalHitChanceDelegate handler in handlers.GetInvocationList().Cast<ExtraWeaponCriticalHitChanceDelegate>())
            {
                try
                {
                    chance += Math.Max(0f, handler(weaponStack, target, damage));
                }
                catch (Exception exception)
                {
                    _api?.Logger.Warning($"[OverhaullibLegacyCompat] Weapon critical hit chance provider failed: {exception}");
                }
            }
        }

        return Math.Clamp(chance, 0f, 1f);
    }

    private static float GetGrindingWheelCriticalHitChance(ItemStack weaponStack, CollectibleBehaviorBuffable? behavior)
    {
        if (behavior == null) return 0f;

        try
        {
            float chance = 0f;
            foreach (AppliedCollectibleBuff buff in behavior.GetItemBuffs(weaponStack))
            {
                if (IsGrindingWheelCriticalHitBuff(buff))
                {
                    chance += NormalizeCriticalHitChance(buff.Multiplier);
                }
            }

            return Math.Clamp(chance, 0f, 1f);
        }
        catch
        {
            return 0f;
        }
    }

    private static bool IsGrindingWheelCriticalHitBuff(AppliedCollectibleBuff buff)
    {
        return buff.Code.Equals(GrindingWheelSharpenedBuffCode, StringComparison.OrdinalIgnoreCase)
            && buff.StatCode.Equals(GrindingWheelCritStatCode, StringComparison.OrdinalIgnoreCase)
            && NormalizeCriticalHitChance(buff.Multiplier) > 0f;
    }

    private static float NormalizeCriticalHitChance(float multiplier)
    {
        if (multiplier <= 0f) return 0f;
        return multiplier > 1f ? multiplier - 1f : multiplier;
    }

    private static List<AppliedCollectibleBuff> CloneBuffs(IEnumerable<AppliedCollectibleBuff> buffs)
    {
        return buffs.Select(buff => new AppliedCollectibleBuff
        {
            Code = buff.Code,
            StatCode = buff.StatCode,
            Multiplier = buff.Multiplier,
            FlatChange = buff.FlatChange,
            RemainingDurability = buff.RemainingDurability
        }).ToList();
    }

    public static bool TryEnableGrindingWheelBuff(ItemSlot? slot)
    {
        if (slot?.Itemstack?.Collectible is not Item item || !IsSharpenableWeapon(_api, item))
        {
            return false;
        }

        CollectibleBehaviorBuffable? behavior = item.GetBehavior<CollectibleBehaviorBuffable>();
        if (behavior == null)
        {
            behavior = AddBuffableBehavior(item, _api);
        }

        AppliedCollectibleBuff? sharpened = behavior.GetItemBuffs(slot.Itemstack).FirstOrDefault(buff => buff.Code == "sharpened");
        return sharpened == null || sharpened.Multiplier < 1.099f;
    }

    public static bool PlayerTriesToUseGrindingWheel(EntityPlayer player, ItemSlot? slot, ActionEventData eventData, bool mainHand)
    {
        if (!mainHand || eventData.Action.Action != EnumEntityAction.RightMouseDown)
        {
            return false;
        }

        BlockSelection? blockSelection = player.BlockSelection;
        if (!IsGrindingWheel(blockSelection?.Block))
        {
            return false;
        }

        return TryEnableGrindingWheelBuff(slot);
    }

    public static bool IsGrindingWheel(Block? block)
    {
        if (block == null)
        {
            return false;
        }

        if (block is BlockGrindingWheel)
        {
            return true;
        }

        string path = block.Code?.Path ?? "";
        return path.StartsWith("grindingwheel-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryStartGrindingWeapon(IWorldAccessor? world, IPlayer? player, BlockSelection? blockSelection)
    {
        if (world == null || player == null || blockSelection == null)
        {
            return false;
        }

        ItemSlot? slot = player.InventoryManager?.ActiveHotbarSlot;
        if (!TryEnableGrindingWheelBuff(slot))
        {
            return false;
        }

        BlockEntityGrindingWheel? wheel = world.BlockAccessor.GetBlockEntity(blockSelection.Position) as BlockEntityGrindingWheel;
        return wheel?.OnInteractStart(player, blockSelection) == true;
    }

    private static CollectibleBehaviorBuffable AddBuffableBehavior(Item item, ICoreAPI? api)
    {
        CollectibleBehaviorBuffable behavior = new(item);
        behavior.Initialize(new JsonObject(new JObject()));
        if (api != null)
        {
            behavior.OnLoaded(api);
        }

        item.CollectibleBehaviors = (item.CollectibleBehaviors ?? []).Append(behavior).ToArray();
        return behavior;
    }

    private static bool IsSharpenableWeapon(ICoreAPI? api, Item item)
    {
        if (api != null && HasTag(api, item, "weapon-melee"))
        {
            return true;
        }

        if (item is MeleeWeapon or StanceBasedMeleeWeapon)
        {
            return true;
        }

        if (item.GetCollectibleBehavior<MeleeWeaponBehavior>(true) != null)
        {
            return true;
        }

        if (HasCombatOverhaulMeleeStats(item))
        {
            return true;
        }

        return LooksLikeMeleeWeaponCode(item.Code.Path);
    }

    private static bool HasCombatOverhaulMeleeStats(Item item)
    {
        JsonObject? attributes = item.Attributes;
        if (attributes == null)
        {
            return false;
        }

        return attributes["OneHandedStance"].Exists
            || attributes["TwoHandedStance"].Exists
            || attributes["OffHandStance"].Exists
            || attributes["MainHandStance"].Exists
            || attributes["ThrowAttack"].Exists;
    }

    private static bool HasTag(ICoreAPI api, Item item, string tag)
    {
        if (item.Tags.IsEmpty)
        {
            return false;
        }

        try
        {
            api.CollectibleTagRegistry.TryRegisterAndCreateTagSet(out TagSet tagSet, [tag]);
            return !tagSet.IsEmpty && tagSet.IsFullyContainedIn(item.Tags);
        }
        catch
        {
            return false;
        }
    }

    private static bool LooksLikeMeleeWeaponCode(string path)
    {
        if (path.Contains("firearm", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bullet", StringComparison.OrdinalIgnoreCase)
            || path.Contains("cartridge", StringComparison.OrdinalIgnoreCase)
            || path.Contains("arrow", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bolt", StringComparison.OrdinalIgnoreCase)
            || path.Contains("bow", StringComparison.OrdinalIgnoreCase)
            || path.Contains("sling", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return path.Contains("blade", StringComparison.OrdinalIgnoreCase)
            || path.Contains("sword", StringComparison.OrdinalIgnoreCase)
            || path.Contains("sabre", StringComparison.OrdinalIgnoreCase)
            || path.Contains("dagger", StringComparison.OrdinalIgnoreCase)
            || path.Contains("spear", StringComparison.OrdinalIgnoreCase)
            || path.Contains("javelin", StringComparison.OrdinalIgnoreCase)
            || path.Contains("halberd", StringComparison.OrdinalIgnoreCase)
            || path.Contains("poleaxe", StringComparison.OrdinalIgnoreCase)
            || path.Contains("pike", StringComparison.OrdinalIgnoreCase)
            || path.Contains("mace", StringComparison.OrdinalIgnoreCase)
            || path.Contains("club", StringComparison.OrdinalIgnoreCase)
            || path.Contains("warhammer", StringComparison.OrdinalIgnoreCase)
            || path.Contains("battleaxe", StringComparison.OrdinalIgnoreCase)
            || path.Contains("longaxe", StringComparison.OrdinalIgnoreCase);
    }
}

[HarmonyPatch(typeof(BlockGrindingWheel), nameof(BlockGrindingWheel.OnBlockInteractStart))]
internal static class GrindingWheelStartPatch
{
    private static void Postfix(IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
    {
        if (!GrindingWheelCompat.TryEnableGrindingWheelBuff(byPlayer.InventoryManager?.ActiveHotbarSlot))
        {
            return;
        }

        __result = GrindingWheelCompat.TryStartGrindingWeapon(world, byPlayer, blockSel);
    }
}

[HarmonyPatch(typeof(BlockEntityGrindingWheel), "canBuff")]
internal static class GrindingWheelCanBuffPatch
{
    private static void Postfix(ItemSlot slot, ref bool __result)
    {
        if (__result)
        {
            return;
        }

        __result = GrindingWheelCompat.TryEnableGrindingWheelBuff(slot);
    }
}
