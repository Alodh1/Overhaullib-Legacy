using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
using CombatOverhaul.Integration;
using CombatOverhaul.Utils;
using CombatOverhaul.WeaponBuffs;
using OpenTK.Mathematics;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace CombatOverhaul.RangedSystems;

public class ProjectileStats
{
    public int AdditionalDurabilityCost { get; set; } = 0;
    public string ImpactSound { get; set; } = "game:sounds/arrow-impact";
    public string HitSound { get; set; } = "game:sounds/player/projectilehit";
    public float CollisionRadius { get; set; } = 0;
    public float PenetrationDistance { get; set; } = 0;
    public ProjectileDamageDataJson DamageStats { get; set; } = new();
    public int DamageTierBonus { get; set; } = 0;
    public float SpeedThreshold { get; set; } = 0;
    public float Knockback { get; set; } = 0;
    public string EntityCode { get; set; } = "";
    public int DurabilityDamage { get; set; } = 0;
    public float DropChance { get; set; } = 0;
    public float PenetrationBonus { get; set; } = 0;
    public bool CanBeCollected { get; set; } = true;
    public Dictionary<string, ProjectileDamageDataJson>? DamageStatsByType { get; set; }

    public ProjectileStats() { }

    public ProjectileStats(int additionalDurabilityCost, string impactSound, string hitSound, float collisionRadius, float penetrationDistance, ProjectileDamageDataJson damageStats, int damageTierBonus, float speedThreshold, float knockback, string entityCode, int durabilityDamage, float dropChance, float penetrationBonus, bool canBeCollected)
    {
        AdditionalDurabilityCost = additionalDurabilityCost;
        ImpactSound = impactSound;
        HitSound = hitSound;
        CollisionRadius = collisionRadius;
        PenetrationDistance = penetrationDistance;
        DamageStats = damageStats;
        DamageTierBonus = damageTierBonus;
        SpeedThreshold = speedThreshold;
        Knockback = knockback;
        EntityCode = entityCode;
        DurabilityDamage = durabilityDamage;
        DropChance = dropChance;
        PenetrationBonus = penetrationBonus;
        CanBeCollected = canBeCollected;
    }

    public ProjectileStats Clone()
    {
        return new ProjectileStats(AdditionalDurabilityCost, ImpactSound, HitSound, CollisionRadius, PenetrationDistance, new ProjectileDamageDataJson() { Damage = DamageStats.Damage, DamageType = DamageStats.DamageType }, DamageTierBonus, SpeedThreshold, Knockback, EntityCode, DurabilityDamage, DropChance, PenetrationBonus, CanBeCollected);
    }
}

public readonly struct ItemStackProjectileStats
{
    public readonly float DamageMultiplier;
    public readonly int DamageTierBonus;
    public readonly float KnockbackMultiplier;
    public readonly float DropChanceMultiplier;
    public readonly float PenetrationBonus;
    public readonly int AdditionalDurabilityCost;

    public ItemStackProjectileStats()
    {
        DamageMultiplier = 1;
        DamageTierBonus = 0;
        KnockbackMultiplier = 1;
        DropChanceMultiplier = 1;
        PenetrationBonus = 0;
        AdditionalDurabilityCost = 0;
    }

    public ItemStackProjectileStats(float damageMultiplier, int damageTierBonus, float knockbackMultiplier, float dropChanceMultiplier, float penetrationBonus, int additionalDurabilityCost)
    {
        DamageMultiplier = damageMultiplier;
        DamageTierBonus = damageTierBonus;
        KnockbackMultiplier = knockbackMultiplier;
        DropChanceMultiplier = dropChanceMultiplier;
        PenetrationBonus = penetrationBonus;
        AdditionalDurabilityCost = additionalDurabilityCost;
    }

    public static ItemStackProjectileStats FromItemStack(ItemStack stack)
    {
        float damageMultiplier = stack.Attributes.GetFloat("damageMultiplier", 1);
        int damageTierBonus = stack.Attributes.GetInt("damageTierBonus", 0);
        float knockbackMultiplier = stack.Attributes.GetFloat("knockbackMultiplier", 1);
        float dropChanceMultiplier = stack.Attributes.GetFloat("dropChanceMultiplier", 1);
        float penetrationBonus = stack.Attributes.GetFloat("penetrationBonus", 0);
        int additionalDurabilityCost = stack.Attributes.GetInt("additionalDurabilityCost", 0);

        return WeaponBuffSystem.ComposeProjectileStackStats(stack, new()
        {
            DamageMultiplier = damageMultiplier,
            DamageTierBonus = damageTierBonus,
            KnockbackMultiplier = knockbackMultiplier,
            DropChanceMultiplier = dropChanceMultiplier,
            PenetrationBonus = penetrationBonus,
            AdditionalDurabilityCost = additionalDurabilityCost
        });
    }
}

public struct ProjectileSpawnStats
{
    public long ProducerEntityId { get; set; }
    public float DamageMultiplier { get; set; }
    public int DamageTier { get; set; }
    public Vector3d Position { get; set; }
    public Vector3d Velocity { get; set; }
    public float GravityFactorMultiplier { get; set; }
    [Obsolete("Use DamageTier instead")]
    public float DamageStrength { get => DamageTier; set => DamageTier = (int)value; } // for compatibility
}

public sealed class RangedDamageResolvedEventArgs
{
    public RangedDamageResolvedEventArgs(Entity target, DamageSource damageSource, ItemStack? weaponStack, ItemStack? projectileStack, float damage, bool damageReceived, DamageBlockEventArgs? block)
    {
        Target = target;
        DamageSource = damageSource;
        WeaponStack = weaponStack;
        ProjectileStack = projectileStack;
        Damage = damage;
        DamageReceived = damageReceived;
        Block = block;
    }

