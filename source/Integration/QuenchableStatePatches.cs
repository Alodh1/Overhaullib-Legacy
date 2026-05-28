using CombatOverhaul.Utils;
using HarmonyLib;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), "applyQuenchedStats")]
internal static class QuenchableApplyQuenchedStatsPatch
{
    private static bool Prefix(CollectibleBehaviorQuenchable __instance, IWorldAccessor world, ItemStack itemstack)
    {
        if (!QuenchableStateUtil.IsArmorOrArmorComponent(itemstack) || !QuenchableStateUtil.IsFerrous(itemstack))
        {
            return true;
        }

        bool clayCovered = itemstack.Attributes.GetBool("clayCovered", false);

        // Non-clay quench: one-time only for armor flat reduction.
        if (!clayCovered)
        {
            if (QuenchableStateUtil.HasDirectArmorQuench(itemstack))
            {
                // Already consumed the direct quench path for this piece.
                return false;
            }

            QuenchableStateUtil.ApplyDirectArmorQuench(itemstack);
            SetArmorShatterChance(__instance, world, itemstack);
            return false;
        }

        // Clay quench: repeatable, supports tempering with the same curve as vanilla:
        // tempering reduces the next shatter chance, but reduces non-durability quench power.
        QuenchableStateUtil.ApplyClayArmorQuench(itemstack);
        SetArmorShatterChance(__instance, world, itemstack);
        return false;
    }

    private static void Postfix(ItemStack itemstack)
    {
        QuenchableStateUtil.NormalizeGenericBuffs(itemstack);
    }

    internal static void SetArmorShatterChance(CollectibleBehaviorQuenchable behavior, IWorldAccessor world, ItemStack itemstack)
    {
        int quenchIteration = itemstack.Attributes.GetInt("quenchIteration", 0);
        int temperIteration = itemstack.Attributes.GetInt("temperIteration", 0);

        float baseChance = 0.05f + (quenchIteration * behavior.BreakChancePerQuench);
        float temperedChance = baseChance * MathF.Pow(QuenchableStatUtil.TemperShatterChanceMultiplier, Math.Max(0, temperIteration));
        behavior.SetShatterChance(world, itemstack, Math.Max(0f, temperedChance));
    }
}

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), "applyTemperedStats")]
internal static class QuenchableApplyTemperedStatsPatch
{
    private static bool Prefix(CollectibleBehaviorQuenchable __instance, ItemStack itemstack, object[] __args)
    {
        if (!QuenchableStateUtil.HasAnyQuenchState(itemstack))
        {
            // Do not allow tempering before the piece has actually been quenched.
            return false;
        }

        if (!QuenchableStateUtil.IsArmorOrArmorComponent(itemstack) || !QuenchableStateUtil.IsFerrous(itemstack))
        {
            // Vanilla handles weapons/tools. This prefix only blocks unquenched items above.
            return true;
        }

        int clayQuenchIteration = Math.Max(0, itemstack.Attributes.GetInt("quenchIteration", 0));
        int temperIteration = Math.Max(0, itemstack.Attributes.GetInt("temperIteration", 0));
        if (clayQuenchIteration <= 0 || temperIteration >= clayQuenchIteration)
        {
            // Armor flat/direct quench is a one-time permanent +0.2 flat reduction
            // and does not grant an extra temper.  Each clay quench grants one
            // optional temper, so clay quench/temper can be repeated indefinitely.
            return false;
        }

        itemstack.Attributes.SetInt("temperIteration", temperIteration + 1);

        IWorldAccessor? world = TryGetWorld(__args);
        if (world != null)
        {
            QuenchableApplyQuenchedStatsPatch.SetArmorShatterChance(__instance, world, itemstack);
        }

        return false;
    }

    private static void Postfix(ItemStack itemstack)
    {
        QuenchableStateUtil.NormalizeGenericBuffs(itemstack);
    }

    private static IWorldAccessor? TryGetWorld(object[]? args)
    {
        if (args == null)
        {
            return null;
        }

        foreach (object arg in args)
        {
            if (arg is IWorldAccessor world)
            {
                return world;
            }
        }

        return null;
    }
}

