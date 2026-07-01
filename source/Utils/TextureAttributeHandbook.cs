using CombatOverhaul.Implementations;
using Vintagestory.API.Common;
using Vintagestory.API.Client;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Utils;

public static class TextureAttributeHandbook
{
    public static string[] GetTextureAttributes(CollectibleObject collectible)
    {
        return collectible.CollectibleBehaviors?
            .SelectMany(behavior => behavior switch
            {
                CombatOverhaul.TexturesFromAttributes textures => textures.TextureAttributes,
                CombatOverhaul.TextureFromAttributes texture => texture.TextureAttributes,
                _ => Array.Empty<string>()
            })
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
            .Distinct(StringComparer.Ordinal)
            .ToArray() ?? Array.Empty<string>();
    }

    public static string PageCodeForStack(ItemStack stack, IEnumerable<string> attributesToIgnore)
    {
        string[] attributes = Normalize(attributesToIgnore);
        if (attributes.Length == 0) return GuiHandbookItemStackPage.PageCodeForStack(stack);

        ItemStack pageStack = stack.Clone();
        RemoveAttributes(pageStack.Attributes, attributes);
        return GuiHandbookItemStackPage.PageCodeForStack(pageStack);
    }

    public static string VisualAttributePageCodeForStack(ItemStack stack)
    {
        string[] attributes = GetVisualAttributes(stack);
        if (attributes.Length > 0)
        {
            return PageCodeForStack(stack, attributes);
        }

        return VisualGroupPageCodeForStack(stack) ?? GuiHandbookItemStackPage.PageCodeForStack(stack);
    }

    public static bool MatchesIgnoringTextureAttributes(ItemStack? thisStack, ItemStack? otherStack)
    {
        if (thisStack?.Collectible == null || otherStack?.Collectible == null) return false;

        if (!ShouldUseGroupedHandbookPage(thisStack) || !ShouldUseGroupedHandbookPage(otherStack)) return false;

        return VisualAttributePageCodeForStack(thisStack) == VisualAttributePageCodeForStack(otherStack);
    }

    public static string? GroupPageCodeForStack(ItemStack stack)
    {
        AssetLocation? groupCode = GroupCodeForStack(stack);
        return groupCode == null ? null : $"{ItemClassMethods.Name(stack.Class)}-{groupCode.ToShortString()}";
    }

    public static AssetLocation? GroupCodeForStack(ItemStack stack)
    {
        if (stack?.Collectible?.Code == null) return null;

        string[] groups = GetHandbookGroupBy(stack.Collectible);
        if (groups.Length == 0) return null;

        string wildcard = NormalizeHandbookGroupWildcard(groups[0], stack);
        if (string.IsNullOrWhiteSpace(wildcard)) return null;

        return wildcard.Contains(':')
            ? new AssetLocation(wildcard)
            : new AssetLocation(stack.Collectible.Code.Domain, wildcard);
    }

    public static string[] GetHandbookGroupBy(CollectibleObject collectible)
    {
        return collectible.Attributes?["handbook"]?["groupBy"].AsArray<string>() ?? Array.Empty<string>();
    }

    public static bool ShouldUseGroupedHandbookPage(ItemStack? stack)
    {
        return stack?.Collectible != null && (GetVisualAttributes(stack).Length > 0 || VisualGroupPageCodeForStack(stack) != null);
    }

    public static string[] GetVisualAttributes(ItemStack? stack)
    {
        if (stack?.Collectible == null) return Array.Empty<string>();

        return Normalize(
            GetTextureAttributes(stack.Collectible)
                .Concat(GetConfiguredVisualAttributes(stack.Collectible))
                .Concat(GetImplicitVisualAttributes(stack)));
    }

    public static string? VisualGroupPageCodeForStack(ItemStack stack)
    {
        AssetLocation? groupCode = VisualGroupCodeForStack(stack);
        return groupCode == null ? null : $"{ItemClassMethods.Name(stack.Class)}-{groupCode.ToShortString()}";
    }

    public static string ResolveHandbookGroupWildcard(string wildcard, ItemStack stack)
    {
        if (stack?.Collectible == null || string.IsNullOrWhiteSpace(wildcard)) return wildcard;

        foreach ((string key, string value) in stack.Collectible.Variant)
        {
            wildcard = wildcard.Replace($"{{{key}}}", value, StringComparison.Ordinal);
        }

        foreach (string key in GetPlaceholders(wildcard))
        {
            string? value = stack.Attributes?.GetAsString(key);
            if (!string.IsNullOrWhiteSpace(value))
            {
                wildcard = wildcard.Replace($"{{{key}}}", value, StringComparison.Ordinal);
            }
        }

        return wildcard;
    }

    public static string NormalizeHandbookGroupWildcard(string wildcard, ItemStack stack)
    {
        return NarrowLeadingWildcard(ResolveHandbookGroupWildcard(wildcard, stack), stack);
    }

