using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace CombatOverhaul.Utils;

public static class CollectibleClassifier
{
    public static bool IsDagger(ItemSlot? slot) => IsDagger(slot?.Itemstack);

    public static bool IsDagger(ItemStack? stack)
    {
        if (HasClassification(stack, "isDagger", "dagger")) return true;

        return IsDaggerCode(stack?.Collectible?.Code);
    }

    public static bool IsDaggerCode(AssetLocation? code) => ContainsCodePart(code, "dagger");

    public static bool IsTongs(ItemStack? stack)
    {
        if (HasClassification(stack, "isTongs", "tongs")) return true;

        string path = stack?.Collectible?.Code?.Path ?? "";
        return path.StartsWith("tongs", StringComparison.OrdinalIgnoreCase)
            || path.Contains("tongsmetal", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsFirearm(CollectibleObject? collectible)
    {
        if (collectible == null) return false;
        if (HasClassification(null, collectible, "isFirearm", "firearm")) return true;

        Type collectibleType = collectible.GetType();
        if (collectibleType.FullName?.StartsWith("Firearms.", StringComparison.Ordinal) == true)
        {
            return true;
        }

        string? assemblyName = collectibleType.Assembly.GetName().Name;
        return assemblyName?.Contains("Firearms", StringComparison.OrdinalIgnoreCase) == true;
    }

    public static bool IsVanillaItemShield(Item? item)
    {
        return item?.GetType().FullName == "Vintagestory.GameContent.ItemShield";
    }

    private static bool HasClassification(ItemStack? stack, string attributeName, string tag)
    {
        return HasClassification(stack, stack?.Collectible, attributeName, tag);
    }

    private static bool HasClassification(ItemStack? stack, CollectibleObject? collectible, string attributeName, string tag)
    {
        string qualifiedAttribute = $"combatoverhaul:{attributeName}";
        return AttributeTrue(stack?.ItemAttributes, qualifiedAttribute, attributeName)
            || AttributeTrue(collectible?.Attributes, qualifiedAttribute, attributeName)
            || HasTag(stack?.Item, tag)
            || HasTag(collectible, tag);
    }

    private static bool AttributeTrue(JsonObject? attributes, string qualifiedName, string shortName)
    {
        return attributes?[qualifiedName].AsBool(false) == true
            || attributes?[shortName].AsBool(false) == true
            || attributes?["combatoverhaul"]?[shortName].AsBool(false) == true;
    }

    private static bool HasTag(CollectibleObject? collectible, string tag)
    {
        if (collectible is not Item item) return false;

        return HasTag(item, tag);
    }

    private static bool HasTag(Item? item, string tag)
    {
        object? tagsObject = item?.Tags;
        if (tagsObject is not System.Collections.IEnumerable tags) return false;

        foreach (object? tagObject in tags)
        {
            string tagText = tagObject?.ToString() ?? "";
            if (tagText.Equals(tag, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }

    private static bool ContainsCodePart(AssetLocation? code, string part)
    {
        if (code == null) return false;

        return code.Path.Contains(part, StringComparison.OrdinalIgnoreCase)
            || code.Domain.Contains(part, StringComparison.OrdinalIgnoreCase);
    }
}
