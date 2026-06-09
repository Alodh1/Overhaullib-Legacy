using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Implementations;
using CombatOverhaul.Integration;
using CombatOverhaul.Utils;
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
            MeleeAttackDamagePackets = packets.ToArray()
        });
    }

    public void SendPackets(IEnumerable<MeleeCollisionPacket> packets)
    {
        _clientChannel.SendPacket(new MeleePushPacket
        {
            MeleeAttackDamagePackets = packets.ToArray()
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
    public delegate void MeleeAttackDelegate(Entity attacker, ItemSlot weaponSlot);

    public event MeleeDamageDelegate? OnDealMeleeDamage;
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
    private const float _knockbackTolerance = 0.01f;
    private const int _maxRejectedAttackPacketLogs = 20;
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

        if (packet.Damage < 0 || packet.Damage > limits.MaxDamage + _damageTolerance)
        {
            LogRejectedAttackPacket(player, packet, "damage-too-high");
            return false;
        }

        if (packet.Tier < 0 || packet.Tier > limits.MaxTier)
        {
            LogRejectedAttackPacket(player, packet, "tier-too-high");
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

        return builder.TryBuild(out limits);
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

    private static Vector3d GetClosestPointOnTargetAabb(Vector3d point, Entity target)
    {
        Cuboidf collisionBox = target.CollisionBox.Clone();
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

    private void LogRejectedAttackPacket(IServerPlayer player, MeleeDamagePacket packet, string reason)
    {
        if (_rejectedAttackPacketLogs >= _maxRejectedAttackPacketLogs) return;

        _rejectedAttackPacketLogs++;
        LoggerUtil.Warn(_api, this, $"Rejected melee attack packet from '{player.PlayerName}' ({player.PlayerUID}): reason={reason}, attacker={packet.AttackerEntityId}, target={packet.TargetEntityId}, damage={packet.Damage}, type='{packet.DamageType}'");
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

        public MeleeAttackLimits(float maxReach, float maxDamage, float minKnockback, float maxKnockback, int maxTier, int maxArmorPiercingTier, int maxDurabilityDamage, int maxStaggerTimeMs, int maxStaggerTier, HashSet<EnumDamageType> damageTypes)
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
        }
    }

    private sealed class MeleeAttackLimitsBuilder
    {
        public MeleeAttackLimitsBuilder(EntityPlayer attacker, Entity target, ItemSlot slot, ItemStackMeleeWeaponStats stackStats)
        {
            _attacker = attacker;
            _target = target;
            _slot = slot;
            _stackStats = stackStats;
            _meleeDamageMultiplier = attacker.Stats.GetBlended("meleeWeaponsDamage");
            _mechanicalsDamageMultiplier = target.Properties.Attributes?["isMechanical"].AsBool() == true
                ? attacker.Stats.GetBlended("mechanicalsDamage")
                : 1f;
            _isDagger = IsDagger(slot);
        }

        public void Add(MeleeAttackStats? attack)
        {
            if (attack == null) return;

            _hasAttack = true;
            _maxReach = Math.Max(_maxReach, attack.MaxReach);

            foreach (MeleeDamageTypeJson damageType in attack.DamageTypes ?? Array.Empty<MeleeDamageTypeJson>())
            {
                Add(damageType);
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
                _damageTypes);
            return true;
        }

        private void Add(MeleeDamageTypeJson damageType)
        {
            if (!Enum.TryParse(damageType.Damage.DamageType, out EnumDamageType configuredDamageType))
            {
                return;
            }

            _damageTypes.Add(configuredDamageType);
            if (configuredDamageType == EnumDamageType.PiercingAttack && _isDagger)
            {
                _damageTypes.Add(EnumDamageType.SlashingAttack);
            }

            float damage = damageType.Damage.Damage * _meleeDamageMultiplier * _mechanicalsDamageMultiplier;
            damage += _stackStats.DamageBonus;
            damage *= _stackStats.DamageMultiplier;
            _maxDamage = Math.Max(_maxDamage, damage);

            string damageTierStat = MeleeDamageType.DamageTierPlayerStatPrefix + configuredDamageType;
            float statValue = _attacker.Stats.GetBlended(damageTierStat) - 1;
            int damageTier = GameMath.Max(damageType.Damage.Tier + _stackStats.DamageTierBonus + (int)statValue, 0);
            _maxTier = Math.Max(_maxTier, damageTier);
            _maxArmorPiercingTier = Math.Max(_maxArmorPiercingTier, damageType.Damage.ArmorPiercingTier + _stackStats.ArmorPiercingBonus);
            _maxDurabilityDamage = Math.Max(_maxDurabilityDamage, damageType.DurabilityDamage);
            _maxStaggerTimeMs = Math.Max(_maxStaggerTimeMs, damageType.StaggerTimeMs);
            _maxStaggerTier = Math.Max(_maxStaggerTier, damageType.StaggerTier);

            float knockback = damageType.Knockback * _stackStats.KnockbackMultiplier;
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
        private readonly ItemStackMeleeWeaponStats _stackStats;
        private readonly float _meleeDamageMultiplier;
        private readonly float _mechanicalsDamageMultiplier;
        private readonly bool _isDagger;
        private readonly HashSet<EnumDamageType> _damageTypes = [];
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
        damage = GrindingWheelCompat.ApplyBuffableDamage(slot?.Itemstack, target, damage);

        return target.ReceiveDamage(damageSource, damage);
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