    public static bool MatchesHandbookGroup(ItemStack groupStack, ItemStack candidate)
    {
        if (groupStack?.Collectible?.Code == null || candidate?.Collectible?.Code == null) return false;

        foreach (string group in GetHandbookGroupBy(groupStack.Collectible))
        {
            string wildcard = NormalizeHandbookGroupWildcard(group, groupStack);
            AssetLocation wildcardCode = wildcard.Contains(':')
                ? new AssetLocation(wildcard)
                : new AssetLocation(groupStack.Collectible.Code.Domain, wildcard);

            AssetLocation candidateCode =
                candidate.Collectible.GetCollectibleInterface<IHandbookGrouping>()?.GetCodeForHandbookGrouping(candidate)
                ?? candidate.Collectible.Code;

            if (WildcardUtil.Match(wildcardCode, candidateCode))
            {
                return true;
            }
        }

        return false;
    }

    public static bool SatisfiesIgnoringAttributes(IWorldAccessor? world, ItemStack? thisStack, ItemStack? otherStack, IEnumerable<string> attributesToIgnore)
    {
        string[] attributes = Normalize(attributesToIgnore);
        if (attributes.Length == 0 || world == null || thisStack == null || otherStack == null) return false;

        if (thisStack.Class != otherStack.Class || thisStack.Id != otherStack.Id) return false;

        ITreeAttribute thisAttributes = thisStack.Attributes.Clone();
        ITreeAttribute otherAttributes = otherStack.Attributes.Clone();

        RemoveAttributes(thisAttributes, attributes);
        RemoveAttributes(otherAttributes, attributes);

        return thisAttributes.IsSubSetOf(world, otherAttributes);
    }

    private static void RemoveAttributes(ITreeAttribute attributes, IEnumerable<string> attributesToRemove)
    {
        foreach (string attribute in attributesToRemove)
        {
            attributes.RemoveAttribute(attribute);
        }
    }

    private static string[] Normalize(IEnumerable<string> attributes)
    {
        return attributes
            .Where(attribute => !string.IsNullOrWhiteSpace(attribute))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }

    private static string[] GetConfiguredVisualAttributes(CollectibleObject collectible)
    {
        return collectible.Attributes?["handbook"]?["visualAttributes"].AsArray<string>() ?? Array.Empty<string>();
    }

    private static IEnumerable<string> GetImplicitVisualAttributes(ItemStack stack)
    {
        if (IsQuiversVisualStack(stack))
        {
            yield return "types";
        }
    }

    private static AssetLocation? VisualGroupCodeForStack(ItemStack stack)
    {
        if (stack?.Collectible?.Code == null) return null;

        foreach (string group in GetConfiguredVisualGroupBy(stack.Collectible))
        {
            string wildcard = NormalizeHandbookGroupWildcard(group, stack);
            if (string.IsNullOrWhiteSpace(wildcard)) continue;

            return wildcard.Contains(':')
                ? new AssetLocation(wildcard)
                : new AssetLocation(stack.Collectible.Code.Domain, wildcard);
        }

        if (IsTailoredGambeson(stack))
        {
            string bodyPart = stack.Collectible.Variant["bodypart"];
            return new AssetLocation(stack.Collectible.Code.Domain, $"armor-{bodyPart}-tailored-*-linen");
        }

        return null;
    }

    private static string[] GetConfiguredVisualGroupBy(CollectibleObject collectible)
    {
        return collectible.Attributes?["handbook"]?["visualGroupBy"].AsArray<string>() ?? Array.Empty<string>();
    }

    private static bool IsQuiversVisualStack(ItemStack stack)
    {
        return stack.Collectible?.Code?.Domain == "quiversandsheaths" && GetHandbookGroupBy(stack.Collectible).Length > 0;
    }

    private static bool IsTailoredGambeson(ItemStack stack)
    {
        if (stack.Collectible?.Code?.Domain != "game") return false;
        if (!stack.Collectible.Code.Path.StartsWith("armor-", StringComparison.Ordinal)) return false;

        var variant = stack.Collectible.Variant;
        return variant.TryGetValue("bodypart", out string? bodyPart)
            && !string.IsNullOrWhiteSpace(bodyPart)
            && variant.TryGetValue("construction", out string? construction)
            && construction.StartsWith("tailored-", StringComparison.Ordinal)
            && variant.TryGetValue("material", out string? material)
            && material == "linen";
    }

    private static string NarrowLeadingWildcard(string wildcard, ItemStack stack)
    {
        if (!wildcard.StartsWith('*') || stack?.Collectible?.Code == null) return wildcard;

        int nextWildcard = wildcard.IndexOf('*', 1);
        string literal = nextWildcard >= 0 ? wildcard[1..nextWildcard] : wildcard[1..];
        if (string.IsNullOrEmpty(literal)) return wildcard;

        string path = stack.Collectible.Code.Path;
        int literalIndex = path.IndexOf(literal, StringComparison.Ordinal);
        if (literalIndex <= 0) return wildcard;

        return path[..literalIndex] + wildcard[1..];
    }

    private static IEnumerable<string> GetPlaceholders(string text)
    {
        int index = 0;
        while (index < text.Length)
        {
            int start = text.IndexOf('{', index);
            if (start < 0) yield break;

            int end = text.IndexOf('}', start + 1);
            if (end < 0) yield break;

            if (end > start + 1)
            {
                yield return text[(start + 1)..end];
            }

            index = end + 1;
        }
    }
}
