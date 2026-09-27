using CombatOverhaul.Implementations;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.API.Util;
using Vintagestory.GameContent;

namespace CombatOverhaul.DamageSystems;

internal static class ServerDamageBlockProfiles
{
    internal static bool TryResolve(IServerPlayer player, DamageBlockPacket requested, out DamageBlockPacket authorized)
    {
        authorized = null!;

        // JSON patch merges can leave more than four configured values; DirectionConstrain uses
        // the first four, but the complete array still has to match the server's item profile.
        if (requested.Directions == null || requested.Directions.Length < 4 || requested.Directions.Length > 64) return false;
        if (!Enum.IsDefined(requested.Kind)) return false;

        foreach (DamageBlockPacket candidate in GetAllowedPackets(player, requested.MainHand))
        {
            if (!Matches(requested, candidate)) continue;

            candidate.Id = requested.Id;
            authorized = candidate;
            return true;
        }

        return false;
    }

    internal static IReadOnlyList<DamageBlockPacket> GetAllowedPackets(IServerPlayer player, bool mainHand)
    {
        ItemSlot slot = mainHand ? player.Entity.RightHandItemSlot : player.Entity.LeftHandItemSlot;
        ItemStack? stack = slot.Itemstack;
        if (stack?.Collectible?.Attributes == null) return [];
        if (stack.Collectible.GetCollectibleInterface<IHasServerBlockCallback>() == null) return [];

        try
        {
            IEnumerable<ConfiguredProfile> profiles = stack.Collectible is StanceBasedMeleeWeapon
                ? GetLegacyProfiles(player.Entity, stack, mainHand)
                : GetModernProfiles(player.Entity, stack, mainHand);

            List<DamageBlockPacket> packets = [];
            foreach (ConfiguredProfile profile in profiles)
            {
                DamageBlockPacket packet = profile.Block.ToPacket();
                packet.MainHand = mainHand;
                packet.Kind = profile.Kind;
                packets.Add(packet);
            }

            return packets;
        }
        catch
        {
            // A malformed or unsupported item definition must not make an untrusted packet valid.
            return [];
        }
    }

    internal static bool IsStillAuthorized(IServerPlayer player, DamageBlockStats current)
    {
        foreach (DamageBlockPacket candidate in GetAllowedPackets(player, current.MainHand))
        {
            if (current.ZoneType != (PlayerBodyPart)candidate.Zones) continue;
            if (current.Kind != candidate.Kind) continue;
            if (!DirectionsEffectivelyEqual(current.Directions.ToArray(), candidate.Directions)) continue;
            if (!string.Equals(current.Sound, candidate.Sound, StringComparison.Ordinal)) continue;
            if (!TiersEqual(current.BlockTier, candidate.BlockTier)) continue;
            if (current.CanBlockProjectiles != candidate.CanBlockProjectiles) continue;
            if ((int)current.StaggerTime.TotalMilliseconds != candidate.StaggerTimeMs) continue;
            if (current.StaggerTier != candidate.StaggerTier) continue;
            return true;
        }

        return false;
    }

    private static IEnumerable<ConfiguredProfile> GetModernProfiles(EntityPlayer player, ItemStack stack, bool mainHand)
    {
        IEnumerable<MeleeWeaponStats> modes = stack.Collectible.Attributes.KeyExists("Modes")
            ? stack.Collectible.Attributes.AsObject<MeleeWeaponModeCollectionStats>().Modes.Values
            : [stack.Collectible.Attributes.AsObject<MeleeWeaponModeStats>()];

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(stack);
        bool canParryProjectiles = HasProjectileParryTrait(player);

        foreach (MeleeWeaponStats mode in modes)
        {
            StanceStats? stance = GetModernStance(mode, player, mainHand);
            if (stance == null) continue;

            if (stance.CanBlock && stance.Block != null)
            {
                yield return new(ApplyTierBonus(stance.Block, stackStats.BlockTierBonus), EnumDamageBlockKind.Block);
            }

            if (stance.CanParry && stance.Parry != null)
            {
                DamageBlockJson parry = ApplyTierBonus(stance.Parry, stackStats.ParryTierBonus);
                if (parry.BlockTier != null && canParryProjectiles)
                {
                    parry.CanBlockProjectiles = true;
                }

                yield return new(parry, EnumDamageBlockKind.Parry);
            }
        }
    }

    private static StanceStats? GetModernStance(MeleeWeaponStats stats, EntityPlayer player, bool mainHand)
    {
        ItemSlot otherSlot = mainHand ? player.LeftHandItemSlot : player.RightHandItemSlot;
        string otherCode = otherSlot.Itemstack?.Collectible?.Code?.ToString() ?? string.Empty;

        if (!mainHand)
        {
            return GetMatchingDualWieldStance(stats.OffHandDualWieldStances, otherCode) ?? stats.OffHandStance;
        }

        if (otherSlot.Empty && stats.TwoHandedStance != null)
        {
            return stats.TwoHandedStance;
        }

        return GetMatchingDualWieldStance(stats.MainHandDualWieldStances, otherCode) ?? stats.OneHandedStance;
    }