    public Entity Target { get; }
    public DamageSource DamageSource { get; }
    public ItemStack? WeaponStack { get; }
    public ItemStack? ProjectileStack { get; }
    public float Damage { get; }
    public bool DamageReceived { get; }
    public DamageBlockEventArgs? Block { get; }
    public bool WasBlocked => Block != null;
    public bool WasParried => Block?.Kind == EnumDamageBlockKind.Parry;
    public bool IsLegalHit => DamageReceived && Block == null;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileCollisionPacket
{
    public Guid Id { get; set; }
    public double[] CollisionPoint { get; set; } = Array.Empty<double>();
    public double[] AfterCollisionVelocity { get; set; } = Array.Empty<double>();
    public double RelativeSpeed { get; set; }
    public string Collider { get; set; } = "";
    public long ReceiverEntity { get; set; }
    public int PacketVersion { get; set; }
    public float PenetrationStrengthLoss { get; set; } = 0;
}

[ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
public class ProjectileCollisionCheckRequest
{
    public Guid ProjectileId { get; set; }
    public long ProjectileEntityId { get; set; }
    public double[] CurrentPosition { get; set; } = Array.Empty<double>();
    public double[] PreviousPosition { get; set; } = Array.Empty<double>();
    public double[] Velocity { get; set; } = Array.Empty<double>();
    public double[] ShooterPosition { get; set; } = Array.Empty<double>();
    public double[] ShotDirection { get; set; } = Array.Empty<double>();
    public float Radius { get; set; }
    public float PenetrationDistance { get; set; }
    public float PenetrationStrength { get; set; }
    public bool CollideWithShooter { get; set; }
    public long[] IgnoreEntities { get; set; } = Array.Empty<long>();
    public long[] CandidateEntityIds { get; set; } = Array.Empty<long>();
    public int PacketVersion { get; set; }
    public long ShooterId { get; set; }
    public bool CheckAABBOnly { get; set; } = false;
    public bool RequireColliderWhenAvailable { get; set; } = false;
}

public sealed class ProjectileSystemClient
{
    public const string NetworkChannelId = "CombatOverhaul:projectiles";

    public ProjectileSystemClient(ICoreClientAPI api, EntityPartitioning entityPartitioning)
    {
        _clientChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>()
            .RegisterMessageType<ProjectileCollisionCheckRequest>()
            .SetMessageHandler<ProjectileCollisionCheckRequest>(HandleRequest);

        _api = api;
        _entityPartitioning = entityPartitioning;
        _combatOverhaulSystem = _api.ModLoader.GetModSystem<CombatOverhaulSystem>();
    }

    public void Collide(Guid id, Entity target, Vector3d point, Vector3d velocity, double relativeSpeed, string collider, ProjectileCollisionCheckRequest packet, float penetrationStrenghLoss)
    {
        ProjectileCollisionPacket newPacket = new()
        {
            Id = id,
            CollisionPoint = [point.X, point.Y, point.Z],
            AfterCollisionVelocity = [velocity.X, velocity.Y, velocity.Z],
            RelativeSpeed = relativeSpeed,
            ReceiverEntity = target.EntityId,
            Collider = collider,
            PacketVersion = packet.PacketVersion,
            PenetrationStrengthLoss = penetrationStrenghLoss
        };

        _clientChannel.SendPacket(newPacket);
    }

    private readonly ICoreClientAPI _api;
    private readonly IClientNetworkChannel _clientChannel;
    private readonly EntityPartitioning _entityPartitioning;
    private readonly CombatOverhaulSystem _combatOverhaulSystem;
    private readonly Dictionary<string, int> _firearmsCollisionMissCounts = new();
    private long _nextFirearmsCollisionDiagnosticsAtMs;
    private const int FirearmsCollisionDiagnosticsIntervalMs = 10000;
    private const double BehindShooterTolerance = -0.25;

    //private Stopwatch _stopwatch = new();

    private void HandleRequest(ProjectileCollisionCheckRequest packet)
    {
        Vector3d currentPosition = new(packet.CurrentPosition[0], packet.CurrentPosition[1], packet.CurrentPosition[2]);
        Vector3d previousPosition = new(packet.PreviousPosition[0], packet.PreviousPosition[1], packet.PreviousPosition[2]);
        Vector3d velocity = new(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2]);
        Vector3d segment = currentPosition - previousPosition;

        Vec3d midPoint = new(
            segment.X / 2f + previousPosition.X,
            segment.Y / 2f + previousPosition.Y,
            segment.Z / 2f + previousPosition.Z);

        double searchRadius = Math.Max(
            _combatOverhaulSystem.Settings.CollisionRadius + packet.Radius,
            segment.Length / 2f + _combatOverhaulSystem.Settings.CollisionRadius + packet.Radius);

        long[] candidateEntityIds = packet.CandidateEntityIds ?? Array.Empty<long>();
        if (candidateEntityIds.Length > 0)
        {
            foreach (long entityId in candidateEntityIds)
            {
                Entity? entity = _api.World.GetEntityById(entityId);
                if (entity == null || !CanProjectileHit(entity)) continue;

                if (Collide(entity, packet, currentPosition, previousPosition, velocity))
                {
                    return;
                }
            }

            return;
        }

        if (TryCollidePartitionedEntities(midPoint, searchRadius, packet, currentPosition, previousPosition, velocity))
        {
            return;
        }

        Entity[] entities = _api.World.GetEntitiesAround(midPoint, (float)searchRadius, (float)searchRadius);
        foreach (Entity entity in entities)
        {
            if (!CanProjectileHit(entity)) continue;

            if (Collide(entity, packet, currentPosition, previousPosition, velocity))
            {
                return;
            }
        }
    }

    private bool TryCollidePartitionedEntities(Vec3d midPoint, double searchRadius, ProjectileCollisionCheckRequest packet, Vector3d currentPosition, Vector3d previousPosition, Vector3d velocity)
    {
        bool collided = false;
        _entityPartitioning.WalkEntities(midPoint, searchRadius, entity =>
        {
            if (!CanProjectileHit(entity)) return true;

            if (Collide(entity, packet, currentPosition, previousPosition, velocity))
            {
                collided = true;
                return false;
            }

            return true;
        }, EnumEntitySearchType.Creatures);

        return collided;
    }

    private void RecordFirearmsCollisionMiss(string reason)
    {
        _firearmsCollisionMissCounts.TryGetValue(reason, out int count);
        _firearmsCollisionMissCounts[reason] = count + 1;

        FlushFirearmsCollisionMissDiagnostics();
    }

    private void FlushFirearmsCollisionMissDiagnostics()
    {
        long now = _api.World.ElapsedMilliseconds;
        if (_nextFirearmsCollisionDiagnosticsAtMs == 0)
        {
            _nextFirearmsCollisionDiagnosticsAtMs = now + FirearmsCollisionDiagnosticsIntervalMs;
            return;
        }

        if (now < _nextFirearmsCollisionDiagnosticsAtMs || _firearmsCollisionMissCounts.Count == 0)
        {
            return;
        }

        string counts = string.Join(", ", _firearmsCollisionMissCounts.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"));
        LoggerUtil.Verbose(_api, typeof(ProjectileSystemClient), $"Firearms projectile CO collider miss counts: {counts}");

        _firearmsCollisionMissCounts.Clear();
        _nextFirearmsCollisionDiagnosticsAtMs = now + FirearmsCollisionDiagnosticsIntervalMs;
    }

    private static bool CanProjectileHit(Entity entity)
    {
        if (!entity.Alive) return false;

        return entity.IsCreature || entity.GetBehavior<EntityBehaviorHealth>() != null;
    }

    private bool Collide(Entity target, ProjectileCollisionCheckRequest packet, Vector3d currentPosition, Vector3d previousPosition, Vector3d velocity)
    {
        if (target.EntityId == packet.ProjectileEntityId) return false;

        if (!packet.CollideWithShooter && packet.ShooterId == target.EntityId) return false;

        if (Array.IndexOf(packet.IgnoreEntities, target.EntityId) >= 0) return false;

        if (!CheckCollision(target, out string collider, out Vector3d point, currentPosition, previousPosition, packet.Radius, packet.PenetrationDistance, packet.PenetrationStrength, out float penetrationStrengthLoss, packet.CheckAABBOnly, packet.RequireColliderWhenAvailable, out string collisionMode, out string missReason))
        {
            if (packet.RequireColliderWhenAvailable && collisionMode == "CO" && missReason.Length > 0)
            {
                RecordFirearmsCollisionMiss(missReason);
            }

            return false;
        }

        if (IsCollisionBehindShooter(packet, point))
        {
            return false;
        }

        Vector3d targetVelocity = new((float)target.Pos.Motion.X, (float)target.Pos.Motion.Y, (float)target.Pos.Motion.Z);

        double relativeSpeed = (targetVelocity - velocity).Length;

        Collide(packet.ProjectileId, target, point, targetVelocity, relativeSpeed, collider, packet, penetrationStrengthLoss);

        return true;
    }

    private static bool IsCollisionBehindShooter(ProjectileCollisionCheckRequest packet, Vector3d point)
    {
        if (packet.ShooterPosition == null || packet.ShooterPosition.Length < 3) return false;
        if (packet.ShotDirection == null || packet.ShotDirection.Length < 3) return false;

        Vector3d shooterPosition = new(packet.ShooterPosition[0], packet.ShooterPosition[1], packet.ShooterPosition[2]);
        Vector3d shotDirection = new(packet.ShotDirection[0], 0, packet.ShotDirection[2]);

        if (shotDirection.LengthSquared <= 1e-8)
        {
            shotDirection = new(packet.ShotDirection[0], packet.ShotDirection[1], packet.ShotDirection[2]);
        }

        if (shotDirection.LengthSquared <= 1e-8) return false;

        shotDirection = shotDirection.Normalized();
        Vector3d toPoint = new(point.X - shooterPosition.X, 0, point.Z - shooterPosition.Z);

        if (toPoint.LengthSquared <= 1e-8)
        {
            toPoint = point - shooterPosition;
        }

        return Vector3d.Dot(toPoint, shotDirection) < BehindShooterTolerance;
    }

    private bool CheckCollision(Entity target, out string collider, out Vector3d point, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationDistance, float penetrationSrength, out float penetrationStrengthLoss, bool checkAABBOnly, bool requireColliderWhenAvailable, out string collisionMode, out string missReason)
    {
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        EntityDamageModelBehavior? damageModel = target.GetBehavior<EntityDamageModelBehavior>();
        PlayerDamageModelBehavior? playerDamageModel = target.GetBehavior<PlayerDamageModelBehavior>();
        collider = "";
        point = new();
        penetrationStrengthLoss = 0;
        bool hasUsableDetailedColliders = colliders?.HasUsableDetailedColliders == true;
        collisionMode = colliders == null
            ? "AABBNoColliders"
            : checkAABBOnly
                ? "AABBOnly"
                : hasUsableDetailedColliders
                    ? "CO"
                    : "AABBNoUsableColliders";
        missReason = "";
        float defaultColliderPenetrationResistance = _combatOverhaulSystem.Settings.DefaultColliderPenetrationResistance;
        bool requireDetailedCollider = requireColliderWhenAvailable && hasUsableDetailedColliders && !checkAABBOnly;

        bool aabbHit = CheckEntityAabb(target, currentPosition, previousPosition, radius, defaultColliderPenetrationResistance, out Vector3d aabbPoint, out float aabbPenetrationStrengthLoss);

        if (colliders == null || checkAABBOnly || !hasUsableDetailedColliders)
        {
            if (!aabbHit)
            {
                missReason = "aabb-miss";
                return false;
            }

            point = aabbPoint;
            penetrationStrengthLoss = aabbPenetrationStrengthLoss;
            return true;
        }

        bool result;
        List<(string key, double parameter, Vector3d point)> intersections;

        try
        {
            result = colliders.Collide(currentPosition, previousPosition, radius, penetrationDistance, out intersections);
        }
        catch (Exception exception)
        {
            missReason = $"co-collide-exception:{exception.GetType().Name}";
            if (requireDetailedCollider) return false;

            if (!aabbHit) return false;

            point = aabbPoint;
            penetrationStrengthLoss = aabbPenetrationStrengthLoss;
            return true;
        }

        if (!result || intersections.Count == 0)
        {
            if (requireDetailedCollider)
            {
                missReason = result ? "co-no-intersections" : "co-miss";
                return false;
            }

            if (aabbHit)
            {
                point = aabbPoint;
                penetrationStrengthLoss = aabbPenetrationStrengthLoss;
                return true;
            }

            if (colliders.HasOBBCollider && colliders.Colliders.Count > 0 &&
                CheckCollisionBox(colliders.BoundingBox, currentPosition, previousPosition, radius, colliders.DefaultPenetrationResistance, out point, out penetrationStrengthLoss))
            {
                return true;
            }

            return false;
        }

        penetrationStrengthLoss = colliders.DefaultPenetrationResistance;

        if (damageModel == null && playerDamageModel == null)
        {
            collider = intersections[0].key;
            point = intersections[0].point;
            return true;
        }

        if (damageModel != null)
        {
            bool selectedHit = false;
            float maxDamageMultiplier = 0;
            penetrationStrengthLoss = 0;
            ColliderTypes selectedColliderType = ColliderTypes.Torso;

            Span<ColliderTypes> encounteredColliderTypes = stackalloc ColliderTypes[16];
            int encounteredColliderTypeCount = 0;

            foreach ((string key, double parameter, Vector3d intersectionPoint) in intersections)
            {
                bool knownColliderType = colliders.CollidersTypes.TryGetValue(key, out ColliderTypes colliderType);
                if (!knownColliderType)
                {
                    colliderType = ColliderTypes.Torso;
                }

                if (knownColliderType && colliderType == ColliderTypes.Resistant && colliders.ResistantCollidersStopProjectiles)
                {
                    if (!selectedHit)
                    {
                        collider = key;
                        point = intersectionPoint;
                        selectedColliderType = colliderType;
                        selectedHit = true;
                    }

                    penetrationStrengthLoss = penetrationSrength;
                    break;
                }

                float damageMultiplier = damageModel.DamageMultipliers.TryGetValue(colliderType, out float multiplier) ? multiplier : 1f;
                if (!selectedHit || damageMultiplier >= maxDamageMultiplier)
                {
                    maxDamageMultiplier = damageMultiplier;
                    collider = knownColliderType || requireDetailedCollider ? key : "";
                    point = intersectionPoint;
                    selectedColliderType = colliderType;
                    selectedHit = true;
                }

                float penetrationResistance = colliders.DefaultPenetrationResistance;
                if (colliders.PenetrationResistances.ContainsKey(key))
                {
                    penetrationResistance = colliders.PenetrationResistances[key];
                }

                if (!Contains(encounteredColliderTypes, encounteredColliderTypeCount, colliderType))
                {
                    penetrationStrengthLoss += penetrationResistance;
                    AddEncountered(encounteredColliderTypes, ref encounteredColliderTypeCount, colliderType);
                }

                if (penetrationStrengthLoss > penetrationSrength)
                {
                    break;
                }
            }

            if (!selectedHit)
            {
                if (requireDetailedCollider)
                {
                    missReason = "co-no-selected-collider";
                    return false;
                }

                if (aabbHit)
                {
                    collider = "";
                    point = aabbPoint;
                    penetrationStrengthLoss = aabbPenetrationStrengthLoss;
                }
                else
                {
                    collider = "";
                    point = intersections[0].point;
                    penetrationStrengthLoss = colliders.DefaultPenetrationResistance;
                }
            }
            else if (selectedColliderType == ColliderTypes.Resistant && aabbHit && !requireDetailedCollider)
            {
                collider = "";
                point = aabbPoint;
                penetrationStrengthLoss = aabbPenetrationStrengthLoss;
            }

            return true;
        }

        if (playerDamageModel != null)
        {
            bool selectedHit = false;
            float maxDamageMultiplier = 0;
            penetrationStrengthLoss = 0;

            Span<PlayerBodyPart> encounteredColliderTypes = stackalloc PlayerBodyPart[16];
            int encounteredColliderTypeCount = 0;

            foreach ((string key, double parameter, Vector3d intersectionPoint) in intersections)
            {
                if (!playerDamageModel.CollidersToBodyParts.TryGetValue(key, out PlayerBodyPart bodyType))
                {
                    continue;
                }

                float damageMultiplier = GetPlayerBodyPartMultiplier(playerDamageModel, bodyType);
                if (!selectedHit || damageMultiplier >= maxDamageMultiplier)
                {
                    maxDamageMultiplier = damageMultiplier;
                    collider = key;
                    point = intersectionPoint;
                    selectedHit = true;
                }

                float penetrationResistance = colliders.DefaultPenetrationResistance;
                if (colliders.PenetrationResistances.ContainsKey(key))
                {
                    penetrationResistance = colliders.PenetrationResistances[key];
                }

                if (!Contains(encounteredColliderTypes, encounteredColliderTypeCount, bodyType))
                {
                    penetrationStrengthLoss += penetrationResistance;
                    AddEncountered(encounteredColliderTypes, ref encounteredColliderTypeCount, bodyType);
                }

                if (penetrationStrengthLoss > penetrationSrength)
                {
                    break;
                }
            }

            if (!selectedHit)
            {
                if (requireDetailedCollider)
                {
                    missReason = "co-no-mapped-player-body-part";
                    return false;
                }

                if (aabbHit)
                {
                    collider = "";
                    point = aabbPoint;
                    penetrationStrengthLoss = aabbPenetrationStrengthLoss;
                }
                else
                {
                    collider = "";
                    point = intersections[0].point;
                    penetrationStrengthLoss = colliders.DefaultPenetrationResistance;
                }
            }
        }

        return true;
    }

    private static bool Contains<T>(Span<T> values, int count, T value)
    {
        for (int index = 0; index < count; index++)
        {
            if (EqualityComparer<T>.Default.Equals(values[index], value)) return true;
        }

        return false;
    }

    private static void AddEncountered<T>(Span<T> values, ref int count, T value)
    {
        if (count >= values.Length) return;

        values[count++] = value;
    }

    private static float GetPlayerBodyPartMultiplier(PlayerDamageModelBehavior playerDamageModel, PlayerBodyPart bodyType)
    {
        foreach (DamageZoneStats damageZone in playerDamageModel.DamageModel.DamageZones)
        {
            if (damageZone.ZoneType == bodyType)
            {
                return damageZone.DamageMultiplier;
            }
        }

        return 1f;
    }
    private static bool CheckEntityAabb(Entity target, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationResistance, out Vector3d point, out float penetrationStrengthLoss)
    {
        return CheckCollisionBox(GetCollisionBox(target), currentPosition, previousPosition, radius, penetrationResistance, out point, out penetrationStrengthLoss);
    }
    private static bool CheckCollisionBox(CuboidAABBCollider collisionBox, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationResistance, out Vector3d point, out float penetrationStrengthLoss)
    {
        point = new();
        penetrationStrengthLoss = 0;

        if (!collisionBox.Collide(currentPosition, previousPosition, radius, out point)) return false;

        penetrationStrengthLoss = penetrationResistance;
        return true;
    }
    private static CuboidAABBCollider GetCollisionBox(Entity entity)
    {
        Cuboidf collisionBox = entity.CollisionBox;
        EntityPos position = entity.Pos;
        return new(
            new Vector3d(collisionBox.X1 + position.X, collisionBox.Y1 + position.Y, collisionBox.Z1 + position.Z),
            new Vector3d(collisionBox.X2 + position.X, collisionBox.Y2 + position.Y, collisionBox.Z2 + position.Z)
        );
    }
}

public sealed class ProjectileSystemServer
{
    public delegate void RangedDamageDelegate(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage);
    public delegate void RangedDamageResolvedDelegate(RangedDamageResolvedEventArgs args);

    public event RangedDamageDelegate? OnDealRangedDamage;
    public event RangedDamageResolvedDelegate? OnRangedDamageResolved;

    public ProjectileSystemServer(ICoreServerAPI api)
    {
        _api = api;
        _serverChannel = api.Network.RegisterChannel(NetworkChannelId)
            .RegisterMessageType<ProjectileCollisionPacket>()
            .SetMessageHandler<ProjectileCollisionPacket>(HandleCollision)
            .RegisterMessageType<ProjectileCollisionCheckRequest>();
    }

    public const string NetworkChannelId = "CombatOverhaul:projectiles";
    public const float PenetrationDistanceOffset = 5f; // Temporary fix to ensure projectile hitting stuff inside entity model

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter)
    {
        SpawnInternal(id, projectileStats, spawnStats, projectileStack, weaponStack, null, shooter, shooter, applyBuffs: false);
    }

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter, Entity target)
    {
        SpawnInternal(id, projectileStats, spawnStats, projectileStack, weaponStack, null, shooter, target, applyBuffs: false);
    }

