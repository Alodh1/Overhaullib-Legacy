using CombatOverhaul.Implementations;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.GameContent;

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

    public static bool IsBow(ItemStack? stack) => IsBow(stack, stack?.Collectible);

    public static bool IsBow(CollectibleObject? collectible) => IsBow(null, collectible);

    public static bool IsCrossbow(ItemStack? stack) => IsCrossbow(stack, stack?.Collectible);

    public static bool IsCrossbow(CollectibleObject? collectible) => IsCrossbow(null, collectible);

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

    public static bool IsShield(ItemStack? stack) => IsShield(stack, stack?.Collectible);

    public static bool IsShield(CollectibleObject? collectible) => IsShield(null, collectible);

    private static bool IsShield(ItemStack? stack, CollectibleObject? collectible)
    {
        if (collectible == null) return false;
        if (HasClassification(stack, collectible, "isShield", "shield")) return true;
        if (collectible is ItemShield) return true;
        if (collectible.Tool == EnumTool.Shield) return true;

        return IsShieldCode(collectible.Code);
    }

    private static bool IsBow(ItemStack? stack, CollectibleObject? collectible)
    {
        if (collectible == null) return false;
        if (IsFirearm(collectible) || IsCrossbow(stack, collectible)) return false;
        if (HasClassification(stack, collectible, "isBow", "bow")) return true;

        Type collectibleType = collectible.GetType();
        string typeName = collectibleType.FullName ?? "";
        if (typeName.EndsWith(".BowItem", StringComparison.Ordinal) || typeName.EndsWith(".ItemBow", StringComparison.Ordinal))
        {
            return true;
        }

        return IsBowCode(collectible.Code);
    }

    private static bool IsCrossbow(ItemStack? stack, CollectibleObject? collectible)
    {
        if (collectible == null) return false;
        if (HasClassification(stack, collectible, "isCrossbow", "crossbow")) return true;

        Type collectibleType = collectible.GetType();
        string typeName = collectibleType.FullName ?? "";
        if (typeName.Contains("Crossbow", StringComparison.Ordinal))
        {
            return true;
        }

        string? assemblyName = collectibleType.Assembly.GetName().Name;
        if (assemblyName?.Contains("Crossbows", StringComparison.OrdinalIgnoreCase) == true && IsCrossbowCode(collectible.Code))
        {
            return true;
        }

        return IsCrossbowCode(collectible.Code);
    }

    private static bool IsBowCode(AssetLocation? code)
    {
        string path = code?.Path ?? "";
        return path.Equals("bow", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("bow-", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsCrossbowCode(AssetLocation? code)
    {
        string path = code?.Path ?? "";
        return path.Equals("crossbow", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("crossbow-", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsVanillaItemShield(Item? item) => IsUnpatchedVanillaItemShield(item);

    public static bool IsUnpatchedVanillaItemShield(Item? item)
    {
        if (item?.GetType().FullName != "Vintagestory.GameContent.ItemShield") return false;

        return item.GetCollectibleInterface<IHasMeleeWeaponActions>() == null;
    }

    public static bool HasMeleeWeaponActions(CollectibleObject? collectible)
    {
        return collectible?.GetCollectibleInterface<IHasMeleeWeaponActions>() != null;
    }

    private static bool IsShieldCode(AssetLocation? code)
    {
        string path = code?.Path ?? "";
        return path.Equals("shield", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("shield-", StringComparison.OrdinalIgnoreCase)
            || path.Equals("roundshield", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("roundshield-", StringComparison.OrdinalIgnoreCase);
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
