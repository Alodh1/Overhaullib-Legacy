using CombatOverhaul.Utils;
using HarmonyLib;
using Vintagestory.API.Common;

namespace CombatOverhaul.Integration;

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnCreatedByCrafting))]
internal static class SplitMaterialWeaponCraftingPatch
{
    private static void Postfix(ItemSlot outputSlot)
    {
        if (outputSlot.Empty || outputSlot.Itemstack == null)
        {
            return;
        }

        bool changed = SplitMaterialWeaponUtil.Normalize(outputSlot.Itemstack);
        SplitMaterialWeaponUtil.NormalizeDurabilityIfNeeded(outputSlot.Itemstack);

        if (changed)
        {
            outputSlot.MarkDirty();
        }
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMaxDurability))]
internal static class SplitMaterialWeaponDurabilityPatch
{
    private static void Postfix(ItemStack itemstack, ref int __result)
    {
        if (SplitMaterialWeaponUtil.TryGetBodyMaxDurability(itemstack, __result, out int maxDurability))
        {
            __result = maxDurability;
        }
    }
}
