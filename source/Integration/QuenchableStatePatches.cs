using CombatOverhaul.Utils;
using HarmonyLib;
using System.Globalization;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

internal static class QuenchablePatchGate
{
    private static readonly HashSet<string> CustomQuenchRecipeNames =
    [
        "claycovering-armoryparts",
        "claycovering-coblades",
        "claycovering-cospears",
        "claycovering-armorcomponents",
        "claycovering-armorcomponents-meteoricsteel"
    ];

    internal static bool Enabled { get; set; }

    internal static bool IsCombatOverhaulEnabled(ICoreAPI api)
    {
        return api.ModLoader.IsModEnabled("combatoverhaul") || api.ModLoader.IsModEnabled("combatoverhaulfork");
    }

    internal static void DisableCustomQuenchRecipeAssetsIfDisabled(ICoreAPI api)
    {
        if (Enabled)
        {
            return;
        }

        byte[] emptyRecipeArray = Encoding.UTF8.GetBytes("[]");
        foreach (string recipeName in CustomQuenchRecipeNames)
        {
            IAsset? asset = api.Assets.TryGet(new AssetLocation("game", $"recipes/grid/{recipeName}.json"), true);
            if (asset != null)
            {
                asset.Data = emptyRecipeArray;
            }
        }
    }

    internal static void RemoveCustomQuenchRecipesIfDisabled(ICoreAPI api)
    {
        if (Enabled)
        {
            return;
        }

        List<GridRecipe>? recipes = api.World?.GridRecipes;
        if (recipes == null)
        {
            return;
        }

        int removed = recipes.RemoveAll(IsCustomQuenchRecipe);
        if (removed > 0)
        {
            LoggerUtil.Verbose(api, typeof(QuenchablePatchGate), $"Removed {removed} custom quench recipes because Combat Overhaul is not loaded.");
        }
    }

    internal static bool Prepare() => Enabled;

    private static bool IsCustomQuenchRecipe(GridRecipe recipe)
    {
        string name = recipe?.Name?.Path ?? recipe?.Name?.ToString() ?? "";
        return CustomQuenchRecipeNames.Contains(name);
    }
}

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), nameof(CollectibleBehaviorQuenchable.GetShatterChance))]
internal static class ArmorQuenchShatterChancePatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

    private static void Postfix(ItemStack itemstack, ref float __result)
    {
        if (!QuenchableStateUtil.IsArmorOrArmorComponent(itemstack)) return;
        __result = QuenchableStateUtil.IsArmorQuenchingEnabled(itemstack)
            ? Math.Clamp((QuenchableStatUtil.ArmorQuenchBaseShatterChance
                + Math.Max(0, itemstack.Attributes.GetInt("quenchIteration")) * QuenchableStatUtil.ArmorQuenchShatterChancePerQuench)
                * MathF.Pow(QuenchableStatUtil.ArmorQuenchTemperShatterMultiplier, Math.Max(0, itemstack.Attributes.GetInt("temperIteration"))), 0f, 1f)
            : 0f;
    }
}

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), "applyQuenchedStats")]
internal static class QuenchableApplyQuenchedStatsPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

    private static bool Prefix(CollectibleBehaviorQuenchable __instance, IWorldAccessor world, ItemStack itemstack)
    {
        if (!QuenchableStateUtil.IsArmorOrArmorComponent(itemstack) || !QuenchableStateUtil.IsFerrous(itemstack))
        {
            return true;
        }

        if (!QuenchableStateUtil.IsArmorQuenchingEnabled(itemstack)) return false;

        bool clayCovered = itemstack.Attributes.GetBool("clayCovered", false);

        // Non-clay quench: one-time only for armor flat reduction.
        if (!clayCovered)
        {
            if (QuenchableStatUtil.GetArmorFlatReductionBonus(itemstack) >= QuenchableStatUtil.ArmorQuenchFlatReduction && QuenchableStateUtil.HasDirectArmorQuench(itemstack))
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

        float baseChance = QuenchableStatUtil.ArmorQuenchBaseShatterChance + (quenchIteration * QuenchableStatUtil.ArmorQuenchShatterChancePerQuench);
        float temperedChance = baseChance * MathF.Pow(QuenchableStatUtil.ArmorQuenchTemperShatterMultiplier, Math.Max(0, temperIteration));
        behavior.SetShatterChance(world, itemstack, Math.Max(0f, temperedChance));
    }
}

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), "applyTemperedStats")]
internal static class QuenchableApplyTemperedStatsPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

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

        if (!QuenchableStateUtil.IsArmorQuenchingEnabled(itemstack)) return false;

        int clayQuenchIteration = Math.Max(0, itemstack.Attributes.GetInt("quenchIteration", 0));
        int temperIteration = Math.Max(0, itemstack.Attributes.GetInt("temperIteration", 0));
        if (clayQuenchIteration <= 0 || temperIteration >= clayQuenchIteration)
        {
            // Armor flat/direct quench is a one-time permanent +0.2 flat reduction
            // and does not grant an extra temper.  Each clay quench grants one
            // optional temper, so clay quench/temper can be repeated indefinitely.
            return false;
        }

        if (itemstack.Attributes.HasAttribute("armorQuenchPenaltyBonus"))
            itemstack.Attributes.SetFloat("armorQuenchPenaltyBonus", (1f - QuenchableStatUtil.GetArmorPenaltyMultiplier(itemstack)) * QuenchableStatUtil.ArmorQuenchTemperPowerMultiplier);
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