    public void SpawnFromWeaponSlot(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemSlot weaponSlot, Entity shooter)
    {
        SpawnInternal(id, projectileStats, spawnStats, projectileStack, weaponSlot.Itemstack, weaponSlot, shooter, shooter, applyBuffs: true);
    }

    public void SpawnFromWeaponSlot(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemSlot weaponSlot, Entity shooter, Entity target)
    {
        SpawnInternal(id, projectileStats, spawnStats, projectileStack, weaponSlot.Itemstack, weaponSlot, shooter, target, applyBuffs: true);
    }

    private void SpawnInternal(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, ItemSlot? weaponSlot, Entity shooter, Entity target, bool applyBuffs)
    {
        EntityPlayer? owner = (shooter as EntityPlayer) ??
            (target as EntityPlayer) ??
            _api.World.GetNearestEntity(target.Pos.XYZ, _nearestPlayerSearchRange, _nearestPlayerSearchRange, entity => entity is EntityPlayer) as EntityPlayer;

        if (owner == null)
        {
            return;
        }

        if (applyBuffs)
        {
            WeaponBuffSystem.ModifyProjectileSpawn(projectileStats, ref spawnStats, projectileStack, weaponStack, shooter, target);
        }

        SpawnProjectile(id, projectileStack, weaponStack, projectileStats, spawnStats, _api, shooter, owner, out ProjectileEntity? projectile);

        if (projectile != null)
        {
            _projectiles.Add(id, new(projectile, projectileStats, spawnStats, _api, ClearId, projectileStack, weaponSlot, applyBuffs));
            projectile.ServerProjectile = _projectiles[id];
            if (applyBuffs && WeaponBuffSystem.ConsumeInternal(weaponStack, WeaponBuffConsumptionTrigger.ProjectileSpawn) > 0)
            {
                weaponSlot?.MarkDirty();
            }
            if (applyBuffs)
            {
                WeaponBuffSystem.ConsumeInternal(projectileStack, WeaponBuffConsumptionTrigger.ProjectileSpawn);
            }
        }
    }
    public void TryCollide(ProjectileEntity projectile)
    {
        double deltaX = projectile.ServerPos.X - projectile.PreviousPosition.X;
        double deltaY = projectile.ServerPos.Y - projectile.PreviousPosition.Y;
        double deltaZ = projectile.ServerPos.Z - projectile.PreviousPosition.Z;
        if (deltaX * deltaX + deltaY * deltaY + deltaZ * deltaZ <= 1e-12)
        {
            return;
        }

        bool isFirearmsProjectile = IsFirearmsProjectile(projectile);
        long[] candidateEntityIds = isFirearmsProjectile ? GetProjectileCollisionCandidateEntityIds(projectile) : Array.Empty<long>();
        if (isFirearmsProjectile && candidateEntityIds.Length == 0) return;
        bool checkAABBOnly = CheckAABBOnly(_api, projectile);
        long[] ignoredEntities = projectile.CollidedWith.Count == 0 ? Array.Empty<long>() : projectile.CollidedWith.ToArray();
        Entity? shooter = _api.World.GetEntityById(projectile.ShooterId);
        bool gateAgainstShooterBackHits = shooter is EntityPlayer;
        Vector3d shotDirection = gateAgainstShooterBackHits ? GetProjectileShotDirection(projectile) : Vector3d.Zero;
        Vec3d shooterPosition = gateAgainstShooterBackHits ? GetProjectileShooterPosition(projectile, shooter) : new();

        ProjectileCollisionCheckRequest packet = new()
        {
            ProjectileId = projectile.ProjectileId,
            ProjectileEntityId = projectile.EntityId,
            CurrentPosition = [projectile.ServerPos.X, projectile.ServerPos.Y, projectile.ServerPos.Z],
            PreviousPosition = [projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z],
            Velocity = [projectile.PreviousVelocity.X, projectile.PreviousVelocity.Y, projectile.PreviousVelocity.Z],
            ShooterPosition = gateAgainstShooterBackHits ? [shooterPosition.X, shooterPosition.Y, shooterPosition.Z] : Array.Empty<double>(),
            ShotDirection = gateAgainstShooterBackHits && shotDirection.LengthSquared > 1e-8 ? [shotDirection.X, shotDirection.Y, shotDirection.Z] : Array.Empty<double>(),
            Radius = projectile.ColliderRadius,
            PenetrationDistance = projectile.PenetrationDistance + PenetrationDistanceOffset,
            PenetrationStrength = projectile.PenetrationStrength,
            CollideWithShooter = false,
            IgnoreEntities = ignoredEntities,
            CandidateEntityIds = candidateEntityIds,
            PacketVersion = projectile.ServerProjectile?.PacketVersion ?? 0,
            ShooterId = projectile.ShooterId,
            CheckAABBOnly = checkAABBOnly,
            RequireColliderWhenAvailable = !checkAABBOnly && IsBodyPartRangedProjectile(projectile, isFirearmsProjectile)
        };

        IServerPlayer? player = (_api.World.GetEntityById(projectile.OwnerId) as EntityPlayer)?.Player as IServerPlayer;

        if (player != null) _serverChannel.SendPacket(packet, player);
    }
    public void OnDealDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage)
    {
        OnDealDamage(target, damageSource, weaponStack, null, ref damage);
    }

    public void OnDealDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ItemStack? projectileStack, ref float damage)
    {
        OnDealDamage(target, damageSource, weaponStack, projectileStack, ref damage, applyBuffs: true);
    }

    internal void OnDealDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ItemStack? projectileStack, ref float damage, bool applyBuffs)
    {
        OnDealRangedDamage?.Invoke(target, damageSource, weaponStack, ref damage);
        if (applyBuffs)
        {
            WeaponBuffSystem.ModifyRangedDamage(target, damageSource, weaponStack, projectileStack, ref damage);
        }
        damage = GrindingWheelCompat.ApplyBuffableDamage(weaponStack, target, damage, damageSource);
    }

    internal void EmitRangedDamageResolved(Entity target, DamageSource damageSource, ItemStack? weaponStack, ItemStack? projectileStack, float damage, bool damageReceived, DamageBlockEventArgs? block)
    {
        OnRangedDamageResolved?.Invoke(new(target, damageSource, weaponStack, projectileStack, damage, damageReceived, block));
    }

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _serverChannel;
    private readonly Dictionary<Guid, ProjectileServer> _projectiles = new();
    private readonly Dictionary<Guid, LateProjectileCollision> _lateProjectiles = new();
    private readonly Dictionary<string, int> _firearmsCollisionRejectionCounts = new();
    private readonly List<(long EntityId, double Distance)> _projectileCandidateScratch = new(MaxFirearmsProjectileCollisionCandidates);
    private readonly List<Guid> _lateProjectileRemovalScratch = new();
    private const float _nearestPlayerSearchRange = 300;
    private const float FirearmsProjectileCandidatePadding = 4f;
    private const int MaxFirearmsProjectileCollisionCandidates = 16;
    private const int LateCollisionGraceMs = 1500;
    private const int MaxLateProjectileCacheSize = 4096;
    private const int FirearmsCollisionDiagnosticsIntervalMs = 10000;
    private const double BehindShooterTolerance = -0.25;
    private long _nextFirearmsCollisionDiagnosticsAtMs;

    private long[] GetProjectileCollisionCandidateEntityIds(ProjectileEntity projectile)
    {
        Vector3d currentPosition = new(projectile.ServerPos.X, projectile.ServerPos.Y, projectile.ServerPos.Z);
        Vector3d previousPosition = new(projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z);
        Vector3d segment = currentPosition - previousPosition;

        if (segment.LengthSquared <= 0)
        {
            return Array.Empty<long>();
        }

        Vec3d midPoint = new(
            segment.X / 2f + previousPosition.X,
            segment.Y / 2f + previousPosition.Y,
            segment.Z / 2f + previousPosition.Z);

        double searchRadius = segment.Length / 2f + projectile.ColliderRadius + FirearmsProjectileCandidatePadding;
        float candidateRadius = projectile.ColliderRadius + FirearmsProjectileCandidatePadding;
        _projectileCandidateScratch.Clear();

        Entity[] entities = _api.World.GetEntitiesAround(midPoint, (float)searchRadius, (float)searchRadius);
        foreach (Entity entity in entities)
        {
            if (!CanProjectileHit(entity)) continue;
            if (ShouldIgnoreProjectileCandidate(projectile, entity)) continue;

            if (!CheckEntityAabb(entity, currentPosition, previousPosition, candidateRadius, 0, out Vector3d point, out _, useServerPosition: true))
            {
                continue;
            }

            double distance = (point - previousPosition).LengthSquared;
            AddProjectileCandidate(entity.EntityId, distance);
        }

        if (_projectileCandidateScratch.Count == 0)
        {
            return Array.Empty<long>();
        }

        int count = _projectileCandidateScratch.Count;
        long[] result = new long[count];
        for (int index = 0; index < count; index++)
        {
            result[index] = _projectileCandidateScratch[index].EntityId;
        }

        return result;
    }

    private void AddProjectileCandidate(long entityId, double distance)
    {
        int insertIndex = _projectileCandidateScratch.Count;
        while (insertIndex > 0 && distance < _projectileCandidateScratch[insertIndex - 1].Distance)
        {
            insertIndex--;
        }

        if (insertIndex >= MaxFirearmsProjectileCollisionCandidates)
        {
            return;
        }

        _projectileCandidateScratch.Insert(insertIndex, (entityId, distance));
        if (_projectileCandidateScratch.Count > MaxFirearmsProjectileCollisionCandidates)
        {
            _projectileCandidateScratch.RemoveAt(_projectileCandidateScratch.Count - 1);
        }
    }

    private static bool ShouldIgnoreProjectileCandidate(ProjectileEntity projectile, Entity entity)
    {
        if (entity.EntityId == projectile.EntityId) return true;
        if (entity.EntityId == projectile.ShooterId) return true;
        if (projectile.CollidedWith.Contains(entity.EntityId)) return true;

        return false;
    }

    private static bool CheckAABBOnly(ICoreAPI api, ProjectileEntity projectile)
    {
        if (api.World.GetEntityById(projectile.ShooterId) is not EntityPlayer)
        {
            return true;
        }

        return false;
    }

    private static bool IsFirearmsProjectile(ProjectileEntity projectile)
    {
        if (projectile.Code?.Domain == "maltiezfirearms")
        {
            return true;
        }

        return IsFirearmsCollectible(projectile.WeaponStack?.Collectible) ||
            IsFirearmsCollectible(projectile.ProjectileStack?.Collectible);
    }

    private static bool IsFirearmsCollectible(CollectibleObject? collectible)
    {
        if (collectible == null)
        {
            return false;
        }

        if (collectible.Code?.Domain == "maltiezfirearms")
        {
            return true;
        }

        return CollectibleClassifier.IsFirearm(collectible);
    }

    private static bool IsBodyPartRangedProjectile(ProjectileEntity projectile, bool isFirearmsProjectile)
    {
        if (isFirearmsProjectile) return true;

        ItemStack? weaponStack = projectile.WeaponStack;
        return CollectibleClassifier.IsBow(weaponStack) ||
            CollectibleClassifier.IsCrossbow(weaponStack);
    }

    private static bool CanProjectileHit(Entity entity)
    {
        if (!entity.Alive) return false;

        return entity.IsCreature || entity.GetBehavior<EntityBehaviorHealth>() != null;
    }

    private static bool CheckEntityAabb(Entity target, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationResistance, out Vector3d point, out float penetrationStrengthLoss, bool useServerPosition = false)
    {
        return CheckCollisionBox(GetCollisionBox(target, useServerPosition), currentPosition, previousPosition, radius, penetrationResistance, out point, out penetrationStrengthLoss);
    }

    private static bool CheckCollisionBox(CuboidAABBCollider collisionBox, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationResistance, out Vector3d point, out float penetrationStrengthLoss)
    {
        point = new();
        penetrationStrengthLoss = 0;

        if (!collisionBox.Collide(currentPosition, previousPosition, radius, out point)) return false;

        penetrationStrengthLoss = penetrationResistance;
        return true;
    }

    private static CuboidAABBCollider GetCollisionBox(Entity entity, bool useServerPosition)
    {
        Cuboidf collisionBox = entity.CollisionBox;
        EntityPos position = useServerPosition ? entity.ServerPos : entity.Pos;
        return new(
            new Vector3d(collisionBox.X1 + position.X, collisionBox.Y1 + position.Y, collisionBox.Z1 + position.Z),
            new Vector3d(collisionBox.X2 + position.X, collisionBox.Y2 + position.Y, collisionBox.Z2 + position.Z)
        );
    }
    private static void SpawnProjectile(Guid id, ItemStack projectileStack, ItemStack? weaponStack, ProjectileStats stats, ProjectileSpawnStats spawnStats, ICoreAPI api, Entity shooter, Entity owner, out ProjectileEntity? projectile)
    {
        AssetLocation entityTypeAsset = new(stats.EntityCode);

        EntityProperties? entityType = api.World.GetEntityType(entityTypeAsset) ?? throw new InvalidOperationException($"[Overhaul lib] Unable to create entity '{entityTypeAsset}'");

        Entity entity = api.ClassRegistry.CreateEntity(entityType) ?? throw new InvalidOperationException($"[Overhaul lib] Unable to create entity '{entityTypeAsset}'");

        entity.ServerPos.SetPos(new Vec3d(spawnStats.Position.X, spawnStats.Position.Y, spawnStats.Position.Z));
        entity.ServerPos.Motion.Set(new Vec3d(spawnStats.Velocity.X, spawnStats.Velocity.Y, spawnStats.Velocity.Z));
        entity.Pos.SetFrom(entity.ServerPos);
        entity.World = api.World;

        projectile = entity as ProjectileEntity;
        if (projectile != null)
        {
            projectile.ProjectileId = id;
            projectile.ProjectileStack = projectileStack;
            projectile.WeaponStack = weaponStack;
            projectile.DropOnImpactChance = stats.DropChance;
            projectile.ColliderRadius = stats.CollisionRadius;
            projectile.PenetrationDistance = stats.PenetrationDistance;
            projectile.PenetrationStrength = Math.Max(0, stats.PenetrationBonus + spawnStats.DamageTier);
            projectile.DurabilityDamageOnImpact = stats.DurabilityDamage;
            projectile.ShooterId = shooter.EntityId;
            projectile.OwnerId = owner.EntityId;
            projectile.ShooterPosition.Set(shooter.ServerPos.X, shooter.ServerPos.Y, shooter.ServerPos.Z);
            projectile.CanBeCollected = stats.CanBeCollected;
            projectile.IgnoreInvFrames = true;

            projectile.SetRotation();
        }
        else if (entity is IProjectile vanillaProjectile)
        {
            vanillaProjectile.FiredBy = shooter;
            vanillaProjectile.Damage = stats.DamageStats.Damage * spawnStats.DamageMultiplier;
            vanillaProjectile.DamageTier = spawnStats.DamageTier + stats.DamageTierBonus;
            vanillaProjectile.ProjectileStack = projectileStack;
            vanillaProjectile.WeaponStack = weaponStack;
            vanillaProjectile.DropOnImpactChance = stats.DropChance;
            vanillaProjectile.DamageStackOnImpact = stats.DurabilityDamage > 0;
            vanillaProjectile.Weight = entity.Properties.Weight;
            vanillaProjectile.IgnoreInvFrames = true;
            vanillaProjectile.PreInitialize();
        }

        api.World.SpawnEntity(entity);

        float gravityFactorMultiplier = spawnStats.GravityFactorMultiplier <= 0 ? 1f : spawnStats.GravityFactorMultiplier;
        if (Math.Abs(gravityFactorMultiplier - 1f) > 0.0001f &&
            entity.GetBehavior<EntityBehaviorPassivePhysics>() is { } physics)
        {
            physics.GravityPerSecond *= gravityFactorMultiplier;
        }
    }
    private sealed class LateProjectileCollision
    {
        public LateProjectileCollision(ProjectileServer projectile, int packetVersion, long expiresAtMs)
        {
            Projectile = projectile;
            PacketVersion = packetVersion;
            ExpiresAtMs = expiresAtMs;
        }

        public ProjectileServer Projectile { get; }
        public int PacketVersion { get; }
        public long ExpiresAtMs { get; }
    }

    private void ClearId(Guid id)
    {
        PruneLateProjectiles();

        if (_projectiles.TryGetValue(id, out ProjectileServer? projectileServer) && IsFirearmsProjectile(projectileServer._entity))
        {
            TrimLateProjectileCacheForInsert();
            _lateProjectiles[id] = new(projectileServer, projectileServer.PacketVersion, _api.World.ElapsedMilliseconds + LateCollisionGraceMs);
        }

        _projectiles.Remove(id);
    }
    private void HandleCollision(IServerPlayer player, ProjectileCollisionPacket packet)
    {
        PruneLateProjectiles();

        if (_projectiles.TryGetValue(packet.Id, out ProjectileServer? projectileServer))
        {
            if (!IsValidCollisionPacket(player, projectileServer, packet, projectileServer.PacketVersion, out string rejectionReason))
            {
                RecordFirearmsCollisionRejection(projectileServer, rejectionReason);
                return;
            }

            projectileServer.PacketVersion++;

            projectileServer._entity.CollidedWith.Add(packet.ReceiverEntity);
            projectileServer._entity.Stuck = false;
            projectileServer._entity.CollidedVertically = false;
            projectileServer._entity.CollidedHorizontally = false;
            projectileServer.OnCollision(packet);
            return;
        }

        if (!_lateProjectiles.TryGetValue(packet.Id, out LateProjectileCollision? lateCollision))
        {
            return;
        }

        if (_api.World.ElapsedMilliseconds > lateCollision.ExpiresAtMs)
        {
            RecordFirearmsCollisionRejection(lateCollision.Projectile, "expired-late-hit");
            _lateProjectiles.Remove(packet.Id);
            return;
        }

        if (!IsValidCollisionPacket(player, lateCollision.Projectile, packet, lateCollision.PacketVersion, out string lateRejectionReason))
        {
            RecordFirearmsCollisionRejection(lateCollision.Projectile, lateRejectionReason);
            return;
        }

        _lateProjectiles.Remove(packet.Id);
        lateCollision.Projectile.OnLateCollision(packet);
    }

    private void PruneLateProjectiles()
    {
        if (_lateProjectiles.Count == 0)
        {
            return;
        }

        long now = _api.World.ElapsedMilliseconds;
        _lateProjectileRemovalScratch.Clear();
        foreach ((Guid id, LateProjectileCollision lateCollision) in _lateProjectiles)
        {
            if (now > lateCollision.ExpiresAtMs)
            {
                _lateProjectileRemovalScratch.Add(id);
            }
        }

        foreach (Guid id in _lateProjectileRemovalScratch)
        {
            _lateProjectiles.Remove(id);
        }
    }

    private void TrimLateProjectileCacheForInsert()
    {
        int overflow = _lateProjectiles.Count - MaxLateProjectileCacheSize + 1;
        if (overflow <= 0)
        {
            return;
        }

        _lateProjectileRemovalScratch.Clear();
        for (int removeIndex = 0; removeIndex < overflow; removeIndex++)
        {
            Guid oldestId = default;
            long oldestExpiresAt = long.MaxValue;
            foreach ((Guid id, LateProjectileCollision lateCollision) in _lateProjectiles)
            {
                if (_lateProjectileRemovalScratch.Contains(id)) continue;
                if (lateCollision.ExpiresAtMs >= oldestExpiresAt) continue;

                oldestId = id;
                oldestExpiresAt = lateCollision.ExpiresAtMs;
            }

            if (oldestExpiresAt == long.MaxValue) break;
            _lateProjectileRemovalScratch.Add(oldestId);
        }

        foreach (Guid id in _lateProjectileRemovalScratch)
        {
            _lateProjectiles.Remove(id);
        }
    }

    private void RecordFirearmsCollisionRejection(ProjectileServer projectileServer, string reason)
    {
        if (!IsFirearmsProjectile(projectileServer._entity))
        {
            return;
        }

        _firearmsCollisionRejectionCounts.TryGetValue(reason, out int count);
        _firearmsCollisionRejectionCounts[reason] = count + 1;

        FlushFirearmsCollisionRejectionDiagnostics();
    }

    private void FlushFirearmsCollisionRejectionDiagnostics()
    {
        long now = _api.World.ElapsedMilliseconds;
        if (_nextFirearmsCollisionDiagnosticsAtMs == 0)
        {
            _nextFirearmsCollisionDiagnosticsAtMs = now + FirearmsCollisionDiagnosticsIntervalMs;
            return;
        }

        if (now < _nextFirearmsCollisionDiagnosticsAtMs || _firearmsCollisionRejectionCounts.Count == 0)
        {
            return;
        }

        string counts = string.Join(", ", _firearmsCollisionRejectionCounts.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key}={entry.Value}"));
        LoggerUtil.Verbose(_api, typeof(ProjectileSystemServer), $"Firearms projectile collision rejection counts: {counts}");

        _firearmsCollisionRejectionCounts.Clear();
        _nextFirearmsCollisionDiagnosticsAtMs = now + FirearmsCollisionDiagnosticsIntervalMs;
    }

    private bool IsValidCollisionPacket(IServerPlayer player, ProjectileServer projectileServer, ProjectileCollisionPacket packet, int expectedPacketVersion, out string rejectionReason)
    {
        rejectionReason = "";

        if (packet.PacketVersion != expectedPacketVersion)
        {
            rejectionReason = "wrong-packet-version";
            return false;
        }

        if (!IsPacketSenderOwner(player, projectileServer))
        {
            rejectionReason = "wrong-owner";
            return false;
        }

        Entity? target = _api.World.GetEntityById(packet.ReceiverEntity);
        if (target == null || !CanProjectileHit(target))
        {
            rejectionReason = "invalid-target";
            return false;
        }

        if (target.EntityId == projectileServer._entity.EntityId ||
            target.EntityId == projectileServer._entity.ShooterId)
        {
            rejectionReason = "self-or-shooter";
            return false;
        }

        if (IsCollisionBehindShooter(projectileServer._entity, packet))
        {
            rejectionReason = "behind-shooter";
            return false;
        }

        return true;
    }

    private bool IsCollisionBehindShooter(ProjectileEntity projectile, ProjectileCollisionPacket packet)
    {
        if (packet.CollisionPoint == null || packet.CollisionPoint.Length < 3) return false;
        if (_api.World.GetEntityById(projectile.ShooterId) is not EntityPlayer) return false;

        Vector3d shotDirection = GetProjectileShotDirection(projectile);
        if (shotDirection.LengthSquared <= 1e-8) return false;

        Vec3d shooterPositionVanilla = GetProjectileShooterPosition(projectile, null);
        Vector3d shooterPosition = new(shooterPositionVanilla.X, shooterPositionVanilla.Y, shooterPositionVanilla.Z);
        Vector3d collisionPoint = new(packet.CollisionPoint[0], packet.CollisionPoint[1], packet.CollisionPoint[2]);
        Vector3d horizontalShotDirection = new(shotDirection.X, 0, shotDirection.Z);
        if (horizontalShotDirection.LengthSquared <= 1e-8) horizontalShotDirection = shotDirection;
        horizontalShotDirection = horizontalShotDirection.Normalized();

        Vector3d toPoint = new(collisionPoint.X - shooterPosition.X, 0, collisionPoint.Z - shooterPosition.Z);
        if (toPoint.LengthSquared <= 1e-8) toPoint = collisionPoint - shooterPosition;

        return Vector3d.Dot(toPoint, horizontalShotDirection) < BehindShooterTolerance;
    }

    private static Vector3d GetProjectileShotDirection(ProjectileEntity projectile)
    {
        Vector3d velocity = new(projectile.PreviousVelocity.X, projectile.PreviousVelocity.Y, projectile.PreviousVelocity.Z);
        if (velocity.LengthSquared <= 1e-8)
        {
            velocity = new(projectile.ServerPos.Motion.X, projectile.ServerPos.Motion.Y, projectile.ServerPos.Motion.Z);
        }
        if (velocity.LengthSquared <= 1e-8)
        {
            velocity = new(projectile.ServerPos.X - projectile.PreviousPosition.X, projectile.ServerPos.Y - projectile.PreviousPosition.Y, projectile.ServerPos.Z - projectile.PreviousPosition.Z);
        }

        return velocity.LengthSquared <= 1e-8 ? Vector3d.Zero : velocity.Normalized();
    }

    private static Vec3d GetProjectileShooterPosition(ProjectileEntity projectile, Entity? shooter)
    {
        if (projectile.ShooterPosition.LengthSq() > 1e-8)
        {
            return projectile.ShooterPosition;
        }

        if (shooter != null)
        {
            return new(shooter.ServerPos.X, shooter.ServerPos.Y, shooter.ServerPos.Z);
        }

        return new(projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z);
    }

    private static bool IsPacketSenderOwner(IServerPlayer player, ProjectileServer projectileServer)
    {
        return player.Entity?.EntityId == projectileServer._entity.OwnerId;
    }
}
