using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Integration;
using CombatOverhaul.Utils;
using CombatOverhaul.WeaponBuffs;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace CombatOverhaul.MeleeSystems;

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeAttackPacket
{
    public MeleeDamagePacket[] MeleeAttackDamagePackets { get; set; }
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleePushPacket
{
    public MeleeCollisionPacket[] MeleeAttackDamagePackets { get; set; }
}

public enum MeleeAttackStatus
{
    Start,
    End
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public struct MeleeAttackStatusPacket
{
    public MeleeAttackStatus Status { get; set; }
    public bool MainHand { get; set; }
}

public abstract class MeleeSystem
{
    public const string NetworkChannelId = "CombatOverhaul:damage-packets";
}

public readonly struct AttackId
{
    public readonly int ItemId;
    public readonly int Id;

    public AttackId(int itemId, int id)
    {
        ItemId = itemId;
        Id = id;
    }
}

public readonly struct ServerMeleeAttackStats
{
    public readonly MeleeAttackStats? Attack;
    public readonly ItemStackMeleeWeaponStats StackStats;

    public ServerMeleeAttackStats(MeleeAttackStats? attack, ItemStackMeleeWeaponStats stackStats)
    {
        Attack = attack;
        StackStats = stackStats;
    }
}

public interface IHasServerMeleeAttacks
{
    IEnumerable<ServerMeleeAttackStats> GetServerMeleeAttacks(EntityPlayer attacker, Entity target, ItemSlot weaponSlot);
}

public sealed class MeleeDamageResolvedEventArgs
{
    public MeleeDamageResolvedEventArgs(Entity target, DamageSource damageSource, ItemSlot? slot, float damage, bool damageReceived, DamageBlockEventArgs? block)
    {
        Target = target;
        DamageSource = damageSource;
        Slot = slot;
        Damage = damage;
        DamageReceived = damageReceived;
        Block = block;
    }

    public Entity Target { get; }
    public DamageSource DamageSource { get; }
    public ItemSlot? Slot { get; }
    public float Damage { get; }
    public bool DamageReceived { get; }
    public DamageBlockEventArgs? Block { get; }
    public bool WasBlocked => Block != null;
    public bool WasParried => Block?.Kind == EnumDamageBlockKind.Parry;
    public bool IsLegalHit => DamageReceived && Block == null;
}

public sealed class MeleeSystemClient : MeleeSystem
{
    public delegate void MeleeAttackDelegate(Entity attacker, ItemSlot? slot);

    public event MeleeAttackDelegate? OnMeleeAttackStart;
    public event MeleeAttackDelegate? OnMeleeAttackEnd;

    public MeleeSystemClient(ICoreClientAPI api)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>()
            .RegisterMessageType<MeleePushPacket>()
            .RegisterMessageType<MeleeAttackStatusPacket>();
    }

    public void SendPackets(IEnumerable<MeleeDamagePacket> packets)
    {
        _clientChannel.SendPacket(new MeleeAttackPacket
        {
            MeleeAttackDamagePackets = packets as MeleeDamagePacket[] ?? packets.ToArray()
        });
    }

    public void SendPackets(IEnumerable<MeleeCollisionPacket> packets)
    {
        _clientChannel.SendPacket(new MeleePushPacket
        {
            MeleeAttackDamagePackets = packets as MeleeCollisionPacket[] ?? packets.ToArray()
        });
    }

    public void UpdateAttackStatus(EntityPlayer attacker, MeleeAttackStatus status, bool mainHand)
    {
        _clientChannel.SendPacket(new MeleeAttackStatusPacket
        {
            Status = status,
            MainHand = mainHand
        });

        ItemSlot weaponSlot = mainHand ? attacker.ActiveHandItemSlot : attacker.LeftHandItemSlot;
        switch (status)
        {
            case MeleeAttackStatus.Start:
                OnMeleeAttackStart?.Invoke(attacker, weaponSlot);
                break;
            case MeleeAttackStatus.End:
                OnMeleeAttackEnd?.Invoke(attacker, weaponSlot);
                break;
        }
    }

    private readonly IClientNetworkChannel _clientChannel;
}

public sealed class MeleeSystemServer : MeleeSystem
{
    public delegate void MeleeDamageDelegate(Entity target, DamageSource damageSource, ItemSlot? slot, ref float damage);
    public delegate void MeleeDamageResolvedDelegate(MeleeDamageResolvedEventArgs args);
    public delegate void MeleeAttackDelegate(Entity attacker, ItemSlot weaponSlot);

    public event MeleeDamageDelegate? OnDealMeleeDamage;
    public event MeleeDamageResolvedDelegate? OnMeleeDamageResolved;
    public event MeleeAttackDelegate? OnMeleeAttackStart;
    public event MeleeAttackDelegate? OnMeleeAttackEnd;

    public MeleeSystemServer(ICoreServerAPI api)
    {
        _api = api;
        api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<MeleeAttackPacket>()
            .RegisterMessageType<MeleePushPacket>()
            .RegisterMessageType<MeleeAttackStatusPacket>()
            .SetMessageHandler<MeleeAttackPacket>(HandlePacket)
            .SetMessageHandler<MeleePushPacket>(HandlePacket)
            .SetMessageHandler<MeleeAttackStatusPacket>(HandlePacket);
    }

    private readonly ICoreServerAPI _api;
    private const double _reachTolerance = 1.0;
    private const float _damageTolerance = 0.05f;
    private const float _damageRelativeTolerance = 0.20f;
    private const float _knockbackTolerance = 0.01f;
    private const int _maxRejectedAttackPacketLogs = 20;
    private const double TerrainObstructionRadius = 0.03;
    private const double TerrainObstructionStartPadding = 0.25;
    private const double TerrainObstructionEndPadding = 0.05;
    private int _rejectedAttackPacketLogs;

    private void HandlePacket(IServerPlayer player, MeleeAttackPacket packet)
    {
        if (packet.MeleeAttackDamagePackets == null) return;

        foreach (MeleeDamagePacket damagePacket in packet.MeleeAttackDamagePackets)
        {
            if (damagePacket == null) continue;

            Attack(player, damagePacket);
        }
    }

    private void HandlePacket(IServerPlayer player, MeleePushPacket packet)
    {
        foreach (MeleeCollisionPacket collisionPacket in packet.MeleeAttackDamagePackets)
        {
            Push(collisionPacket);
        }
    }

    private void HandlePacket(IServerPlayer player, MeleeAttackStatusPacket packet)
    {
        ItemSlot weaponSlot = packet.MainHand ? player.Entity.ActiveHandItemSlot : player.Entity.LeftHandItemSlot;
        switch (packet.Status)
        {
            case MeleeAttackStatus.Start:
                OnMeleeAttackStart?.Invoke(player.Entity, weaponSlot);
                break;
            case MeleeAttackStatus.End:
                OnMeleeAttackEnd?.Invoke(player.Entity, weaponSlot);
                break;
        }
    }

    private void Attack(IServerPlayer player, MeleeDamagePacket packet)
    {
        if (!TryValidateAttackPacket(player, packet, out Entity target, out ItemSlot? slot, out EnumDamageType damageType, out Vector3d position))
        {
            return;
        }

        Entity attacker = player.Entity;
        string targetName = target.GetName();

        if (damageType != EnumDamageType.Heal)
        {
            if (target is EntityPlayer && (!_api.Server.Config.AllowPvP || !player.HasPrivilege("attackplayers")))
            {
                return;
            }

            if (target is EntityAgent && !player.HasPrivilege("attackcreatures"))
            {
                return;
            }
        }

        DirectionalTypedDamageSource damageSource = new()
        {
            Source = attacker is EntityPlayer ? EnumDamageSource.Player : EnumDamageSource.Entity,
            SourceEntity = attacker,
            CauseEntity = attacker,
            DamageTypeData = new DamageData(damageType, packet.Tier, packet.ArmorPiercingTier),
            Position = position,
            Collider = packet.Collider,
            KnockbackStrength = packet.Knockback,
            DamageTier = packet.Tier,
            Type = damageType,
            Weapon = slot?.Itemstack,
            IgnoreInvFrames = true
        };

        bool damageReceived = DealDamage(target, damageSource, slot, packet.Damage);
        if (damageReceived)
        {
            WeaponBuffSystem.Current?.Consume(slot, WeaponBuffConsumptionTrigger.MeleeHit);
        }

        _api.ModLoader.GetModSystem<CombatOverhaulSystem>().ServerImpaleSystem?.TryAttach(packet, attacker, target, slot, damageReceived);

        if (packet.StaggerTimeMs > 0)
        {
            target.GetBehavior<StaggerBehavior>()?.TriggerStagger(TimeSpan.FromMilliseconds(packet.StaggerTimeMs), packet.StaggerTier);
        }

        DealDurabilityDamage(slot, packet, attacker);

        PrintLog(attacker, damageReceived, target, packet, targetName);
    }

    private bool TryValidateAttackPacket(IServerPlayer player, MeleeDamagePacket packet, out Entity target, out ItemSlot? slot, out EnumDamageType damageType, out Vector3d position)
    {
        target = null!;
        slot = null;
        damageType = default;
        position = default;

        if (packet.AttackerEntityId != player.Entity.EntityId)
        {
            LogRejectedAttackPacket(player, packet, "wrong-attacker");
            return false;
        }

        Entity? packetTarget = _api.World.GetEntityById(packet.TargetEntityId);
        if (packetTarget == null || !packetTarget.Alive)
        {
            LogRejectedAttackPacket(player, packet, "invalid-target");
            return false;
        }

        if (!Enum.TryParse(packet.DamageType, out damageType))
        {
            LogRejectedAttackPacket(player, packet, "invalid-damage-type");
            return false;
        }

        if (!TryGetPacketPosition(packet, out position))
        {
            LogRejectedAttackPacket(player, packet, "invalid-position");
            return false;
        }

        slot = GetWeaponSlot(player.Entity, packet.MainHand);
        if (slot?.Itemstack == null)
        {
            LogRejectedAttackPacket(player, packet, "missing-weapon");
            return false;
        }

        if (!TryGetAttackLimits(player.Entity, packetTarget, slot, out MeleeAttackLimits limits))
        {
            LogRejectedAttackPacket(player, packet, "missing-weapon-stats");
            return false;
        }

        if (!IsTargetWithinReach(player.Entity, packetTarget, limits.MaxReach))
        {
            LogRejectedAttackPacket(player, packet, "out-of-range");
            return false;
        }

        if (!limits.DamageTypes.Contains(damageType))
        {
            LogRejectedAttackPacket(player, packet, "unconfigured-damage-type");
            return false;
        }

        CombatOverhaulSystem system = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        if (system.Settings.MeleeWeaponStopOnTerrainHit
            && limits.RequiresTerrainClearance(damageType)
            && IsTerrainObstructingHit(player.Entity, position))
        {
            LogRejectedAttackPacket(player, packet, "terrain-obstructed");
            return false;
        }

        float allowedMaxDamage = GetAllowedMaxDamage(limits.MaxDamage, slot.Itemstack, packetTarget);
        if (packet.Damage < 0 || !float.IsFinite(packet.Damage) || packet.Damage > allowedMaxDamage)
        {
            LogRejectedAttackPacket(player, packet, "damage-too-high", $"maxDamage={limits.MaxDamage}, allowedDamage={allowedMaxDamage}");
            return false;
        }

        if (packet.Tier < 0 || packet.Tier > limits.MaxTier)
        {
            LogRejectedAttackPacket(player, packet, "tier-too-high", $"allowedTier={limits.MaxTier}, serverSlashingTierBonus={player.Entity.Stats.GetBlended(MeleeDamageType.DamageTierPlayerStatPrefix + EnumDamageType.SlashingAttack) - 1}, serverPiercingTierBonus={player.Entity.Stats.GetBlended(MeleeDamageType.DamageTierPlayerStatPrefix + EnumDamageType.PiercingAttack) - 1}, serverBluntTierBonus={player.Entity.Stats.GetBlended(MeleeDamageType.DamageTierPlayerStatPrefix + EnumDamageType.BluntAttack) - 1}");
            return false;
        }

        if (packet.ArmorPiercingTier < 0 || packet.ArmorPiercingTier > limits.MaxArmorPiercingTier)
        {
            LogRejectedAttackPacket(player, packet, "armor-piercing-too-high");
            return false;
        }

        if (packet.Knockback < limits.MinKnockback - _knockbackTolerance || packet.Knockback > limits.MaxKnockback + _knockbackTolerance)
        {
            LogRejectedAttackPacket(player, packet, "knockback-out-of-range");
            return false;
        }

        if (packet.DurabilityDamage < 0 || packet.DurabilityDamage > limits.MaxDurabilityDamage)
        {
            LogRejectedAttackPacket(player, packet, "durability-too-high");
            return false;
        }

        if (packet.StaggerTimeMs < 0 || packet.StaggerTimeMs > limits.MaxStaggerTimeMs || packet.StaggerTier < 0 || packet.StaggerTier > limits.MaxStaggerTier)
        {
            LogRejectedAttackPacket(player, packet, "stagger-too-high");
            return false;
        }

        target = packetTarget;
        return true;
    }

    private static bool TryGetPacketPosition(MeleeDamagePacket packet, out Vector3d position)
    {
        position = default;

        if (packet.Position == null || packet.Position.Length < 3) return false;

        double x = packet.Position[0];
        double y = packet.Position[1];
        double z = packet.Position[2];
        if (!double.IsFinite(x) || !double.IsFinite(y) || !double.IsFinite(z)) return false;

        position = new(x, y, z);
        return true;
    }

    private static ItemSlot? GetWeaponSlot(EntityPlayer player, bool mainHand)
    {
        if (!mainHand) return player.LeftHandItemSlot;

        return player.ActiveHandItemSlot?.Itemstack != null
            ? player.ActiveHandItemSlot
            : player.RightHandItemSlot;
    }

    private bool TryGetAttackLimits(EntityPlayer attacker, Entity target, ItemSlot slot, out MeleeAttackLimits limits)
    {
        limits = new();

        if (slot.Itemstack == null) return false;

        ItemStackMeleeWeaponStats stackStats = ItemStackMeleeWeaponStats.FromItemStack(slot.Itemstack);
        MeleeAttackLimitsBuilder builder = new(attacker, target, slot, stackStats);

        try
        {
            MeleeWeaponStats? meleeStats = slot.Itemstack.ItemAttributes?.AsObject<MeleeWeaponStats>();
            if (meleeStats != null)
            {
                AddMeleeWeaponStats(builder, meleeStats);
            }
        }
        catch
        {
            // Some legacy/compat items use a different melee stats shape.
        }

        try
        {
            StanceBasedMeleeWeaponStats? stanceStats = slot.Itemstack.ItemAttributes?.AsObject<StanceBasedMeleeWeaponStats>();
            if (stanceStats != null)
            {
                AddStanceBasedMeleeWeaponStats(builder, stanceStats);
            }
        }
        catch
        {
            // Not every melee weapon uses stance-based stats.
        }

        try
        {
            MeleeWeaponModeCollectionStats? modeStats = slot.Itemstack.ItemAttributes?.AsObject<MeleeWeaponModeCollectionStats>();
            if (modeStats != null)
            {
                foreach (MeleeWeaponModeStats mode in modeStats.Modes.Values)
                {
                    AddMeleeWeaponStats(builder, mode);
                }
            }
        }
        catch
        {
            // Spears and similar modal weapons use Modes; non-modal weapons do not.
        }

        AddServerMeleeAttacks(attacker, target, slot, builder);

        return builder.TryBuild(out limits);
    }

    private static void AddServerMeleeAttacks(EntityPlayer attacker, Entity target, ItemSlot slot, MeleeAttackLimitsBuilder builder)
    {
        CollectibleObject? collectible = slot.Itemstack?.Collectible;
        if (collectible == null) return;

        if (collectible is IHasServerMeleeAttacks itemProvider)
        {
            AddServerMeleeAttacks(attacker, target, slot, builder, itemProvider);
        }

        foreach (CollectibleBehavior behavior in collectible.CollectibleBehaviors ?? Array.Empty<CollectibleBehavior>())
        {
            if (behavior is IHasServerMeleeAttacks behaviorProvider)
            {
                AddServerMeleeAttacks(attacker, target, slot, builder, behaviorProvider);
            }
        }
    }

    private static void AddServerMeleeAttacks(EntityPlayer attacker, Entity target, ItemSlot slot, MeleeAttackLimitsBuilder builder, IHasServerMeleeAttacks provider)
    {
        try
        {
            foreach (ServerMeleeAttackStats attack in provider.GetServerMeleeAttacks(attacker, target, slot))
            {
                builder.Add(attack.Attack, attack.StackStats);
            }
        }
        catch
        {
            // Optional validators should not break normal melee validation.
        }
    }

    private static void AddMeleeWeaponStats(MeleeAttackLimitsBuilder builder, MeleeWeaponStats stats)
    {
        AddStance(builder, stats.OneHandedStance);
        AddStance(builder, stats.TwoHandedStance);
        AddStance(builder, stats.OffHandStance);

        foreach (StanceStats stance in stats.MainHandDualWieldStances.Values) AddStance(builder, stance);
        foreach (StanceStats stance in stats.OffHandDualWieldStances.Values) AddStance(builder, stance);
    }

    private static void AddStance(MeleeAttackLimitsBuilder builder, StanceStats? stance)
    {
        if (stance == null) return;

        builder.Add(stance.Attack);
        builder.Add(stance.Riposte);
        builder.Add(stance.BlockBash);
        builder.Add(stance.HandleAttack);

        foreach (MeleeAttackStats attack in stance.DirectionalAttacks?.Values ?? Enumerable.Empty<MeleeAttackStats>()) builder.Add(attack);
        foreach (MeleeAttackStats attack in stance.DirectionalBlockBashes?.Values ?? Enumerable.Empty<MeleeAttackStats>()) builder.Add(attack);
    }

    private static void AddStanceBasedMeleeWeaponStats(MeleeAttackLimitsBuilder builder, StanceBasedMeleeWeaponStats stats)
    {
        AddGrip(builder, stats.OneHanded);
        AddGrip(builder, stats.TwoHanded);
        AddGrip(builder, stats.OffHand);
    }

    private static void AddGrip(MeleeAttackLimitsBuilder builder, StanceBasedMeleeWeaponGripStats? grip)
    {
        if (grip == null) return;

        builder.Add(grip.DefaultLeftClickAttack);
        builder.Add(grip.DefaultRightClickAttack);

        foreach (StanceBasedMeleeWeaponAttackStats attack in grip.StanceToStanceLeftClickAttacks.Values) builder.Add(attack);
        foreach (StanceBasedMeleeWeaponAttackStats attack in grip.StanceToStanceRightClickAttacks.Values) builder.Add(attack);
    }

    private bool IsTargetWithinReach(EntityPlayer attacker, Entity target, float maxReach)
    {
        CombatOverhaulSystem system = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();
        double allowedDistance = maxReach + system.Settings.CollisionRadius + _reachTolerance;
        Vector3d attackerPosition = new(attacker.Pos.X, attacker.Pos.Y, attacker.Pos.Z);
        Vector3d closestTargetPoint = GetClosestPointOnTargetAabb(attackerPosition, target);

        return Vector3d.Distance(attackerPosition, closestTargetPoint) <= allowedDistance;
    }

    private bool IsTerrainObstructingHit(EntityPlayer attacker, Vector3d hitPosition)
    {
        foreach (Vector3d origin in GetTerrainValidationOrigins(attacker))
        {
            if (!IsTerrainObstructingSegment(origin, hitPosition))
            {
                return false;
            }
        }

        return true;
    }

    private bool IsTerrainObstructingSegment(Vector3d origin, Vector3d hitPosition)
    {
        Vector3d segment = hitPosition - origin;
        double length = segment.Length;
        if (length <= TerrainObstructionStartPadding + TerrainObstructionEndPadding) return false;

        Vector3d direction = segment / length;
        Vector3d start = origin + direction * TerrainObstructionStartPadding;
        Vector3d end = hitPosition - direction * TerrainObstructionEndPadding;
        if ((end - start).LengthSquared <= 1e-6) return false;

        return CuboidAABBCollider.CollideWithTerrain(
            _api.World.BlockAccessor,
            end,
            start,
            TerrainObstructionRadius,
            out _,
            out _,
            out _,
            out _,
            out _);
    }

    private static IEnumerable<Vector3d> GetTerrainValidationOrigins(EntityPlayer attacker)
    {
        Vector3d basePosition = attacker.ServerPos.ToOpenTK();
        yield return basePosition + attacker.LocalEyePos.ToOpenTK();

        double chestY = Math.Max(0.25, attacker.LocalEyePos.Y * 0.65);
        yield return basePosition + new Vector3d(0, chestY, 0);

        double bodyY = Math.Max(0.25, Math.Min(attacker.LocalEyePos.Y * 0.45, attacker.CollisionBox.Y2 * 0.5));
        yield return basePosition + new Vector3d(0, bodyY, 0);
    }

    private static Vector3d GetClosestPointOnTargetAabb(Vector3d point, Entity target)
    {
        Cuboidf collisionBox = target.CollisionBox;
        EntityPos position = target.Pos;
        double minX = Math.Min(collisionBox.X1, collisionBox.X2) + position.X;
        double minY = Math.Min(collisionBox.Y1, collisionBox.Y2) + position.Y;
        double minZ = Math.Min(collisionBox.Z1, collisionBox.Z2) + position.Z;
        double maxX = Math.Max(collisionBox.X1, collisionBox.X2) + position.X;
        double maxY = Math.Max(collisionBox.Y1, collisionBox.Y2) + position.Y;
        double maxZ = Math.Max(collisionBox.Z1, collisionBox.Z2) + position.Z;

        return new(
            Math.Clamp(point.X, minX, maxX),
            Math.Clamp(point.Y, minY, maxY),
            Math.Clamp(point.Z, minZ, maxZ));
    }

    private static float GetAllowedMaxDamage(float maxDamage, ItemStack? weaponStack, Entity target)
    {
        float effectiveMaxDamage = maxDamage;
        float buffableMaxDamage = GrindingWheelCompat.ApplyBuffableDamage(weaponStack, target, maxDamage, null, allowCriticalHit: false);
        if (float.IsFinite(buffableMaxDamage))
        {
            effectiveMaxDamage = MathF.Max(effectiveMaxDamage, buffableMaxDamage);
        }

        return effectiveMaxDamage + MathF.Max(_damageTolerance, MathF.Abs(effectiveMaxDamage) * _damageRelativeTolerance);
    }

    private void LogRejectedAttackPacket(IServerPlayer player, MeleeDamagePacket packet, string reason, string details = "")
    {
        if (_rejectedAttackPacketLogs >= _maxRejectedAttackPacketLogs) return;

        _rejectedAttackPacketLogs++;
        string detailsText = string.IsNullOrEmpty(details) ? "" : $", {details}";
        ItemStack? weapon = GetWeaponSlot(player.Entity, packet.MainHand)?.Itemstack;
        LoggerUtil.Warn(_api, this, $"Rejected melee attack packet from '{player.PlayerName}' ({player.PlayerUID}): reason={reason}, attacker={packet.AttackerEntityId}, target={packet.TargetEntityId}, damage={packet.Damage}, type='{packet.DamageType}', tier={packet.Tier}, weapon='{weapon?.Collectible.Code}', mainHand={packet.MainHand}, characterClass='{player.Entity.WatchedAttributes.GetString("characterClass")}'{detailsText}");
    }

    private readonly struct MeleeAttackLimits
    {
        public readonly float MaxReach;
        public readonly float MaxDamage;
        public readonly float MinKnockback;
        public readonly float MaxKnockback;
        public readonly int MaxTier;
        public readonly int MaxArmorPiercingTier;
        public readonly int MaxDurabilityDamage;
        public readonly int MaxStaggerTimeMs;
        public readonly int MaxStaggerTier;
        public readonly HashSet<EnumDamageType> DamageTypes;
        public readonly HashSet<EnumDamageType> TerrainBypassDamageTypes;

        public MeleeAttackLimits(float maxReach, float maxDamage, float minKnockback, float maxKnockback, int maxTier, int maxArmorPiercingTier, int maxDurabilityDamage, int maxStaggerTimeMs, int maxStaggerTier, HashSet<EnumDamageType> damageTypes, HashSet<EnumDamageType> terrainBypassDamageTypes)
        {
            MaxReach = maxReach;
            MaxDamage = maxDamage;
            MinKnockback = minKnockback;
            MaxKnockback = maxKnockback;
            MaxTier = maxTier;
            MaxArmorPiercingTier = maxArmorPiercingTier;
            MaxDurabilityDamage = maxDurabilityDamage;
            MaxStaggerTimeMs = maxStaggerTimeMs;
            MaxStaggerTier = maxStaggerTier;
            DamageTypes = damageTypes;
            TerrainBypassDamageTypes = terrainBypassDamageTypes;
        }

        public bool RequiresTerrainClearance(EnumDamageType damageType) => !TerrainBypassDamageTypes.Contains(damageType);
    }

    private sealed class MeleeAttackLimitsBuilder
    {
        public MeleeAttackLimitsBuilder(EntityPlayer attacker, Entity target, ItemSlot slot, ItemStackMeleeWeaponStats stackStats)
        {
            _attacker = attacker;
            _target = target;
            _slot = slot;
            _defaultStackStats = stackStats;
            _meleeDamageMultiplier = attacker.Stats.GetBlended("meleeWeaponsDamage");
            _mechanicalsDamageMultiplier = target.Properties.Attributes?["isMechanical"].AsBool() == true
                ? attacker.Stats.GetBlended("mechanicalsDamage")
                : 1f;
            _isDagger = IsDagger(slot);
        }

        public void Add(MeleeAttackStats? attack)
        {
            Add(attack, _defaultStackStats);
        }

        public void Add(MeleeAttackStats? attack, ItemStackMeleeWeaponStats stackStats)
        {
            if (attack == null) return;

            _hasAttack = true;
            _maxReach = Math.Max(_maxReach, attack.MaxReach);

            foreach (MeleeDamageTypeJson damageType in attack.DamageTypes ?? Array.Empty<MeleeDamageTypeJson>())
            {
                Add(damageType, stackStats, attack.CollideWithTerrain);
            }
        }

        public bool TryBuild(out MeleeAttackLimits limits)
        {
            limits = new();

            if (!_hasAttack || _damageTypes.Count == 0) return false;

            limits = new(
                _maxReach,
                _maxDamage,
                _minKnockback,
                _maxKnockback,
                _maxTier,
                _maxArmorPiercingTier,
                _maxDurabilityDamage,
                _maxStaggerTimeMs,
                _maxStaggerTier,
                _damageTypes,
                _terrainBypassDamageTypes);
            return true;
        }

        private void Add(MeleeDamageTypeJson damageType, ItemStackMeleeWeaponStats stackStats)
        {
            Add(damageType, stackStats, collidesWithTerrain: true);
        }

        private void Add(MeleeDamageTypeJson damageType, ItemStackMeleeWeaponStats stackStats, bool collidesWithTerrain)
        {
            if (!Enum.TryParse(damageType.Damage.DamageType, out EnumDamageType configuredDamageType))
            {
                return;
            }

            _damageTypes.Add(configuredDamageType);
            if (!collidesWithTerrain)
            {
                _terrainBypassDamageTypes.Add(configuredDamageType);
            }

            if (configuredDamageType == EnumDamageType.PiercingAttack && _isDagger)
            {
                _damageTypes.Add(EnumDamageType.SlashingAttack);
                if (!collidesWithTerrain)
                {
                    _terrainBypassDamageTypes.Add(EnumDamageType.SlashingAttack);
                }
            }

            float damage = damageType.Damage.Damage * _meleeDamageMultiplier * _mechanicalsDamageMultiplier;
            damage += stackStats.DamageBonus;
            damage *= stackStats.DamageMultiplier;
            _maxDamage = Math.Max(_maxDamage, damage);

            string damageTierStat = MeleeDamageType.DamageTierPlayerStatPrefix + configuredDamageType;
            float statValue = _attacker.Stats.GetBlended(damageTierStat) - 1;
            int damageTier = GameMath.Max(damageType.Damage.Tier + stackStats.DamageTierBonus + (int)statValue, 0);
            _maxTier = Math.Max(_maxTier, damageTier);
            _maxArmorPiercingTier = Math.Max(_maxArmorPiercingTier, damageType.Damage.ArmorPiercingTier + stackStats.ArmorPiercingBonus);
            _maxDurabilityDamage = Math.Max(_maxDurabilityDamage, damageType.DurabilityDamage);
            _maxStaggerTimeMs = Math.Max(_maxStaggerTimeMs, damageType.StaggerTimeMs);
            _maxStaggerTier = Math.Max(_maxStaggerTier, damageType.StaggerTier);

            float knockback = damageType.Knockback * stackStats.KnockbackMultiplier;
            _minKnockback = Math.Min(_minKnockback, knockback);
            _maxKnockback = Math.Max(_maxKnockback, knockback);
        }

        private static bool IsDagger(ItemSlot slot)
        {
            return CollectibleClassifier.IsDagger(slot);
        }

        private readonly EntityPlayer _attacker;
        private readonly Entity _target;
        private readonly ItemSlot _slot;
        private readonly ItemStackMeleeWeaponStats _defaultStackStats;
        private readonly float _meleeDamageMultiplier;
        private readonly float _mechanicalsDamageMultiplier;
        private readonly bool _isDagger;
        private readonly HashSet<EnumDamageType> _damageTypes = [];
        private readonly HashSet<EnumDamageType> _terrainBypassDamageTypes = [];
        private bool _hasAttack;
        private float _maxReach;
        private float _maxDamage;
        private float _minKnockback;
        private float _maxKnockback;
        private int _maxTier;
        private int _maxArmorPiercingTier;
        private int _maxDurabilityDamage;
        private int _maxStaggerTimeMs;
        private int _maxStaggerTier;
    }

    private void Push(MeleeCollisionPacket packet)
    {
        // Push packets are intentionally ignored until server-side entity push physics is rebuilt.
    }

    private bool DealDamage(Entity target, DamageSource damageSource, ItemSlot? slot, float damage)
    {
        OnDealMeleeDamage?.Invoke(target, damageSource, slot, ref damage);
        WeaponBuffSystem.ModifyMeleeDamage(target, damageSource, slot, ref damage);
        damage = GrindingWheelCompat.ApplyBuffableDamage(slot?.Itemstack, target, damage, damageSource);

        bool damageReceived = target.ReceiveDamage(damageSource, damage);
        if (OnMeleeDamageResolved != null)
        {
            DamageBlockEventArgs? block = target.GetBehavior<PlayerDamageModelBehavior>()?.GetLastDamageBlock(damageSource);
            OnMeleeDamageResolved.Invoke(new(target, damageSource, slot, damage, damageReceived, block));
        }

        return damageReceived;
    }

    private void DealDurabilityDamage(ItemSlot? slot, MeleeDamagePacket packet, Entity? attacker)
    {
        if (packet.DurabilityDamage <= 0) return;

        if (slot?.Itemstack?.Collectible != null && attacker != null)
        {
            slot.Itemstack.Collectible.DamageItem(attacker.Api.World, attacker, slot, packet.DurabilityDamage);
            slot.MarkDirty();
        }
    }

    private void PrintLog(Entity? attacker, bool damageReceived, Entity target, MeleeDamagePacket packet, string targetName)
    {
        bool printIntoChat = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings.PrintMeleeHits;

        if (printIntoChat)
        {
            float damage = damageReceived ? target.WatchedAttributes.GetFloat("onHurt") : 0;

            string damageLogMessage = Lang.Get("combatoverhaul:damagelog-dealt-damage", Lang.Get($"combatoverhaul:entity-damage-zone-{(ColliderTypes)packet.ColliderType}"), targetName, $"{damage:F2}");

            ((attacker as EntityPlayer)?.Player as IServerPlayer)?.SendMessage(GlobalConstants.DamageLogChatGroup, damageLogMessage, EnumChatType.Notification);
        }
    }
}
