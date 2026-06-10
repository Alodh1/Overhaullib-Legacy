using CombatOverhaul.DamageSystems;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.Armor;

internal static class ArmorFootStepSoundUtil
{
    public static AssetLocation[] GetFootStepSounds(ICoreAPI? api, ItemSlot slot, CollectibleObject collectible, CollectibleBehaviorWearable? wearable)
    {
        AssetLocation[] sounds = ResolveAvailableFootStepSounds(api, slot, collectible, wearable);
        if (sounds.Length == 0) return Array.Empty<AssetLocation>();

        return IsSelectedFootStepArmor(api, slot) ? sounds : Array.Empty<AssetLocation>();
    }

    private static bool IsSelectedFootStepArmor(ICoreAPI? api, ItemSlot slot)
    {
        if (slot.Inventory == null || slot.Itemstack == null) return true;

        (int soundPriority, int zonePriority, int slotId) current = GetFootStepArmorPriority(api, slot);
        if (current.soundPriority <= 0) return true;

        foreach (ItemSlot candidate in slot.Inventory)
        {
            if (ReferenceEquals(candidate, slot) || candidate.Empty) continue;

            (int soundPriority, int zonePriority, int slotId) other = GetFootStepArmorPriority(api, candidate);
            if (other.soundPriority <= 0) continue;

            if (other.soundPriority > current.soundPriority) return false;
            if (other.soundPriority < current.soundPriority) continue;

            if (other.zonePriority > current.zonePriority) return false;
            if (other.zonePriority < current.zonePriority) continue;

            if (other.slotId >= 0 && (current.slotId < 0 || other.slotId < current.slotId)) return false;
        }

        return true;
    }

    private static (int soundPriority, int zonePriority, int slotId) GetFootStepArmorPriority(ICoreAPI? api, ItemSlot slot)
    {
        CollectibleObject? collectible = slot.Itemstack?.Collectible;
        if (collectible == null) return (0, 0, -1);

        if (collectible.GetCollectibleBehavior<ArmorBehavior>(true) == null) return (0, 0, -1);

        CollectibleBehaviorWearable? wearable = collectible.GetBehavior<CollectibleBehaviorWearable>();
        AssetLocation[] sounds = ResolveAvailableFootStepSounds(api, slot, collectible, wearable);
        if (sounds.Length == 0) return (0, 0, -1);

        int slotId = slot.Inventory?.GetSlotId(slot) ?? -1;
        return (GetSoundPriority(collectible, sounds), GetZonePriority(collectible), slotId);
    }

    private static AssetLocation[] ResolveAvailableFootStepSounds(ICoreAPI? api, ItemSlot slot, CollectibleObject collectible, CollectibleBehaviorWearable? wearable)
    {
        AssetLocation[]? sounds = wearable?.GetFootStepSounds(slot);
        if (sounds is { Length: > 0 }) return sounds;

        sounds = ResolveConfiguredFootStepSounds(api, slot, collectible);
        if (sounds.Length > 0) return sounds;

        return InferFootStepSounds(api, collectible);
    }

    private static AssetLocation[] ResolveConfiguredFootStepSounds(ICoreAPI? api, ItemSlot slot, CollectibleObject collectible)
    {
        string? soundCode = slot.Itemstack?.ItemAttributes?["footStepSound"].AsString(null);
        soundCode ??= collectible.Attributes?["footStepSound"].AsString(null);

        if (string.IsNullOrWhiteSpace(soundCode)) return Array.Empty<AssetLocation>();

        string defaultDomain = soundCode.Contains(':') || soundCode.StartsWith("wearable/", StringComparison.OrdinalIgnoreCase)
            ? "game"
            : collectible.Code.Domain;

        return ExpandSound(api, soundCode, defaultDomain);
    }

    private static AssetLocation[] InferFootStepSounds(ICoreAPI? api, CollectibleObject collectible)
    {
        string code = collectible.Code?.Path?.ToLowerInvariant() ?? "";

        if (code.Contains("plate")) return ExpandSound(api, "wearable/plate*", "game");
        if (code.Contains("scale")) return ExpandSound(api, "wearable/scale*", "game");
        if (code.Contains("chain")) return ExpandSound(api, "wearable/chain*", "game");
        if (code.Contains("brigandine")) return ExpandSound(api, "wearable/brigandine*", "game");
        if (code.Contains("lamellar-wood")) return ExpandSound(api, "wearable/leather*", "game");
        if (code.Contains("lamellar")) return ExpandSound(api, "wearable/brigandine*", "game");
        if (code.Contains("leather") || code.Contains("jerkin") || code.Contains("hide") || code.Contains("sewn")) return ExpandSound(api, "wearable/leather*", "game");

        return Array.Empty<AssetLocation>();
    }

    private static AssetLocation[] ExpandSound(ICoreAPI? api, string soundCode, string defaultDomain)
    {
        AssetLocation soundLocation = AssetLocation.Create(soundCode, defaultDomain).WithPathPrefixOnce("sounds/");

        if (soundCode.EndsWith('*') && api != null)
        {
            soundLocation.Path = soundLocation.Path.TrimEnd('*');
            return api.Assets.GetLocations(soundLocation.Path, soundLocation.Domain).ToArray();
        }

        return [soundLocation];
    }

    private static int GetSoundPriority(CollectibleObject collectible, AssetLocation[] sounds)
    {
        string code = collectible.Code?.Path?.ToLowerInvariant() ?? "";
        string sound = string.Join(' ', sounds.Select(sound => sound.Path.ToLowerInvariant()));
        string value = $"{code} {sound}";

        if (value.Contains("plate")) return 60;
        if (value.Contains("scale")) return 50;
        if (value.Contains("chain")) return 45;
        if (value.Contains("brigandine")) return 40;
        if (value.Contains("lamellar")) return 30;
        if (value.Contains("leather") || value.Contains("jerkin") || value.Contains("hide") || value.Contains("sewn")) return 20;

        return 10;
    }

    private static int GetZonePriority(CollectibleObject collectible)
    {
        ArmorType armorType = collectible.GetCollectibleBehavior<ArmorBehavior>(true)?.ArmorType ?? ArmorType.Empty;

        if ((armorType.Slots & DamageZone.Torso) != 0) return 700;
        if ((armorType.Slots & DamageZone.Legs) != 0) return 600;
        if ((armorType.Slots & DamageZone.Feet) != 0) return 500;
        if ((armorType.Slots & DamageZone.Arms) != 0) return 400;
        if ((armorType.Slots & DamageZone.Hands) != 0) return 300;
        if ((armorType.Slots & DamageZone.Head) != 0) return 200;
        if ((armorType.Slots & DamageZone.Neck) != 0) return 100;
        if ((armorType.Slots & DamageZone.Face) != 0) return 90;

        return 0;
    }
}