// Settled chains no longer need their heat-treatment timestamps for simulation.
[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.Equals),
    new[] { typeof(ItemStack), typeof(ItemStack), typeof(string[]) })]
internal static class ChainQuenchStackMatchPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

    private static void Prefix(ItemStack thisStack, ItemStack otherStack, ref string[] ignoreAttributeSubTrees)
    {
        if (!(thisStack?.Collectible?.Code?.Path.StartsWith("metalchain-", StringComparison.Ordinal) == true
                || thisStack?.Collectible?.Code?.Path.StartsWith("metalscale-", StringComparison.Ordinal) == true)
            || otherStack?.Collectible != thisStack.Collectible
            || thisStack.Attributes.GetString("metalworkingstate", "settled") != "settled"
            || otherStack.Attributes.GetString("metalworkingstate", "settled") != "settled") return;

        ignoreAttributeSubTrees = (ignoreAttributeSubTrees ?? []).Concat(new[]
        {
            "metalworkingstate", "statechangetotalhours", "lastinquenchrangetotalhours", "lastintemperrangetotalhours"
        }).ToArray();
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.OnCreatedByCrafting))]
internal static class CraftedArmorQuenchStatePatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

    private static void Postfix(ItemSlot[] allInputSlots, ItemSlot outputSlot, IRecipeBase byRecipe)
    {
        if (outputSlot.Empty || outputSlot.Itemstack == null) return;

        bool outputWasClayCovered = outputSlot.Itemstack.Attributes.GetBool("clayCovered", false);
        int previousMaxDurability = outputSlot.Itemstack.Collectible.GetMaxDurability(outputSlot.Itemstack);
        if (QuenchableStateUtil.IsArmorOrArmorComponent(outputSlot.Itemstack))
        {
            var inputs = GetConsumedArmorInputs(allInputSlots, byRecipe).ToArray();
            int count = inputs.Sum(input => input.Quantity);
            if (count > 0)
            {
                float flat = inputs.Sum(input => QuenchableStatUtil.GetArmorFlatReductionBonus(input.Stack) * input.Quantity) / count;
                float durability = inputs.Sum(input => QuenchableStatUtil.GetArmorDurabilityBonus(input.Stack) * input.Quantity) / count;
                float penalty = inputs.Sum(input => (1f - QuenchableStatUtil.GetArmorPenaltyMultiplier(input.Stack)) * input.Quantity) / count;
                int quenches = (int)MathF.Round(inputs.Sum(input => (float)input.Stack.Attributes.GetInt("quenchIteration") * input.Quantity) / count);
                int tempers = (int)MathF.Round(inputs.Sum(input => (float)input.Stack.Attributes.GetInt("temperIteration") * input.Quantity) / count);
                QuenchableStateUtil.ApplyInheritedArmorQuenchState(outputSlot.Itemstack, flat > 0f, quenches, tempers);
                // A single source (including clay covering) retains its original progression.
                if (inputs.Length > 1 || inputs[0].Stack.Attributes.HasAttribute("armorQuenchDurabilityBonus"))
                {
                    outputSlot.Itemstack.Attributes.SetFloat("armorQuenchFlatBonus", flat);
                    outputSlot.Itemstack.Attributes.SetFloat("armorQuenchDurabilityBonus", durability);
                    outputSlot.Itemstack.Attributes.SetFloat("armorQuenchPenaltyBonus", penalty);
                }
                if (previousMaxDurability > 0 && outputSlot.Itemstack.Attributes.HasAttribute("durability"))
                {
                    int maxDurability = outputSlot.Itemstack.Collectible.GetMaxDurability(outputSlot.Itemstack);
                    float condition = Math.Clamp(outputSlot.Itemstack.Attributes.GetInt("durability") / (float)previousMaxDurability, 0f, 1f);
                    outputSlot.Itemstack.Attributes.SetInt("durability", (int)MathF.Round(maxDurability * condition));
                }
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

    private static IEnumerable<(ItemStack Stack, int Quantity)> GetConsumedArmorInputs(ItemSlot[] slots, IRecipeBase recipe)
    {
        if (recipe is GridRecipe grid && !grid.Shapeless && grid.ResolvedIngredients != null)
        {
            int width = (int)Math.Sqrt(slots.Length);
            for (int row = 0; row <= slots.Length / width - grid.Height; row++)
            for (int col = 0; col <= width - grid.Width; col++)
            {
                var matched = new List<(ItemStack Stack, int Quantity)>();
                bool valid = true;
                for (int index = 0; index < slots.Length; index++)
                {
                    int x = index % width - col;
                    int y = index / width - row;
                    var ingredient = x >= 0 && x < grid.Width && y >= 0 && y < grid.Height
                        ? grid.ResolvedIngredients[y * grid.Width + x] : null;
                    ItemStack? stack = slots[index].Itemstack;
                    if (ingredient == null ? stack != null : stack == null || !ingredient.SatisfiesAsIngredient(stack))
                    {
                        valid = false;
                        break;
                    }
                    if (stack != null && ingredient != null && ingredient.Consume && QuenchableStateUtil.IsArmorOrArmorComponent(stack))
                        matched.Add((stack, ingredient.Quantity));
                }
                if (!valid) continue;
                foreach (var input in matched) yield return input;
                yield break;
            }
            yield break;
        }

        var ingredients = recipe.RecipeIngredients.Where(ingredient => ingredient is not CraftingRecipeIngredient { Consume: false }).ToArray();
        int[] remaining = ingredients.Select(ingredient => ingredient.Quantity).ToArray();
        foreach (ItemSlot slot in slots)
        {
            if (slot.Empty) continue;
            int available = slot.StackSize;
            for (int i = 0; i < ingredients.Length && available > 0; i++)
            {
                if (remaining[i] <= 0 || !ingredients[i].SatisfiesAsIngredient(slot.Itemstack, false)) continue;
                int quantity = Math.Min(available, remaining[i]);
                available -= quantity;
                remaining[i] -= quantity;
                if (QuenchableStateUtil.IsArmorOrArmorComponent(slot.Itemstack))
                    yield return (slot.Itemstack, quantity);
            }
        }
    }

    private static void NormalizeCraftedWeaponQuenchState(ItemSlot[] allInputSlots, ItemStack output)
    {
        if (output.Collectible == null
            || QuenchableStateUtil.IsArmorOrArmorComponent(output)
            || !QuenchableStateUtil.IsFerrous(output)
            || QuenchableStateUtil.GetKind(output) != QuenchableStateUtil.WeaponKind)
        {
            return;
        }

        int bestQuenchIteration = 0;
        int bestTemperIteration = 0;
        float bestPowerValue = 0f;
        float bestDurationBonus = 0f;

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
            float durationBonus = Math.Max(0f, input.Itemstack.Attributes.GetFloat("durationbonus", 0f));
            bestDurationBonus = Math.Max(bestDurationBonus, durationBonus);

            if (quenchIteration > bestQuenchIteration
                || (quenchIteration == bestQuenchIteration && temperIteration > bestTemperIteration)
                || (quenchIteration == bestQuenchIteration && temperIteration == bestTemperIteration && powerValue > bestPowerValue))
            {
                bestQuenchIteration = quenchIteration;
                bestTemperIteration = temperIteration;
                bestPowerValue = powerValue;
            }
        }

        if (bestQuenchIteration <= 0 && bestPowerValue <= 0f && bestDurationBonus <= 0f)
        {
            return;
        }

        int finalQuenchIteration = bestQuenchIteration;
        int finalTemperIteration = Math.Clamp(bestTemperIteration, 0, bestQuenchIteration);
        float normalizedPowerValue = finalQuenchIteration > 0
            ? QuenchableStatUtil.GetWeaponQuenchDamageBonus(finalQuenchIteration, finalTemperIteration)
            : bestPowerValue;

        output.Attributes.SetInt("quenchIteration", finalQuenchIteration);
        output.Attributes.SetInt("temperIteration", finalTemperIteration);
        output.Attributes.SetFloat("powervalue", Math.Max(0f, normalizedPowerValue));
        if (bestDurationBonus > 0f)
        {
            output.Attributes.SetFloat("durationbonus", bestDurationBonus);
        }
        else
        {
            output.Attributes.RemoveAttribute("durationbonus");
        }

        NormalizeVisibleWeaponQuenchBuffs(output, normalizedPowerValue, bestDurationBonus);
        NormalizeCraftedWeaponDurability(output);
    }

    internal static bool NormalizeWeaponQuenchDamageBuff(ItemStack output)
    {
        if (output?.Collectible == null || QuenchableStateUtil.GetKind(output) != QuenchableStateUtil.WeaponKind)
        {
            return false;
        }

        int storedQuenchIteration = Math.Max(0, output.Attributes.GetInt("quenchIteration", 0));
        int quenchIteration = QuenchableStatUtil.GetWeaponQuenchIteration(output);
        if (quenchIteration <= 0)
        {
            return false;
        }

        int temperIteration = Math.Clamp(output.Attributes.GetInt("temperIteration", 0), 0, quenchIteration);
        float powerValue = QuenchableStatUtil.GetWeaponQuenchDamageBonus(quenchIteration, temperIteration);
        float durationBonus = Math.Max(0f, output.Attributes.GetFloat("durationbonus", 0f));

        bool changed = storedQuenchIteration != quenchIteration
            || Math.Abs(output.Attributes.GetFloat("powervalue", 0f) - powerValue) > 0.0001f
            || output.Attributes.GetInt("temperIteration", 0) != temperIteration;

        output.Attributes.SetInt("quenchIteration", quenchIteration);
        output.Attributes.SetInt("temperIteration", temperIteration);
        output.Attributes.SetFloat("powervalue", Math.Max(0f, powerValue));
        NormalizeVisibleWeaponQuenchBuffs(output, powerValue, durationBonus);

        return changed;
    }

    private static void NormalizeVisibleWeaponQuenchBuffs(ItemStack output, float powerValue, float durationBonus)
    {
        QuenchableStateUtil.RemoveBuffs(output, "attackpower", "miningspeed", "maxdurability");

        CollectibleBehaviorBuffable? behavior = output.Collectible?.GetBehavior<CollectibleBehaviorBuffable>();
        if (behavior == null)
        {
            return;
        }

        if (powerValue > 0f)
        {
            behavior.AddBuff(output, new AppliedCollectibleBuff
            {
                Code = "hardened",
                Multiplier = 1f + powerValue,
                StatCode = "attackpower"
            }, EnumBuffAddType.ReplaceOnDuplicate);
        }

        if (durationBonus > 0f)
        {
            behavior.AddBuff(output, new AppliedCollectibleBuff
            {
                Code = "hardened",
                Multiplier = 1f + durationBonus,
                StatCode = "maxdurability"
            }, EnumBuffAddType.ReplaceOnDuplicate);
        }
    }

    private static void NormalizeCraftedWeaponDurability(ItemStack output)
    {
        if (output.Collectible == null || !output.Attributes.HasAttribute("durability"))
        {
            return;
        }

        int maxDurability = output.Collectible.GetMaxDurability(output);
        if (maxDurability <= 0)
        {
            return;
        }

        int currentDurability = output.Collectible.GetRemainingDurability(output);
        if (currentDurability <= 0 || currentDurability > maxDurability)
        {
            output.Attributes.RemoveAttribute("durability");
        }
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetMaxDurability))]
internal static class ArmorQuenchDurabilityPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

    private static void Postfix(ItemStack itemstack, ref int __result)
    {
        if (__result <= 0 || !QuenchableStateUtil.IsArmorOrArmorComponent(itemstack))
        {
            return;
        }

        float bonus = QuenchableStatUtil.GetArmorDurabilityBonus(itemstack);
        if (bonus <= 0)
        {
            return;
        }

        __result = Math.Max(1, (int)MathF.Round(__result * (1f + bonus)));
    }
}

