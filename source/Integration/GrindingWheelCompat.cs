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
    private static ICoreAPI? _api;

    public static void SetApi(ICoreAPI api)
    {
        _api = api;
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
        if (damage <= 0 || weaponStack?.Collectible == null)
        {
            return damage;
        }

        if (weaponStack.Collectible.GetCollectibleBehavior<CollectibleBehaviorBuffable>(true) == null)
        {
            return damage;
        }

        bool isCriticalHit = false;
        return weaponStack.Collectible.GetDamageToEntity(damage, target, weaponStack, ref isCriticalHit);
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