    private static StanceStats? GetMatchingDualWieldStance(Dictionary<string, StanceStats> stances, string otherCode)
    {
        if (otherCode.Length == 0) return null;

        foreach ((string wildcard, StanceStats stance) in stances)
        {
            if (WildcardUtil.Match(wildcard, otherCode)) return stance;
        }

        return null;
    }

    private static IEnumerable<ConfiguredProfile> GetLegacyProfiles(EntityPlayer player, ItemStack stack, bool mainHand)
    {
        StanceBasedMeleeWeaponStats stats = stack.Collectible.Attributes.AsObject<StanceBasedMeleeWeaponStats>();
        StanceBasedMeleeWeaponGripStats? grip = mainHand
            ? (player.LeftHandItemSlot.Empty ? stats.TwoHanded ?? stats.OneHanded : stats.OneHanded)
            : stats.OffHand;

        if (grip == null) yield break;

        if (grip.DefaultBlock != null) yield return new(grip.DefaultBlock.Clone(), EnumDamageBlockKind.Block);
        foreach (DamageBlockJson block in grip.BlockByStance.Values)
        {
            if (block != null) yield return new(block.Clone(), EnumDamageBlockKind.Block);
        }

        foreach (DamageBlockJson parry in GetLegacyParries(grip))
        {
            yield return new(parry.Clone(), EnumDamageBlockKind.Parry);
        }
    }

    private static IEnumerable<DamageBlockJson> GetLegacyParries(StanceBasedMeleeWeaponGripStats grip)
    {
        if (grip.DefaultLeftClickAttack?.Parry != null) yield return grip.DefaultLeftClickAttack.Parry;
        if (grip.DefaultRightClickAttack?.Parry != null) yield return grip.DefaultRightClickAttack.Parry;

        foreach (StanceBasedMeleeWeaponAttackStats attack in grip.StanceToStanceLeftClickAttacks.Values)
        {
            if (attack.Parry != null) yield return attack.Parry;
        }

        foreach (StanceBasedMeleeWeaponAttackStats attack in grip.StanceToStanceRightClickAttacks.Values)
        {
            if (attack.Parry != null) yield return attack.Parry;
        }
    }

    private static DamageBlockJson ApplyTierBonus(DamageBlockJson source, int bonus)
    {
        DamageBlockJson result = source.Clone();
        if (result.BlockTier == null) return result;

        foreach (string damageType in result.BlockTier.Keys.ToArray())
        {
            result.BlockTier[damageType] += bonus;
        }

        return result;
    }

    private static bool HasProjectileParryTrait(EntityPlayer player)
    {
        try
        {
            return player.Api.ModLoader.GetModSystem<CharacterSystem>().HasTrait(player.Player, "canParryProjectiles");
        }
        catch
        {
            return false;
        }
    }

    private static bool Matches(DamageBlockPacket requested, DamageBlockPacket authorized)
    {
        if (requested.MainHand != authorized.MainHand) return false;
        if (requested.Kind != EnumDamageBlockKind.Unknown && requested.Kind != authorized.Kind) return false;
        if (requested.Zones != authorized.Zones) return false;
        if (!DirectionsEqual(requested.Directions, authorized.Directions)) return false;
        if (!string.Equals(requested.Sound, authorized.Sound, StringComparison.Ordinal)) return false;
        if (!TiersEqual(requested.BlockTier, authorized.BlockTier)) return false;
        if (requested.CanBlockProjectiles != authorized.CanBlockProjectiles) return false;
        if (requested.StaggerTimeMs != authorized.StaggerTimeMs) return false;
        if (requested.StaggerTier != authorized.StaggerTier) return false;
        return true;
    }

    private static bool DirectionsEqual(float[] requested, float[] authorized)
    {
        if (requested.Length != authorized.Length) return false;
        for (int index = 0; index < requested.Length; index++)
        {
            if (!float.IsFinite(requested[index])) return false;
            if (Math.Abs(requested[index] - authorized[index]) > 0.001f) return false;
        }

        return true;
    }

    private static bool DirectionsEffectivelyEqual(float[] current, float[] authorized)
    {
        if (authorized.Length < current.Length) return false;
        for (int index = 0; index < current.Length; index++)
        {
            if (!float.IsFinite(authorized[index])) return false;
            if (Math.Abs(current[index] - authorized[index]) > 0.001f) return false;
        }

        return true;
    }

    private static bool TiersEqual(Dictionary<EnumDamageType, int>? requested, Dictionary<EnumDamageType, int>? authorized)
    {
        if (requested == null || authorized == null) return requested == null && authorized == null;
        if (requested.Count != authorized.Count) return false;

        foreach ((EnumDamageType damageType, int tier) in authorized)
        {
            if (!requested.TryGetValue(damageType, out int requestedTier) || requestedTier != tier) return false;
        }

        return true;
    }

    private readonly record struct ConfiguredProfile(DamageBlockJson Block, EnumDamageBlockKind Kind);
}