[HarmonyPatch(typeof(GridRecipe), nameof(GridRecipe.Matches))]
internal static class GridRecipeQuenchStateMatchPatch
{
    private static void Postfix(ItemSlot[] ingredients, ref bool __result)
    {
        if (!__result)
        {
            return;
        }

        int? expectedState = null;
        bool? expectedDirect = null;
        int? expectedClayQuenchIteration = null;
        int? expectedClayTemperIteration = null;
        foreach (ItemSlot slot in ingredients)
        {
            if (slot.Empty || !QuenchableStateUtil.IsArmorOrArmorComponent(slot.Itemstack) || !QuenchableStateUtil.IsFerrous(slot.Itemstack))
            {
                continue;
            }

            int state = QuenchableStateUtil.GetArmorQuenchState(slot.Itemstack);
            expectedState ??= state;
            if (expectedState.Value != state)
            {
                __result = false;
                return;
            }

            bool direct = QuenchableStateUtil.HasDirectArmorQuench(slot.Itemstack);
            int clayQuenchIteration = QuenchableStateUtil.GetArmorQuenchMode(slot.Itemstack) == QuenchableStateUtil.ArmorQuenchModeClay
                ? slot.Itemstack.Attributes.GetInt("quenchIteration", 0)
                : 0;
            int clayTemperIteration = QuenchableStateUtil.GetArmorQuenchMode(slot.Itemstack) == QuenchableStateUtil.ArmorQuenchModeClay
                ? slot.Itemstack.Attributes.GetInt("temperIteration", 0)
                : 0;

            expectedDirect ??= direct;
            expectedClayQuenchIteration ??= clayQuenchIteration;
            expectedClayTemperIteration ??= clayTemperIteration;

            if (expectedDirect.Value != direct
                || expectedClayQuenchIteration.Value != clayQuenchIteration
                || expectedClayTemperIteration.Value != clayTemperIteration)
            {
                __result = false;
                return;
            }
        }
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnCreatedByCrafting))]
internal static class CraftedArmorQuenchStatePatch
{
    private static void Postfix(ItemSlot[] allInputSlots, ItemSlot outputSlot)
    {
        if (outputSlot.Empty || outputSlot.Itemstack == null)
        {
            return;
        }

        bool outputWasClayCovered = outputSlot.Itemstack.Attributes.GetBool("clayCovered", false);

        int? inheritedState = null;
        bool? inheritedDirect = null;
        int? inheritedClayQuenchIteration = null;
        int? inheritedClayTemperIteration = null;
        foreach (ItemSlot slot in allInputSlots)
        {
            if (slot.Empty || !QuenchableStateUtil.IsArmorOrArmorComponent(slot.Itemstack) || !QuenchableStateUtil.IsFerrous(slot.Itemstack))
            {
                continue;
            }

            int state = QuenchableStateUtil.GetArmorQuenchState(slot.Itemstack);
            inheritedState ??= state;
            if (inheritedState.Value != state)
            {
                return;
            }

            bool direct = QuenchableStateUtil.HasDirectArmorQuench(slot.Itemstack);
            int clayQuenchIteration = QuenchableStateUtil.GetArmorQuenchMode(slot.Itemstack) == QuenchableStateUtil.ArmorQuenchModeClay
                ? slot.Itemstack.Attributes.GetInt("quenchIteration", 0)
                : 0;
            int clayTemperIteration = QuenchableStateUtil.GetArmorQuenchMode(slot.Itemstack) == QuenchableStateUtil.ArmorQuenchModeClay
                ? slot.Itemstack.Attributes.GetInt("temperIteration", 0)
                : 0;

            inheritedDirect ??= direct;
            inheritedClayQuenchIteration ??= clayQuenchIteration;
            inheritedClayTemperIteration ??= clayTemperIteration;

            if (inheritedDirect.Value != direct
                || inheritedClayQuenchIteration.Value != clayQuenchIteration
                || inheritedClayTemperIteration.Value != clayTemperIteration)
            {
                return;
            }
        }

        if (inheritedState.HasValue && QuenchableStateUtil.IsArmorOrArmorComponent(outputSlot.Itemstack))
        {
            bool hasInheritedQuenchState = inheritedState.Value > 0
                || (inheritedDirect ?? false)
                || (inheritedClayQuenchIteration ?? 0) > 0;

            if (hasInheritedQuenchState)
            {
                QuenchableStateUtil.ApplyInheritedArmorQuenchState(
                    outputSlot.Itemstack,
                    inheritedDirect ?? false,
                    inheritedClayQuenchIteration ?? 0,
                    inheritedClayTemperIteration ?? 0);
            }
        }

        // Preserve explicit clay-covering recipe output state (e.g., direct-quenched armor part + fire clay).
        if (outputWasClayCovered)
        {
            outputSlot.Itemstack.Attributes.SetBool("clayCovered", true);
        }

        NormalizeCraftedWeaponQuenchState(allInputSlots, outputSlot.Itemstack);
        QuenchableStateUtil.NormalizeGenericBuffs(outputSlot.Itemstack);
    }

