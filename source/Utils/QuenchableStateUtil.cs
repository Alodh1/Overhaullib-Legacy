using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

namespace CombatOverhaul.Utils;

public static class QuenchableStateUtil
{
    public const string KindAttribute = "quenchBuffKind";
    public const string ArmorKind = "armor";
    public const string WeaponKind = "weapon";
    public const string ToolKind = "tool";
    public const string ArmorQuenchModeAttribute = "armorQuenchMode";
    public const string ArmorQuenchModeDirect = "direct";
    public const string ArmorQuenchModeClay = "clay";
    public const string ArmorDirectQuenchedAttribute = "armorDirectQuenched";

    public static string GetKind(ItemStack? stack)
    {
        if (stack?.Collectible == null)
        {
            return "";
        }

        string? explicitKind = stack.ItemAttributes?[KindAttribute].AsString(null);
        if (!string.IsNullOrEmpty(explicitKind))
        {
            return explicitKind;
        }

        string code = stack.Collectible.Code?.ToShortString() ?? "";
        if (LooksLikeArmorComponent(code))
        {
            return ArmorKind;
        }

        if (LooksLikeWeaponOrWeaponPart(code))
        {
            return WeaponKind;
        }

        if (stack.Collectible.GetTool(new DummySlot(stack)) != null)
        {
            return ToolKind;
        }

        return "";
    }

    public static bool IsArmorQuenchComponent(ItemStack? stack)
    {
        return GetKind(stack) == ArmorKind;
    }

    public static bool IsArmorOrArmorComponent(ItemStack? stack)
    {
        if (stack?.Collectible == null)
        {
            return false;
        }

        if (IsArmorQuenchComponent(stack))
        {
            return true;
        }

        string code = stack.Collectible.Code?.ToShortString() ?? "";
        return code.StartsWith("armory:armor-", StringComparison.Ordinal)
            || code.StartsWith("game:armor-", StringComparison.Ordinal)
            || stack.Collectible.GetCollectibleBehavior<CombatOverhaul.Armor.ArmorBehavior>(true) != null;
    }

    public static int GetArmorQuenchState(ItemStack? stack)
    {
        MigrateLegacyArmorQuenchState(stack);

        if (stack?.Attributes == null)
        {
            return 0;
        }

        int armorLevel = stack.Attributes.GetInt("armorQuenchLevel", 0);
        int quenchIteration = stack.Attributes.GetInt("quenchIteration", 0);
        return Math.Max(armorLevel, quenchIteration);
    }


    public static bool HasAnyQuenchState(ItemStack? stack)
    {
        if (stack?.Attributes == null)
        {
            return false;
        }

        if (IsArmorOrArmorComponent(stack))
        {
            MigrateLegacyArmorQuenchState(stack);

            if (GetArmorQuenchState(stack) > 0 || HasDirectArmorQuench(stack))
            {
                return true;
            }
        }

        if (stack.Attributes.GetFloat("powervalue", 0f) > 0f
            || stack.Attributes.GetFloat("durationbonus", 0f) > 0f
            || stack.Attributes.GetInt("quenchIteration", 0) > 0)
        {
            return true;
        }

        return HasLegacyBuffStat(stack, "attackpower", "miningspeed", "maxdurability", "power", "durability");
    }

    public static string GetArmorQuenchMode(ItemStack? stack)
    {
        MigrateLegacyArmorQuenchState(stack);

        if (stack?.Attributes == null)
        {
            return "";
        }

        return stack.Attributes.GetString(ArmorQuenchModeAttribute, "");
    }

    public static void ApplyArmorQuenchState(ItemStack stack, int state)
    {
        if (state <= 0)
        {
            stack.Attributes.RemoveAttribute("armorQuenchLevel");
            stack.Attributes.RemoveAttribute(ArmorQuenchModeAttribute);
            stack.Attributes.RemoveAttribute(ArmorDirectQuenchedAttribute);
            stack.Attributes.RemoveAttribute("durationbonus");
            stack.Attributes.RemoveAttribute("powervalue");
            stack.Attributes.RemoveAttribute("quenchIteration");
            stack.Attributes.RemoveAttribute("temperIteration");
            stack.Attributes.RemoveAttribute("clayCovered");
            RemoveBuffs(stack, "attackpower", "miningspeed", "maxdurability");
            SanitizeArmorQuenchBuffs(stack);
            return;
        }

        stack.Attributes.SetInt("armorQuenchLevel", 1);
        stack.Attributes.SetInt("quenchIteration", 1);
        stack.Attributes.SetInt("temperIteration", 0);
        stack.Attributes.SetFloat("powervalue", 0f);
        stack.Attributes.SetFloat("durationbonus", 0.10f);
        stack.Attributes.SetBool("clayCovered", false);
        RemoveBuffs(stack, "attackpower", "miningspeed");
        SanitizeArmorQuenchBuffs(stack);
    }

