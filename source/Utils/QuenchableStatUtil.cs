using CombatOverhaul.DamageSystems;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.Utils;

public static class QuenchableStatUtil
{
    // Keep legacy protection-level quench path available, but disabled for now.
    private const bool UseLegacyArmorProtectionLevelBonus = false;
    private const float ArmorFlatReductionPerQuench = 0.20f;
    public const float TemperShatterChanceMultiplier = 0.80f;
    public const float TemperPowerMultiplier = 0.92f;

    public static float GetAttackPowerMultiplier(ItemStack? stack)
    {
        if (stack?.Collectible == null || QuenchableStateUtil.GetKind(stack) == QuenchableStateUtil.ArmorKind)
        {
            return 1f;
        }

        // Vanilla quenching/Buffable stores the visible tooltip state on the stack,
        // but CO melee damage reads only CO's own damageMultiplier.  Do not require
        // the final crafted weapon to still have CollectibleBehaviorQuenchable here:
        // some crafted Armory/CO weapons inherit the quench attributes from parts,
        // show the buff tooltip, but no longer have the Quenchable behavior itself.
        float buffMultiplier = GetBuffMultiplier(stack, "attackpower");
        float powerValueMultiplier = 1f + Math.Max(0f, stack.Attributes.GetFloat("powervalue", 0f));
        float vanillaStateMultiplier = GetVanillaStateAttackPowerMultiplier(stack);

        // Keep CO damage aligned with vanilla quench progression state on the stack
        // (including recrafted items where powervalue/buffs may lag behind iteration state).
        return Math.Max(Math.Max(buffMultiplier, powerValueMultiplier), vanillaStateMultiplier);
    }

    public static DamageResistData ApplyArmorProtection(ItemStack? stack, DamageResistData resists)
    {
        QuenchableStateUtil.SanitizeArmorQuenchBuffs(stack);

        if (UseLegacyArmorProtectionLevelBonus)
        {
            int protectionBonus = GetArmorProtectionLevelBonus(stack);
            if (protectionBonus <= 0)
            {
                return resists;
            }

            return new DamageResistData(
                resists.Resists.ToDictionary(entry => entry.Key, entry => entry.Value > 0 ? entry.Value + protectionBonus : entry.Value),
                resists.FlatDamageReduction.ToDictionary(entry => entry.Key, entry => entry.Value),
                resists.DirectionsCoverage);
        }

        float flatReductionBonus = GetArmorFlatReductionBonus(stack);
        if (flatReductionBonus <= 0f)
        {
            return resists;
        }

        return new DamageResistData(
            resists.Resists.ToDictionary(entry => entry.Key, entry => entry.Value),
            resists.FlatDamageReduction.ToDictionary(entry => entry.Key, entry => entry.Value + flatReductionBonus),
            resists.DirectionsCoverage);
    }

    private static int GetArmorProtectionLevelBonus(ItemStack? stack)
    {
        if (stack?.Collectible == null || !QuenchableStateUtil.IsFerrous(stack))
        {
            return 0;
        }

        return QuenchableStateUtil.GetArmorQuenchState(stack);
    }

    private static float GetArmorFlatReductionBonus(ItemStack? stack)
    {
        if (stack?.Collectible == null || !QuenchableStateUtil.IsFerrous(stack))
        {
            return 0f;
        }

        if (QuenchableStateUtil.HasDirectArmorQuench(stack))
        {
            return ArmorFlatReductionPerQuench;
        }

        // Backward compatibility for stacks created before explicit direct flag existed.
        if (QuenchableStateUtil.GetArmorQuenchMode(stack) == QuenchableStateUtil.ArmorQuenchModeDirect
            && QuenchableStateUtil.GetArmorQuenchState(stack) > 0)
        {
            return ArmorFlatReductionPerQuench;
        }

        return 0f;
    }

    public static float GetArmorPenaltyMultiplier(ItemStack? stack)
    {
        QuenchableStateUtil.SanitizeArmorQuenchBuffs(stack);

        if (stack?.Collectible == null || !QuenchableStateUtil.IsFerrous(stack))
        {
            return 1f;
        }

        string mode = QuenchableStateUtil.GetArmorQuenchMode(stack);
        bool isClayPath = mode == QuenchableStateUtil.ArmorQuenchModeClay;

        // Backward compatibility: if old stacks have quench counters but no explicit mode, treat as clay path.
        if (!isClayPath && string.IsNullOrEmpty(mode) && stack.Attributes.GetInt("quenchIteration", 0) > 0 && !QuenchableStateUtil.HasDirectArmorQuench(stack))
        {
            isClayPath = true;
        }

        if (!isClayPath)
        {
            return 1f;
        }

        int quenchIteration = stack.Attributes.GetInt("quenchIteration", 0);
        if (quenchIteration <= 0)
        {
            return 1f;
        }

        float effectiveQuench = quenchIteration * GetTemperPowerFactor(stack);

        // Each clay-quench step reduces armor penalties by 10%, capped at 50%.
        // Tempering follows vanilla's tradeoff curve: the risk goes down more than the buff does.
        return Math.Max(1f - (effectiveQuench * 0.10f), 0.5f);
    }

    public static float GetTemperPowerFactor(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return 1f;
        }

        int temperIteration = Math.Max(0, stack.Attributes.GetInt("temperIteration", 0));
        if (temperIteration <= 0)
        {
            return 1f;
        }

        return MathF.Pow(TemperPowerMultiplier, temperIteration);
    }

    private static float GetBuffMultiplier(ItemStack stack, string statCode)
    {
        ITreeAttribute? buffs = stack.Attributes.GetTreeAttribute("buffs");
        if (buffs == null)
        {
            return 1f;
        }

        float multiplier = 1f;
        foreach (IAttribute value in buffs.Values)
        {
            if (value is not ITreeAttribute buff)
            {
                continue;
            }

            if (!string.Equals(buff.GetString("statcode", ""), statCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            multiplier *= buff.GetFloat("multiplier", 1f);
        }

        return multiplier;
    }

    private static float GetVanillaStateAttackPowerMultiplier(ItemStack stack)
    {
        int quenchIteration = Math.Max(0, stack.Attributes.GetInt("quenchIteration", 0));
        if (quenchIteration <= 0)
        {
            return 1f;
        }

        int temperIteration = Math.Max(0, stack.Attributes.GetInt("temperIteration", 0));
        float powerPerQuench = GetPowerPerQuench(stack);
        float effectivePower = quenchIteration * MathF.Pow(TemperPowerMultiplier, temperIteration) * powerPerQuench;

        return 1f + Math.Max(0f, effectivePower);
    }

    private static float GetPowerPerQuench(ItemStack stack)
    {
        const float fallback = 0.1f;

        CollectibleBehaviorQuenchable? behavior = stack.Collectible.GetCollectibleBehavior<CollectibleBehaviorQuenchable>(true);
        if (behavior == null)
        {
            return fallback;
        }

        try
        {
            object? value = behavior.GetType().GetProperty("PowerPerQuench")?.GetValue(behavior);
            if (value is float f && f > 0f)
            {
                return f;
            }

            if (value is double d && d > 0d)
            {
                return (float)d;
            }

            if (value is int i && i > 0)
            {
                return i;
            }
        }
        catch
        {
        }

        return fallback;
    }
}