[HarmonyPatch(typeof(CollectibleBehaviorQuenchable), nameof(CollectibleBehaviorQuenchable.GetHeldItemInfo))]
internal static class ArmorQuenchTooltipPatch
{
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

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

        float durabilityBonus = QuenchableStatUtil.GetArmorDurabilityBonus(inSlot.Itemstack);
        if (durabilityBonus > 0f)
            AppendLineOnce(dsc, Lang.Get("combatoverhaul:quenchable-armor-durability-average", (durabilityBonus * 100f).ToString("0.##")));

        float flatBonus = QuenchableStatUtil.GetArmorFlatReductionBonus(inSlot.Itemstack);
        if (flatBonus > 0f)
            AppendLineOnce(dsc, Lang.Get("combatoverhaul:quenchable-armor-flat-average", flatBonus.ToString("0.###")));

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
    [HarmonyPrepare]
    private static bool Prepare() => QuenchablePatchGate.Prepare();

    private static void Postfix(ItemSlot inSlot, StringBuilder dsc)
    {
        WeaponQuenchTooltipPatch.NormalizeTooltip(inSlot, dsc);
        ArmorQuenchTooltipPatch.AppendTooltip(inSlot, dsc);
    }
}

internal static class WeaponQuenchTooltipPatch
{
    internal static void NormalizeTooltip(ItemSlot inSlot, StringBuilder dsc)
    {
        ItemStack? stack = inSlot.Itemstack;
        if (inSlot.Empty
            || stack?.Collectible == null
            || QuenchableStateUtil.GetKind(stack) != QuenchableStateUtil.WeaponKind)
        {
            return;
        }

        float previousStoredDamageMultiplier = 1f + Math.Max(0f, stack.Attributes.GetFloat("powervalue", 0f));
        if (CraftedArmorQuenchStatePatch.NormalizeWeaponQuenchDamageBuff(stack))
        {
            inSlot.MarkDirty();
        }

        float damageMultiplier = QuenchableStatUtil.GetAttackPowerMultiplier(stack);
        List<string> linesToRemove = new()
        {
            Lang.Get("Attack power: {0} damage", stack.Collectible.GetAttackPower(stack).ToString("0.#", CultureInfo.CurrentCulture)),
            Lang.Get("Attack tier: {0}", stack.Collectible.GetToolTier(inSlot)),
            Lang.Get("mulbuff-hardened-attackpower", damageMultiplier - 1f, 0),
            Lang.Get("mulbuff-hardened-attackpower", previousStoredDamageMultiplier - 1f, 0)
        };

        RemoveTooltipLines(dsc, linesToRemove);
        if (damageMultiplier <= 1.0001f)
        {
            return;
        }

        float bonusPercent = (damageMultiplier - 1f) * 100f;
        string bonusText = bonusPercent.ToString("0.#", CultureInfo.CurrentCulture);
        AppendLineOnce(dsc, $"<font color=\"#00bb00\">{Lang.Get("combatoverhaul:quenchable-weapon-damage-multiplier", bonusText)}</font>");
    }

    private static void RemoveTooltipLines(StringBuilder dsc, IReadOnlyCollection<string> linesToRemove)
    {
        HashSet<string> normalizedTargets = linesToRemove
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .Select(NormalizeLine)
            .ToHashSet(StringComparer.Ordinal);

        if (normalizedTargets.Count == 0)
        {
            return;
        }

        string[] lines = dsc.ToString().Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        dsc.Clear();

        foreach (string line in lines)
        {
            if (normalizedTargets.Contains(NormalizeLine(line)))
            {
                continue;
            }

            dsc.AppendLine(line);
        }
    }

    private static string NormalizeLine(string line)
    {
        return line
            .Replace("<font color=\"#00bb00\">", "", StringComparison.Ordinal)
            .Replace("<font color=\"#bb0000\">", "", StringComparison.Ordinal)
            .Replace("</font>", "", StringComparison.Ordinal)
            .Trim();
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