    public static void ApplyInheritedArmorQuenchState(ItemStack stack, bool directQuenched, int clayQuenchIteration, int clayTemperIteration)
    {
        if (!directQuenched && clayQuenchIteration <= 0)
        {
            ApplyArmorQuenchState(stack, 0);
            return;
        }

        stack.Attributes.SetInt("armorQuenchLevel", 1);
        if (directQuenched)
        {
            stack.Attributes.SetBool(ArmorDirectQuenchedAttribute, true);
        }
        else
        {
            stack.Attributes.RemoveAttribute(ArmorDirectQuenchedAttribute);
        }

        if (clayQuenchIteration > 0)
        {
            stack.Attributes.SetString(ArmorQuenchModeAttribute, ArmorQuenchModeClay);
            stack.Attributes.SetInt("quenchIteration", clayQuenchIteration);
            stack.Attributes.SetInt("temperIteration", Math.Clamp(clayTemperIteration, 0, clayQuenchIteration));
        }
        else
        {
            stack.Attributes.SetString(ArmorQuenchModeAttribute, ArmorQuenchModeDirect);
            stack.Attributes.SetInt("quenchIteration", 0);
            stack.Attributes.SetInt("temperIteration", 0);
        }

        stack.Attributes.SetFloat("powervalue", 0f);
        stack.Attributes.SetFloat("durationbonus", 0.10f);
        stack.Attributes.SetBool("clayCovered", false);
        RemoveBuffs(stack, "attackpower", "miningspeed");
        SanitizeArmorQuenchBuffs(stack);
    }

    public static void ApplyDirectArmorQuench(ItemStack stack)
    {
        int clayQuenchIteration = Math.Max(0, stack.Attributes.GetInt("quenchIteration", 0));
        int clayTemperIteration = Math.Clamp(stack.Attributes.GetInt("temperIteration", 0), 0, clayQuenchIteration);

        stack.Attributes.SetInt("armorQuenchLevel", 1);
        stack.Attributes.SetBool(ArmorDirectQuenchedAttribute, true);
        stack.Attributes.SetString(ArmorQuenchModeAttribute, clayQuenchIteration > 0 ? ArmorQuenchModeClay : ArmorQuenchModeDirect);
        // Direct quench is the one-time flat-reduction flag only.  It must not
        // reset or consume the independent clay quench/temper chain.
        stack.Attributes.SetInt("quenchIteration", clayQuenchIteration);
        stack.Attributes.SetInt("temperIteration", clayTemperIteration);
        stack.Attributes.SetFloat("powervalue", 0f);
        stack.Attributes.SetFloat("durationbonus", 0.10f);
        stack.Attributes.SetBool("clayCovered", false);
        RemoveBuffs(stack, "attackpower", "miningspeed");
        SanitizeArmorQuenchBuffs(stack);
    }

    public static void ApplyClayArmorQuench(ItemStack stack)
    {
        int quenchIteration = stack.Attributes.GetInt("quenchIteration", 0) + 1;
        stack.Attributes.SetInt("armorQuenchLevel", 1);
        stack.Attributes.SetString(ArmorQuenchModeAttribute, ArmorQuenchModeClay);
        stack.Attributes.SetInt("quenchIteration", quenchIteration);
        stack.Attributes.SetInt("temperIteration", Math.Min(stack.Attributes.GetInt("temperIteration", 0), quenchIteration));
        stack.Attributes.SetBool("clayCovered", false);
        RemoveBuffs(stack, "attackpower", "miningspeed");
        SanitizeArmorQuenchBuffs(stack);
    }

    public static bool HasDirectArmorQuench(ItemStack? stack)
    {
        MigrateLegacyArmorQuenchState(stack);

        if (stack?.Attributes == null)
        {
            return false;
        }

        return stack.Attributes.GetBool(ArmorDirectQuenchedAttribute, false);
    }