    private static void NormalizeCraftedWeaponQuenchState(ItemSlot[] allInputSlots, ItemStack output)
    {
        if (output.Collectible == null
            || QuenchableStateUtil.IsArmorOrArmorComponent(output)
            || !QuenchableStateUtil.IsFerrous(output)
            || output.Collectible.GetCollectibleBehavior<CollectibleBehaviorQuenchable>(true) == null
            || QuenchableStateUtil.GetKind(output) != QuenchableStateUtil.WeaponKind)
        {
            return;
        }

        int bestQuenchIteration = 0;
        int bestTemperIteration = 0;
        float bestPowerValue = 0f;

        foreach (ItemSlot input in allInputSlots)
        {
            if (input.Empty || input.Itemstack?.Collectible == null || !QuenchableStateUtil.IsFerrous(input.Itemstack))
            {
                continue;
            }

            string kind = QuenchableStateUtil.GetKind(input.Itemstack);
            if (kind != QuenchableStateUtil.WeaponKind)
            {
                continue;
            }

            int quenchIteration = Math.Max(0, input.Itemstack.Attributes.GetInt("quenchIteration", 0));
            int temperIteration = Math.Clamp(input.Itemstack.Attributes.GetInt("temperIteration", 0), 0, quenchIteration);
            float powerValue = Math.Max(0f, input.Itemstack.Attributes.GetFloat("powervalue", 0f));

            if (quenchIteration > bestQuenchIteration
                || (quenchIteration == bestQuenchIteration && temperIteration > bestTemperIteration)
                || (quenchIteration == bestQuenchIteration && temperIteration == bestTemperIteration && powerValue > bestPowerValue))
            {
                bestQuenchIteration = quenchIteration;
                bestTemperIteration = temperIteration;
                bestPowerValue = powerValue;
            }
        }

        if (bestQuenchIteration <= 0 && bestPowerValue <= 0f)
        {
            return;
        }

        int finalQuenchIteration = bestQuenchIteration;
        int finalTemperIteration = Math.Clamp(bestTemperIteration, 0, bestQuenchIteration);
        float powerPerQuench = GetPowerPerQuench(output);
        float normalizedPowerValue = finalQuenchIteration > 0
            ? finalQuenchIteration * MathF.Pow(QuenchableStatUtil.TemperPowerMultiplier, finalTemperIteration) * powerPerQuench
            : bestPowerValue;

        output.Attributes.SetInt("quenchIteration", finalQuenchIteration);
        output.Attributes.SetInt("temperIteration", finalTemperIteration);
        output.Attributes.SetFloat("powervalue", Math.Max(0f, normalizedPowerValue));

        // Prevent stacked/duplicated quench buffs after crafting.
        QuenchableStateUtil.RemoveBuffs(output, "attackpower", "miningspeed");
    }

    private static float GetPowerPerQuench(ItemStack stack)
    {
        const float fallback = 0.1f;
        CollectibleBehaviorQuenchable? behavior = stack.Collectible?.GetCollectibleBehavior<CollectibleBehaviorQuenchable>(true);
        if (behavior == null)
        {
            return fallback;
        }

        try
        {
            object? value = behavior.GetType().GetProperty("PowerPerQuench")?.GetValue(behavior);
            if (value is float f && f > 0f) return f;
            if (value is double d && d > 0d) return (float)d;
            if (value is int i && i > 0) return i;
        }
        catch
        {
        }

        return fallback;
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMaxDurability))]
internal static class ArmorQuenchDurabilityPatch
{
    private static void Postfix(ItemStack itemstack, ref int __result)
    {
        if (__result <= 0 || !QuenchableStateUtil.IsArmorOrArmorComponent(itemstack))
        {
            return;
        }

        int state = QuenchableStateUtil.GetArmorQuenchState(itemstack);
        if (state <= 0)
        {
            return;
        }

        __result = Math.Max(1, (int)MathF.Round(__result * 1.10f));
    }
}

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), nameof(CollectibleBehaviorQuenchable.GetHeldItemInfo))]
internal static class ArmorQuenchTooltipPatch
{
    private static void Postfix(ItemSlot inSlot, StringBuilder dsc)
    {
        AppendTooltip(inSlot, dsc);
    }

    internal static void AppendTooltip(ItemSlot inSlot, StringBuilder dsc)
    {
        if (inSlot.Empty || !QuenchableStateUtil.IsArmorOrArmorComponent(inSlot.Itemstack) || !QuenchableStateUtil.IsFerrous(inSlot.Itemstack))
        {
            return;
        }

        if (QuenchableStateUtil.GetArmorQuenchState(inSlot.Itemstack) > 0)
        {
            AppendLineOnce(dsc, Lang.Get("combatoverhaul:quenchable-armor-durability-gain"));
        }

        if (QuenchableStateUtil.HasDirectArmorQuench(inSlot.Itemstack))
        {
            AppendLineOnce(dsc, Lang.Get("combatoverhaul:quenchable-armor-flat-reduction-gain"));
        }

        float penaltyMultiplier = QuenchableStatUtil.GetArmorPenaltyMultiplier(inSlot.Itemstack);
        float penaltyReduction = (1f - penaltyMultiplier) * 100f;
        if (penaltyReduction > 0f)
        {
            AppendLineOnce(dsc, Lang.Get("combatoverhaul:quenchable-armor-penalty-reduction", MathF.Round(penaltyReduction)));
        }
    }

    private static void AppendLineOnce(StringBuilder dsc, string line)
    {
        if (string.IsNullOrWhiteSpace(line) || dsc.ToString().Contains(line, StringComparison.Ordinal))
        {
            return;
        }

        dsc.AppendLine(line);
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
internal static class ArmorQuenchCollectibleTooltipPatch
{
    private static void Postfix(ItemSlot inSlot, StringBuilder dsc)
    {
        ArmorQuenchTooltipPatch.AppendTooltip(inSlot, dsc);
    }
}
