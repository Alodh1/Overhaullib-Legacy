using HarmonyLib;
using System.Text;
using System.Text.RegularExpressions;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace CombatOverhaul.Integration;

[HarmonyPatch(typeof(ItemSpear), nameof(ItemSpear.GetHeldItemInfo))]
public static class SpearTooltipPatches
{
    [HarmonyPostfix]
    public static void ItemSpear_GetHeldItemInfo_Postfix(ItemSlot inSlot, StringBuilder dsc)
    {
        // Vanilla spears show one-/two-handed damage as min-max ranges.
        // Convert those stance lines to explicit values to match CO-style readability.
        string text = dsc.ToString();
        text = ReplaceRangeWithExplicitValue(text, "One-handed");
        text = ReplaceRangeWithExplicitValue(text, "Two-handed");
        text = SpearTooltipProficiencies.InsertIfMissing(text);

        dsc.Clear();
        dsc.Append(text);
    }

    private static string ReplaceRangeWithExplicitValue(string text, string stanceLabel)
    {
        // Example input line:
        // One-handed: 1-4 (1-6 tier) Piercing, Blunt
        // Output:
        // One-handed: 4 (6 tier) Piercing, Blunt
        string pattern = $@"(?m)^({Regex.Escape(stanceLabel)}:\s*)(\d+)\s*-\s*(\d+)(\s*\((\d+)(?:\s*-\s*(\d+))?\s+tier\).*)$";
        return Regex.Replace(
            text,
            pattern,
            static match =>
            {
                string prefix = match.Groups[1].Value;
                string max = match.Groups[3].Value;
                string minTier = match.Groups[5].Value;
                string maxTier = match.Groups[6].Success ? match.Groups[6].Value : minTier;
                string suffix = match.Groups[4].Value;
                suffix = Regex.Replace(suffix, @"\(\d+(?:\s*-\s*\d+)?\s+tier\)", $"({maxTier} tier)");
                return $"{prefix}{max}{suffix}";
            });
    }
}

[HarmonyPatch(typeof(CollectibleObject), nameof(CollectibleObject.GetHeldItemInfo))]
public static class SpearTooltipGenericPatches
{
    [HarmonyPostfix]
    public static void CollectibleObject_GetHeldItemInfo_Postfix(CollectibleObject __instance, ItemSlot inSlot, StringBuilder dsc)
    {
        string path = __instance?.Code?.Path ?? "";
        if (!path.Contains("spear", StringComparison.OrdinalIgnoreCase)) return;
        if (path.Contains("spearhead", StringComparison.OrdinalIgnoreCase)) return;
        if (path.Contains("head", StringComparison.OrdinalIgnoreCase)) return;

        string text = dsc.ToString();
        text = ReplaceRangeWithExplicitValue(text, "One-handed");
        text = ReplaceRangeWithExplicitValue(text, "Two-handed");
        text = SpearTooltipProficiencies.InsertIfMissing(text);

        dsc.Clear();
        dsc.Append(text);
    }

    private static string ReplaceRangeWithExplicitValue(string text, string stanceLabel)
    {
        string pattern = $@"(?m)^({Regex.Escape(stanceLabel)}:\s*)(\d+)\s*-\s*(\d+)(\s*\((\d+)(?:\s*-\s*(\d+))?\s+tier\).*)$";
        return Regex.Replace(
            text,
            pattern,
            static match =>
            {
                string prefix = match.Groups[1].Value;
                string max = match.Groups[3].Value;
                string minTier = match.Groups[5].Value;
                string maxTier = match.Groups[6].Success ? match.Groups[6].Value : minTier;
                string suffix = match.Groups[4].Value;
                suffix = Regex.Replace(suffix, @"\(\d+(?:\s*-\s*\d+)?\s+tier\)", $"({maxTier} tier)");
                return $"{prefix}{max}{suffix}";
            });
    }
}

public static class SpearTooltipProficiencies
{
    public static string InsertIfMissing(string text)
    {
        string proficiencyLabel = Lang.Get("combatoverhaul:iteminfo-proficiency", "");
        int placeholderIndex = proficiencyLabel.IndexOf("{0}", StringComparison.Ordinal);
        if (placeholderIndex >= 0)
        {
            proficiencyLabel = proficiencyLabel[..placeholderIndex];
        }

        if (text.Contains(proficiencyLabel, StringComparison.OrdinalIgnoreCase)) return text;

        string statsList = string.Join(", ",
            Lang.Get("combatoverhaul:proficiency-meleeProficiency"),
            Lang.Get("combatoverhaul:proficiency-spearsProficiency"));
        string proficiencyLine = Lang.Get("combatoverhaul:iteminfo-proficiency", statsList);

        Match stanceMatch = Regex.Match(text, @"(?m)^One-handed:.*(?:\r?\n|$)");
        if (!stanceMatch.Success)
        {
            stanceMatch = Regex.Match(text, @"(?m)^Two-handed:.*(?:\r?\n|$)");
        }

        if (stanceMatch.Success)
        {
            return text.Insert(stanceMatch.Index, proficiencyLine + Environment.NewLine);
        }

        if (!text.EndsWith('\n')) text += Environment.NewLine;
        return text + proficiencyLine + Environment.NewLine;
    }
}