    public static void NormalizeGenericBuffs(ItemStack? stack)
    {
        MigrateLegacyArmorQuenchState(stack);

        string kind = GetKind(stack);
        if (kind == ArmorKind)
        {
            RemoveBuffs(stack, "attackpower", "miningspeed", "maxdurability");
            SanitizeArmorQuenchBuffs(stack);
        }
        else if (kind == WeaponKind)
        {
            RemoveBuffs(stack, "miningspeed");
        }
        else if (kind == ToolKind)
        {
            RemoveBuffs(stack, "attackpower");
        }
    }

    public static void SanitizeArmorQuenchBuffs(ItemStack? stack)
    {
        MigrateLegacyArmorQuenchState(stack);

        if (stack?.Attributes == null || !IsArmorOrArmorComponent(stack) || !IsFerrous(stack))
        {
            return;
        }

        // Armor quench in compat mode should never retain vanilla power/protection-tier scaling.
        stack.Attributes.SetFloat("powervalue", 0f);

        ITreeAttribute? buffs = stack.Attributes.GetTreeAttribute("buffs");
        if (buffs == null)
        {
            return;
        }

        List<string> toRemove = new();
        foreach (KeyValuePair<string, IAttribute> entry in buffs)
        {
            if (entry.Value is not ITreeAttribute buff)
            {
                continue;
            }

            string statCode = buff.GetString("statcode", "");
            if (ShouldStripArmorQuenchStat(statCode))
            {
                toRemove.Add(entry.Key);
            }
        }

        foreach (string key in toRemove)
        {
            buffs.RemoveAttribute(key);
        }

        if (buffs.Count == 0)
        {
            stack.Attributes.RemoveAttribute("buffs");
        }
    }

    public static void MigrateLegacyArmorQuenchState(ItemStack? stack)
    {
        if (stack?.Attributes == null || !IsArmorOrArmorComponent(stack) || !IsFerrous(stack))
        {
            return;
        }

        ITreeAttribute attrs = stack.Attributes;
        string mode = attrs.GetString(ArmorQuenchModeAttribute, "");
        bool hasDirect = attrs.GetBool(ArmorDirectQuenchedAttribute, false);
        int armorLevel = attrs.GetInt("armorQuenchLevel", 0);
        int quenchIteration = attrs.GetInt("quenchIteration", 0);
        int temperIteration = attrs.GetInt("temperIteration", 0);

        bool hasLegacyPower = attrs.GetFloat("powervalue", 0f) > 0f || HasLegacyBuffStat(stack, "attackpower", "power", "tier", "protection", "armor");
        bool hasLegacyDurability = attrs.GetFloat("durationbonus", 0f) > 0f || HasLegacyBuffStat(stack, "maxdurability");

        bool changed = false;

        if (hasLegacyPower && !hasDirect)
        {
            attrs.SetBool(ArmorDirectQuenchedAttribute, true);
            hasDirect = true;
            changed = true;
        }

        if (hasLegacyDurability && quenchIteration <= 0)
        {
            quenchIteration = 1;
            attrs.SetInt("quenchIteration", quenchIteration);
            changed = true;
        }

        if (hasLegacyDurability && string.IsNullOrEmpty(mode))
        {
            attrs.SetString(ArmorQuenchModeAttribute, ArmorQuenchModeClay);
            mode = ArmorQuenchModeClay;
            changed = true;
        }

        if (hasDirect && string.IsNullOrEmpty(mode))
        {
            attrs.SetString(ArmorQuenchModeAttribute, ArmorQuenchModeDirect);
            changed = true;
        }

        int clampedTemperIteration = Math.Clamp(temperIteration, 0, Math.Max(0, quenchIteration));
        if (clampedTemperIteration != temperIteration)
        {
            attrs.SetInt("temperIteration", clampedTemperIteration);
            changed = true;
        }

        if ((hasDirect || quenchIteration > 0 || hasLegacyPower || hasLegacyDurability) && armorLevel <= 0)
        {
            attrs.SetInt("armorQuenchLevel", 1);
            changed = true;
        }

        if (changed)
        {
            attrs.SetFloat("powervalue", 0f);
            attrs.SetBool("clayCovered", false);
        }
    }

