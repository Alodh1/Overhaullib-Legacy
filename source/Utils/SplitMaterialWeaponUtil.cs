using System.Globalization;
using System.Text;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace CombatOverhaul.Utils;

public readonly struct SplitMaterialInfo
{
    public SplitMaterialInfo(string bodyMaterial, string bladeMaterial)
    {
        BodyMaterial = bodyMaterial;
        BladeMaterial = bladeMaterial;
    }

    public string BodyMaterial { get; }
    public string BladeMaterial { get; }
    public bool IsMixed => !BodyMaterial.Equals(BladeMaterial, StringComparison.OrdinalIgnoreCase);
}

public static class SplitMaterialWeaponUtil
{
    private const string ToolsmithToolHeadAttribute = "tinkeredToolHead";
    private const float SpringSteelBladeDurabilityMultiplier = 1.2f;
    private static IWorldAccessor? _world;

    private static readonly Dictionary<string, int> MaterialTiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["copper"] = 1,
        ["tinbronze"] = 2,
        ["bismuthbronze"] = 2,
        ["blackbronze"] = 2,
        ["iron"] = 4,
        ["meteoriciron"] = 5,
        ["steel"] = 6,
        ["stahl"] = 6,
        ["meteoricsteel"] = 7,
        ["wootz"] = 7,
        ["springsteel"] = 7
    };

    private static readonly Dictionary<string, float> DamageMultipliers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["meteoriciron"] = 0.98f,
        ["springsteel"] = 1.05f,
        ["wootz"] = 1.05f
    };

    private static readonly Dictionary<string, string> MaterialFallbackNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["copper"] = "Copper",
        ["tinbronze"] = "Tin bronze",
        ["bismuthbronze"] = "Bismuth bronze",
        ["blackbronze"] = "Black bronze",
        ["iron"] = "Iron",
        ["meteoriciron"] = "Meteoric iron",
        ["steel"] = "Steel",
        ["stahl"] = "Stahl",
        ["meteoricsteel"] = "Meteoric steel",
        ["wootz"] = "Wootz",
        ["springsteel"] = "Spring steel"
    };

    public static void SetApi(ICoreAPI api)
    {
        _world = api.World;
    }

    public static bool Normalize(ItemStack? stack)
    {
        if (stack?.Collectible == null || !TryGetSplitMaterials(stack, out SplitMaterialInfo materials))
        {
            return false;
        }

        stack.Attributes ??= new TreeAttribute();

        bool changed = false;
        changed |= SetString(stack.Attributes, "splitBodyMaterial", materials.BodyMaterial);
        changed |= SetString(stack.Attributes, "splitBladeMaterial", materials.BladeMaterial);
        changed |= SetString(stack.Attributes, "headMaterial", materials.BodyMaterial);
        changed |= SetString(stack.Attributes, "bladeMaterial", materials.BladeMaterial);
        changed |= SetString(stack.Attributes, "bodyTexture", MaterialTexture(materials.BodyMaterial));
        changed |= SetString(stack.Attributes, "bladeTexture", MaterialTexture(materials.BladeMaterial));
        changed |= SetString(stack.Attributes, "headTexture", MaterialTexture(materials.BladeMaterial));

        if (TryGetMaterialTier(materials.BladeMaterial, out int bladeTier))
        {
            string baseMaterial = GetPrimaryMaterial(stack) ?? materials.BodyMaterial;
            if (TryGetMaterialTier(baseMaterial, out int baseTier))
            {
                changed |= SetInt(stack.Attributes, "damageTierBonus", bladeTier - baseTier);
                changed |= SetInt(stack.Attributes, "thrownDamageTierBonus", bladeTier - baseTier);
            }
        }

        changed |= SetFloat(stack.Attributes, "damageMultiplier", GetDamageMultiplier(materials.BladeMaterial));
        changed |= SetFloat(stack.Attributes, "thrownDamageMultiplier", GetDamageMultiplier(materials.BladeMaterial));

        return changed;
    }

    public static bool TryGetSplitMaterials(ItemStack? stack, out SplitMaterialInfo materials)
    {
        materials = default;
        if (stack?.Collectible == null)
        {
            return false;
        }

        if (TryReadMaterialsFromAttributes(stack, out materials))
        {
            return true;
        }

        if (TryReadMaterialsFromToolsmithHead(stack, out materials))
        {
            return true;
        }

        if (TryReadMaterialsFromVariants(stack, out materials))
        {
            return true;
        }

        return false;
    }

    public static string GetHeldItemName(ItemStack? stack, string baseName)
    {
        Normalize(stack);

        if (!TryGetSplitMaterials(stack, out SplitMaterialInfo materials) || !materials.IsMixed)
        {
            return baseName;
        }

        string cleanName = StripTrailingMaterialSuffix(baseName);
        return Lang.Get(
            "combatoverhaul:split-material-item-name",
            cleanName,
            MaterialName(materials.BladeMaterial),
            MaterialName(materials.BodyMaterial));
    }

    public static void AppendTooltip(ItemStack? stack, StringBuilder dsc)
    {
        Normalize(stack);

        if (!TryGetSplitMaterials(stack, out SplitMaterialInfo materials) || !materials.IsMixed)
        {
            return;
        }

        AppendLineOnce(dsc, Lang.Get(
            "combatoverhaul:split-material-description",
            MaterialName(materials.BladeMaterial),
            MaterialName(materials.BodyMaterial)));
    }

    public static bool TryGetBodyMaxDurability(ItemStack? stack, int currentMaxDurability, out int maxDurability)
    {
        maxDurability = currentMaxDurability;
        if (currentMaxDurability <= 0
            || stack?.Collectible?.Attributes == null
            || !TryGetSplitMaterials(stack, out SplitMaterialInfo materials)
            || !TryGetBodyMaterialMaxDurability(stack, materials, out int bodyMaxDurability))
        {
            return false;
        }

        maxDurability = GetBladeDurabilityAdjustedValue(materials, bodyMaxDurability);
        return maxDurability != currentMaxDurability;
    }

    private static bool TryGetBodyMaterialMaxDurability(ItemStack stack, SplitMaterialInfo materials, out int maxDurability)
    {
        maxDurability = 0;
        Dictionary<string, int>? durabilityByType = ReadDurabilityByType(stack.Collectible);
        if (durabilityByType == null || durabilityByType.Count == 0)
        {
            return false;
        }

        string bodyPath = BuildBodyMaterialPath(stack.Collectible.Code.Path, materials);
        string bodyCode = $"{stack.Collectible.Code.Domain}:{bodyPath}";

        foreach ((string pattern, int durability) in durabilityByType)
        {
            if (WildcardUtil.Match(pattern, bodyPath) || WildcardUtil.Match(pattern, bodyCode))
            {
                maxDurability = durability;
                return true;
            }
        }

        return false;
    }

    private static int GetBladeDurabilityAdjustedValue(SplitMaterialInfo materials, int bodyMaxDurability)
    {
        if (!materials.BladeMaterial.Equals("springsteel", StringComparison.OrdinalIgnoreCase))
        {
            return bodyMaxDurability;
        }

        return Math.Max(1, (int)MathF.Round(bodyMaxDurability * SpringSteelBladeDurabilityMultiplier));
    }

    public static void NormalizeDurabilityIfNeeded(ItemStack? stack)
    {
        if (stack?.Collectible == null || stack.Attributes == null || !stack.Attributes.HasAttribute("durability"))
        {
            return;
        }

        int maxDurability = stack.Collectible.GetMaxDurability(stack);
        if (maxDurability <= 0)
        {
            return;
        }

        int currentDurability = stack.Collectible.GetRemainingDurability(stack);
        if (currentDurability <= 0 || currentDurability > maxDurability)
        {
            stack.Attributes.RemoveAttribute("durability");
        }
    }

    private static bool TryReadMaterialsFromAttributes(ItemStack stack, out SplitMaterialInfo materials)
    {
        materials = default;
        if (stack.Attributes == null)
        {
            return false;
        }

        string? body = ReadString(stack.Attributes, "splitBodyMaterial", "headMaterial", "bodyMaterial", "material");
        string? blade = ReadString(stack.Attributes, "splitBladeMaterial", "bladeMaterial", "edgeMaterial", "edge");

        if (!IsKnownMaterial(body) || !IsKnownMaterial(blade))
        {
            return false;
        }

        materials = new(NormalizeMaterial(body), NormalizeMaterial(blade));
        return true;
    }

    private static bool TryReadMaterialsFromToolsmithHead(ItemStack stack, out SplitMaterialInfo materials)
    {
        materials = default;
        if (stack.Attributes == null || !stack.Attributes.HasAttribute(ToolsmithToolHeadAttribute))
        {
            return false;
        }

        ItemStack? head = stack.Attributes.GetItemstack(ToolsmithToolHeadAttribute, null);
        if (head == null)
        {
            return false;
        }

        if (TryReadMaterialsFromAttributes(head, out materials))
        {
            return true;
        }

        if (head.Collectible == null && _world != null)
        {
            head.ResolveBlockOrItem(_world);
        }

        if (head.Collectible == null)
        {
            return false;
        }

        if (TryReadMaterialsFromVariants(head, out materials))
        {
            return true;
        }

        return TryReadMaterialsFromCode(head.Collectible.Code.Path, out materials);
    }

    private static bool TryReadMaterialsFromVariants(ItemStack stack, out SplitMaterialInfo materials)
    {
        materials = default;
        if (stack.Collectible?.Variant == null)
        {
            return false;
        }

        string? body = ReadVariant(stack.Collectible, "material", "head", "body", "metal");
        string? blade = ReadVariant(stack.Collectible, "edge", "edge1", "blade");

        if (!IsKnownMaterial(body) || !IsKnownMaterial(blade))
        {
            return false;
        }

        materials = new(NormalizeMaterial(body), NormalizeMaterial(blade));
        return true;
    }

    private static bool TryReadMaterialsFromCode(string path, out SplitMaterialInfo materials)
    {
        materials = default;
        string[] materialTokens = path
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsKnownMaterial)
            .Select(NormalizeMaterial)
            .ToArray();

        if (materialTokens.Length < 2)
        {
            return false;
        }

        materials = new(materialTokens[0], materialTokens[^1]);
        return true;
    }

    private static Dictionary<string, int>? ReadDurabilityByType(CollectibleObject collectible)
    {
        JsonObject? durabilityByType = collectible.Attributes?["durabilitybytype"];
        if (durabilityByType?.Exists != true)
        {
            durabilityByType = collectible.Attributes?["durabilityByType"];
        }

        return durabilityByType?.Exists == true
            ? durabilityByType.AsObject<Dictionary<string, int>>()
            : null;
    }

    private static string BuildBodyMaterialPath(string path, SplitMaterialInfo materials)
    {
        string[] parts = path.Split('-', StringSplitOptions.None);
        bool replaced = false;

        for (int index = parts.Length - 1; index >= 0; index--)
        {
            if (!parts[index].Equals(materials.BladeMaterial, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            parts[index] = materials.BodyMaterial;
            replaced = true;
            break;
        }

        if (!replaced)
        {
            for (int index = parts.Length - 1; index >= 0; index--)
            {
                if (!IsKnownMaterial(parts[index]))
                {
                    continue;
                }

                parts[index] = materials.BodyMaterial;
                break;
            }
        }

        return string.Join("-", parts);
    }

    private static string? GetPrimaryMaterial(ItemStack stack)
    {
        CollectibleObject? collectible = stack.Collectible;
        if (collectible == null)
        {
            return null;
        }

        string? variantMaterial = ReadVariant(collectible, "edge", "edge1", "blade", "material", "head", "body", "metal");
        if (variantMaterial != null)
        {
            return NormalizeMaterial(variantMaterial);
        }

        string[] materialTokens = collectible.Code.Path
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(IsKnownMaterial)
            .Select(NormalizeMaterial)
            .ToArray();

        return materialTokens.Length > 0 ? materialTokens[^1] : null;
    }

    private static string? ReadVariant(CollectibleObject collectible, params string[] keys)
    {
        foreach (string key in keys)
        {
            if (collectible.Variant.TryGetValue(key, out string value) && IsKnownMaterial(value))
            {
                return value;
            }
        }

        return null;
    }

    private static string? ReadString(ITreeAttribute attributes, params string[] keys)
    {
        foreach (string key in keys)
        {
            string value = attributes.GetString(key, "");
            if (IsKnownMaterial(value))
            {
                return value;
            }
        }

        return null;
    }

    private static bool SetString(ITreeAttribute attributes, string key, string value)
    {
        if (attributes.GetString(key, "") == value)
        {
            return false;
        }

        attributes.SetString(key, value);
        return true;
    }

    private static bool SetInt(ITreeAttribute attributes, string key, int value)
    {
        if (attributes.GetInt(key, int.MinValue) == value)
        {
            return false;
        }

        attributes.SetInt(key, value);
        return true;
    }

    private static bool SetFloat(ITreeAttribute attributes, string key, float value)
    {
        if (Math.Abs(attributes.GetFloat(key, float.NaN) - value) < 0.0001f)
        {
            return false;
        }

        attributes.SetFloat(key, value);
        return true;
    }

    private static bool TryGetMaterialTier(string material, out int tier) => MaterialTiers.TryGetValue(material, out tier);

    private static float GetDamageMultiplier(string material) => DamageMultipliers.GetValueOrDefault(material, 1f);

    private static bool IsKnownMaterial(string? material) => !string.IsNullOrWhiteSpace(material) && MaterialTiers.ContainsKey(material);

    private static string NormalizeMaterial(string material) => material.ToLowerInvariant();

    private static string MaterialTexture(string material) => $"game:block/metal/ingot/{material}";

    private static string MaterialName(string material)
    {
        string key = $"material-{material}";
        string translated = Lang.Get(key);
        if (!translated.Equals(key, StringComparison.Ordinal))
        {
            return translated;
        }

        string domainKey = $"game:material-{material}";
        translated = Lang.Get(domainKey);
        if (!translated.Equals(domainKey, StringComparison.Ordinal))
        {
            return translated;
        }

        if (MaterialFallbackNames.TryGetValue(material, out string fallback))
        {
            return fallback;
        }

        return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(material.Replace("-", " "));
    }

    private static string StripTrailingMaterialSuffix(string name)
    {
        int suffixStart = name.LastIndexOf(" (", StringComparison.Ordinal);
        return suffixStart > 0 && name.EndsWith(")", StringComparison.Ordinal)
            ? name[..suffixStart]
            : name;
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