    public static void RemoveBuffs(ItemStack? stack, params string[] statCodes)
    {
        if (stack?.Attributes == null || statCodes.Length == 0)
        {
            return;
        }

        ITreeAttribute? buffs = stack.Attributes.GetTreeAttribute("buffs");
        if (buffs == null)
        {
            return;
        }

        HashSet<string> removeCodes = new(statCodes, StringComparer.OrdinalIgnoreCase);
        List<string> toRemove = new();

        foreach (KeyValuePair<string, IAttribute> entry in buffs)
        {
            string key = entry.Key;
            if (entry.Value is not ITreeAttribute buff)
            {
                continue;
            }

            string statCode = buff.GetString("statcode", "");
            if (removeCodes.Contains(statCode))
            {
                toRemove.Add(key);
            }
        }

        foreach (string key in toRemove)
        {
            buffs.RemoveAttribute(key);
        }

        if (buffs.Count == 0)
        {
            stack.Attributes.RemoveAttribute("buffs");
        }
    }

    public static bool IsFerrous(ItemStack? stack)
    {
        string code = stack?.Collectible?.Code?.Path ?? "";
        return code.Contains("-iron", StringComparison.Ordinal)
            || code.Contains("-meteoriciron", StringComparison.Ordinal)
            || code.Contains("-steel", StringComparison.Ordinal)
            || LooksLikeSteelBackedPlatedArmor(code);
    }

    private static bool LooksLikeSteelBackedPlatedArmor(string code)
    {
        if (!code.StartsWith("armor-", StringComparison.Ordinal))
        {
            return false;
        }

        return code.Contains("-plate-gold", StringComparison.Ordinal)
            || code.Contains("-plate-silver", StringComparison.Ordinal);
    }

    private static bool ShouldStripArmorQuenchStat(string statCode)
    {
        if (string.IsNullOrWhiteSpace(statCode))
        {
            return false;
        }

        return string.Equals(statCode, "attackpower", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statCode, "miningspeed", StringComparison.OrdinalIgnoreCase)
            || string.Equals(statCode, "maxdurability", StringComparison.OrdinalIgnoreCase)
            || statCode.Contains("protection", StringComparison.OrdinalIgnoreCase)
            || statCode.Contains("armor", StringComparison.OrdinalIgnoreCase)
            || statCode.Contains("tier", StringComparison.OrdinalIgnoreCase)
            || statCode.Contains("power", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLegacyBuffStat(ItemStack stack, params string[] needles)
    {
        ITreeAttribute? buffs = stack.Attributes.GetTreeAttribute("buffs");
        if (buffs == null)
        {
            return false;
        }

        foreach (KeyValuePair<string, IAttribute> entry in buffs)
        {
            if (entry.Value is not ITreeAttribute buff)
            {
                continue;
            }

            string statCode = buff.GetString("statcode", "");
            if (string.IsNullOrEmpty(statCode))
            {
                continue;
            }

            foreach (string needle in needles)
            {
                if (string.IsNullOrEmpty(needle))
                {
                    continue;
                }

                if (string.Equals(statCode, needle, StringComparison.OrdinalIgnoreCase) || statCode.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool LooksLikeArmorComponent(string code)
    {
        return code.StartsWith("armory:part-", StringComparison.Ordinal) && code.Contains("plate", StringComparison.Ordinal)
            || code == "game:metalplate-iron" || code == "game:metalplate-meteoriciron" || code == "game:metalplate-steel"
            || code == "game:metalchain-iron" || code == "game:metalchain-meteoriciron" || code == "game:metalchain-steel"
            || code == "game:metalscale-iron" || code == "game:metalscale-meteoriciron" || code == "game:metalscale-steel";
    }

    private static bool LooksLikeWeaponOrWeaponPart(string code)
    {
        string path = code.Contains(':') ? code.Split(':', 2)[1] : code;
        return path.Contains("blade", StringComparison.Ordinal)
            || path.Contains("spear", StringComparison.Ordinal)
            || path.Contains("sword", StringComparison.Ordinal)
            || path.Contains("sabre", StringComparison.Ordinal)
            || path.Contains("javelin", StringComparison.Ordinal)
            || path.Contains("halberd", StringComparison.Ordinal)
            || path.Contains("mace", StringComparison.Ordinal)
            || path.Contains("poleaxe", StringComparison.Ordinal)
            || path.Contains("longaxe", StringComparison.Ordinal)
            || path.Contains("pike", StringComparison.Ordinal)
            || path.Contains("dagger", StringComparison.Ordinal);
    }
}
