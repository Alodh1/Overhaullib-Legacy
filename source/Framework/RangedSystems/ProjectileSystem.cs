using CombatOverhaul.Colliders;
using CombatOverhaul.DamageSystems;
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

        return new ItemStackProjectileStats(damageMultiplier, damageTierBonus, knockbackMultiplier, dropChanceMultiplier, penetrationBonus, additionalDurabilityCost);
    }
}

public struct ProjectileSpawnStats
{
    public long ProducerEntityId { get; set; }
    public float DamageMultiplier { get; set; }
    public int DamageTier { get; set; }
    public Vector3d Position { get; set; }
    public Vector3d Velocity { get; set; }
    [Obsolete("Use DamageTier instead")]
    public float DamageStrength { get => DamageTier; set => DamageTier = (int)value; } // for compatibility
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
    public float Radius { get; set; }
    public float PenetrationDistance { get; set; }
    public float PenetrationStrength { get; set; }
    public bool CollideWithShooter { get; set; }
    public long[] IgnoreEntities { get; set; } = Array.Empty<long>();
    public int PacketVersion { get; set; }
    public long ShooterId { get; set; }
    public bool CheckAABBOnly { get; set; } = false;
    public bool RequireColliderWhenAvailable { get; set; } = false;
}

public sealed class ProjectileSystemClient
{
    public const string NetworkChannelId = "CombatOverhaul:projectiles";
    private const int DebugInitialPackets = 5;
    private const int DebugPacketInterval = 20;

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
    private readonly Dictionary<Guid, int> _debugPacketCounts = new();

    //private Stopwatch _stopwatch = new();

    private void HandleRequest(ProjectileCollisionCheckRequest packet)
    {
        Vector3d currentPosition = new(packet.CurrentPosition[0], packet.CurrentPosition[1], packet.CurrentPosition[2]);
        Vector3d previousPosition = new(packet.PreviousPosition[0], packet.PreviousPosition[1], packet.PreviousPosition[2]);
        Vector3d velocity = new(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2]);
        Vector3d segment = currentPosition - previousPosition;
        bool debugFirearm = ShouldLogProjectilePacket(_api.World, packet);
        bool logThisPacket = debugFirearm && ShouldLogProjectileDebug(packet.ProjectileId, _debugPacketCounts);

        Vec3d midPoint = new(
            segment.X / 2f + previousPosition.X,
            segment.Y / 2f + previousPosition.Y,
            segment.Z / 2f + previousPosition.Z);

        double searchRadius = Math.Max(
            _combatOverhaulSystem.Settings.CollisionRadius + packet.Radius,
            segment.Length / 2f + _combatOverhaulSystem.Settings.CollisionRadius + packet.Radius);

        Entity[] entities = _api.World.GetEntitiesAround(
            midPoint,
            (float)searchRadius,
            (float)searchRadius);

        if (logThisPacket)
        {
            _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Client check id={packet.ProjectileId} projectileEntity={packet.ProjectileEntityId} shooter={packet.ShooterId} pos={FormatVector(currentPosition)} prev={FormatVector(previousPosition)} segment={segment.Length:F3} radius={packet.Radius:F3} search={searchRadius:F3} entities={entities.Length} candidates={entities.Count(CanProjectileHit)} ignored={packet.IgnoreEntities.Length} aabbOnly={packet.CheckAABBOnly} requireCollider={packet.RequireColliderWhenAvailable}");
        }

        foreach (Entity entity in entities.Where(CanProjectileHit))
        {
            if (Collide(entity, packet, currentPosition, previousPosition, velocity, logThisPacket))
            {
                return;
            }
        }

        if (logThisPacket)
        {
            _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Client no-hit id={packet.ProjectileId}");
        }

        /*_stopwatch.Start();

        for (int i = 0; i < 1000; i++)
        {
            Vector3d currentPosition = new(packet.CurrentPosition[0], packet.CurrentPosition[1], packet.CurrentPosition[2]);
            Vector3d previousPosition = new(packet.PreviousPosition[0], packet.PreviousPosition[1], packet.PreviousPosition[2]);
            Vector3d velocity = new(packet.Velocity[0], packet.Velocity[1], packet.Velocity[2]);

            foreach (Entity entity in entities.Where(entity => entity.IsCreature))
            {
                if (Collide(entity, packet, currentPosition, previousPosition, velocity))
                {
                    break;
                }
            }
        }

        _stopwatch.Stop();
        Console.WriteLine($"{_stopwatch.Elapsed.TotalMicroseconds / 1000:F3} microseconds\t{entities.Length} entities");
        _stopwatch.Reset();*/
    }

    private static bool CanProjectileHit(Entity entity)
    {
        if (!entity.Alive) return false;

        return entity.IsCreature || entity.GetBehavior<EntityBehaviorHealth>() != null;
    }

    private bool Collide(Entity target, ProjectileCollisionCheckRequest packet, Vector3d currentPosition, Vector3d previousPosition, Vector3d velocity, bool debugLog = false)
    {
        if (target.EntityId == packet.ProjectileEntityId) return false;

        if (!packet.CollideWithShooter && packet.ShooterId == target.EntityId) return false;

        if (packet.IgnoreEntities.Contains(target.EntityId)) return false;

        if (!CheckCollision(target, out string collider, out Vector3d point, currentPosition, previousPosition, packet.Radius, packet.PenetrationDistance, packet.PenetrationStrength, out float penetrationStrengthLoss, packet.CheckAABBOnly, packet.RequireColliderWhenAvailable, out string collisionMode, out string missReason))
        {
            if (debugLog)
            {
                _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Client candidate miss id={packet.ProjectileId} target={target.Code}#{target.EntityId} mode={collisionMode} hasColliders={target.GetBehavior<CollidersEntityBehavior>() != null} aabbOnly={packet.CheckAABBOnly} reason={missReason}");
            }

            return false;
        }

        Vector3d targetVelocity = new((float)target.Pos.Motion.X, (float)target.Pos.Motion.Y, (float)target.Pos.Motion.Z);

        double relativeSpeed = (targetVelocity - velocity).Length;

        if (debugLog)
        {
            string colliderType = GetColliderTypeName(target, collider);
            _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Client hit id={packet.ProjectileId} target={target.Code}#{target.EntityId} mode={collisionMode} collider='{collider}' colliderType='{colliderType}' point={FormatVector(point)} relativeSpeed={relativeSpeed:F3} penetrationLoss={penetrationStrengthLoss:F3}");
        }

        Collide(packet.ProjectileId, target, point, targetVelocity, relativeSpeed, collider, packet, penetrationStrengthLoss);

        return true;
    }

    private static bool ShouldLogProjectilePacket(IWorldAccessor world, ProjectileCollisionCheckRequest packet)
    {
        return (packet.CheckAABBOnly || packet.RequireColliderWhenAvailable) && world.GetEntityById(packet.ShooterId) is EntityPlayer;
    }

    private static bool ShouldLogProjectileDebug(Guid projectileId, Dictionary<Guid, int> packetCounts)
    {
        packetCounts.TryGetValue(projectileId, out int count);
        count++;
        packetCounts[projectileId] = count;

        return count <= DebugInitialPackets || count % DebugPacketInterval == 0;
    }

    private static string FormatVector(Vector3d vector)
    {
        return $"{vector.X:F2},{vector.Y:F2},{vector.Z:F2}";
    }
    private bool CheckCollision(Entity target, out string collider, out Vector3d point, Vector3d currentPosition, Vector3d previousPosition, float radius, float penetrationDistance, float penetrationSrength, out float penetrationStrengthLoss, bool checkAABBOnly, bool requireColliderWhenAvailable, out string collisionMode, out string missReason)
    {
        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        EntityDamageModelBehavior? damageModel = target.GetBehavior<EntityDamageModelBehavior>();
        PlayerDamageModelBehavior? playerDamageModel = target.GetBehavior<PlayerDamageModelBehavior>();
        collider = "";
        point = new();
        penetrationStrengthLoss = 0;
        collisionMode = colliders == null ? "AABBNoColliders" : checkAABBOnly ? "AABBOnly" : "CO";
        missReason = "";
        float defaultColliderPenetrationResistance = _combatOverhaulSystem.Settings.DefaultColliderPenetrationResistance;
        bool requireDetailedCollider = requireColliderWhenAvailable && colliders != null && !checkAABBOnly;

        bool aabbHit = CheckEntityAabb(target, currentPosition, previousPosition, radius, defaultColliderPenetrationResistance, out Vector3d aabbPoint, out float aabbPenetrationStrengthLoss);

        if (colliders == null || checkAABBOnly)
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

            List<ColliderTypes> encounteredColliderTypes = new();

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

                if (!encounteredColliderTypes.Contains(colliderType))
                {
                    penetrationStrengthLoss += penetrationResistance;
                    encounteredColliderTypes.Add(colliderType);
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

            List<PlayerBodyPart> encounteredColliderTypes = new();

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

                if (!encounteredColliderTypes.Contains(bodyType))
                {
                    penetrationStrengthLoss += penetrationResistance;
                    encounteredColliderTypes.Add(bodyType);
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

    internal static string GetColliderTypeName(Entity target, string collider)
    {
        if (string.IsNullOrEmpty(collider)) return "";

        CollidersEntityBehavior? colliders = target.GetBehavior<CollidersEntityBehavior>();
        if (colliders != null && colliders.CollidersTypes.TryGetValue(collider, out ColliderTypes colliderType))
        {
            return colliderType.ToString();
        }

        return "Unknown";
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
        Cuboidf collisionBox = entity.CollisionBox.Clone(); // @TODO: Refactor to not clone
        EntityPos position = entity.Pos;
        collisionBox.X1 += (float)position.X;
        collisionBox.Y1 += (float)position.Y;
        collisionBox.Z1 += (float)position.Z;
        collisionBox.X2 += (float)position.X;
        collisionBox.Y2 += (float)position.Y;
        collisionBox.Z2 += (float)position.Z;
        return new(collisionBox);
    }
}

public sealed class ProjectileSystemServer
{
    public delegate void RangedDamageDelegate(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage);
    private const int DebugInitialPackets = 5;
    private const int DebugPacketInterval = 20;

    public event RangedDamageDelegate? OnDealRangedDamage;

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
        Spawn(id, projectileStats, spawnStats, projectileStack, weaponStack, shooter, shooter);
    }

    public void Spawn(Guid id, ProjectileStats projectileStats, ProjectileSpawnStats spawnStats, ItemStack projectileStack, ItemStack? weaponStack, Entity shooter, Entity target)
    {
        EntityPlayer? owner = (shooter as EntityPlayer) ??
            (target as EntityPlayer) ??
            _api.World.GetNearestEntity(target.Pos.XYZ, _nearestPlayerSearchRange, _nearestPlayerSearchRange, entity => entity is EntityPlayer) as EntityPlayer;

        if (owner == null)
        {
            return;
        }

        SpawnProjectile(id, projectileStack, weaponStack, projectileStats, spawnStats, _api, shooter, owner, out ProjectileEntity? projectile);

        if (projectile != null)
        {
            _projectiles.Add(id, new(projectile, projectileStats, spawnStats, _api, ClearId, projectileStack));
            projectile.ServerProjectile = _projectiles[id];
        }
    }
    public void TryCollide(ProjectileEntity projectile)
    {
        bool isFirearmsProjectile = IsFirearmsProjectile(projectile);
        ProjectileCollisionCheckRequest packet = new()
        {
            ProjectileId = projectile.ProjectileId,
            ProjectileEntityId = projectile.EntityId,
            CurrentPosition = [projectile.ServerPos.X, projectile.ServerPos.Y, projectile.ServerPos.Z],
            PreviousPosition = [projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z],
            Velocity = [projectile.PreviousVelocity.X, projectile.PreviousVelocity.Y, projectile.PreviousVelocity.Z],
            Radius = projectile.ColliderRadius,
            PenetrationDistance = projectile.PenetrationDistance + PenetrationDistanceOffset,
            PenetrationStrength = projectile.PenetrationStrength,
            CollideWithShooter = false,
            IgnoreEntities = projectile.CollidedWith.ToArray(),
            PacketVersion = projectile.ServerProjectile?.PacketVersion ?? 0,
            ShooterId = projectile.ShooterId,
            CheckAABBOnly = CheckAABBOnly(_api, projectile),
            RequireColliderWhenAvailable = isFirearmsProjectile
        };

        IServerPlayer? player = (_api.World.GetEntityById(projectile.OwnerId) as EntityPlayer)?.Player as IServerPlayer;

        if (isFirearmsProjectile && ShouldLogProjectileDebug(projectile.ProjectileId, _debugPacketCounts))
        {
            _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Server request id={projectile.ProjectileId} entity={projectile.EntityId} code={projectile.Code} shooter={projectile.ShooterId} owner={projectile.OwnerId} ownerPlayer={(player != null ? player.PlayerName : "<none>")} weapon={projectile.WeaponStack?.Collectible?.Code} ammo={projectile.ProjectileStack?.Collectible?.Code} pos={FormatVector(new(projectile.ServerPos.X, projectile.ServerPos.Y, projectile.ServerPos.Z))} prev={FormatVector(new(projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z))} radius={projectile.ColliderRadius:F3} penDist={projectile.PenetrationDistance:F3} penStrength={projectile.PenetrationStrength:F3} aabbOnly={packet.CheckAABBOnly} requireCollider={packet.RequireColliderWhenAvailable} ignored={packet.IgnoreEntities.Length} version={packet.PacketVersion}");
        }

        if (player != null) _serverChannel.SendPacket(packet, player);
        else if (isFirearmsProjectile)
        {
            _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Server no owner client for id={projectile.ProjectileId} entity={projectile.EntityId} owner={projectile.OwnerId}; collision request not sent");
        }
    }
    public void OnDealDamage(Entity target, DamageSource damageSource, ItemStack? weaponStack, ref float damage)
    {
        OnDealRangedDamage?.Invoke(target, damageSource, weaponStack, ref damage);
    }

    private readonly ICoreServerAPI _api;
    private readonly IServerNetworkChannel _serverChannel;
    private readonly Dictionary<Guid, ProjectileServer> _projectiles = new();
    private readonly Dictionary<Guid, int> _debugPacketCounts = new();
    private const float _nearestPlayerSearchRange = 300;

    private void TryCollideServerAabb(ProjectileEntity projectile)
    {
        if (!_projectiles.TryGetValue(projectile.ProjectileId, out ProjectileServer? projectileServer))
        {
            return;
        }

        Vector3d currentPosition = new(projectile.ServerPos.X, projectile.ServerPos.Y, projectile.ServerPos.Z);
        Vector3d previousPosition = new(projectile.PreviousPosition.X, projectile.PreviousPosition.Y, projectile.PreviousPosition.Z);
        Vector3d velocity = new(projectile.PreviousVelocity.X, projectile.PreviousVelocity.Y, projectile.PreviousVelocity.Z);
        Vector3d segment = currentPosition - previousPosition;

        if (segment.LengthSquared <= 0)
        {
            return;
        }

        Vec3d midPoint = new(
            segment.X / 2f + previousPosition.X,
            segment.Y / 2f + previousPosition.Y,
            segment.Z / 2f + previousPosition.Z);

        Settings settings = _api.ModLoader.GetModSystem<CombatOverhaulSystem>().Settings;
        double searchRadius = Math.Max(
            settings.CollisionRadius + projectile.ColliderRadius,
            segment.Length / 2f + settings.CollisionRadius + projectile.ColliderRadius);

        Entity? closestTarget = null;
        Vector3d closestPoint = new();
        float closestPenetrationStrengthLoss = 0;
        double closestDistance = double.MaxValue;

        foreach (Entity entity in _api.World.GetEntitiesAround(midPoint, (float)searchRadius, (float)searchRadius).Where(CanProjectileHit))
        {
            if (entity.EntityId == projectile.EntityId) continue;
            if (entity.EntityId == projectile.ShooterId) continue;
            if (projectile.CollidedWith.Contains(entity.EntityId)) continue;

            if (!CheckEntityAabb(entity, currentPosition, previousPosition, projectile.ColliderRadius, settings.DefaultColliderPenetrationResistance, out Vector3d point, out float penetrationStrengthLoss, useServerPosition: true))
            {
                continue;
            }

            double distance = (point - previousPosition).LengthSquared;
            if (distance >= closestDistance)
            {
                continue;
            }

            closestTarget = entity;
            closestPoint = point;
            closestPenetrationStrengthLoss = penetrationStrengthLoss;
            closestDistance = distance;
        }

        if (closestTarget == null)
        {
            return;
        }

        Vector3d targetVelocity = new(closestTarget.ServerPos.Motion.X, closestTarget.ServerPos.Motion.Y, closestTarget.ServerPos.Motion.Z);
        ProjectileCollisionPacket packet = new()
        {
            Id = projectile.ProjectileId,
            CollisionPoint = [closestPoint.X, closestPoint.Y, closestPoint.Z],
            AfterCollisionVelocity = [targetVelocity.X, targetVelocity.Y, targetVelocity.Z],
            RelativeSpeed = (targetVelocity - velocity).Length,
            Collider = "",
            ReceiverEntity = closestTarget.EntityId,
            PacketVersion = projectileServer.PacketVersion,
            PenetrationStrengthLoss = closestPenetrationStrengthLoss
        };

        projectileServer.PacketVersion++;
        projectile.CollidedWith.Add(packet.ReceiverEntity);
        projectile.Stuck = false;
        projectile.CollidedVertically = false;
        projectile.CollidedHorizontally = false;
        projectileServer.OnCollision(packet);
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

        Type collectibleType = collectible.GetType();
        if (collectibleType.FullName?.StartsWith("Firearms.", StringComparison.Ordinal) == true)
        {
            return true;
        }

        string? assemblyName = collectibleType.Assembly.GetName().Name;
        return assemblyName?.Contains("Firearms", StringComparison.OrdinalIgnoreCase) == true;
    }

    private static bool ShouldLogProjectileDebug(Guid projectileId, Dictionary<Guid, int> packetCounts)
    {
        packetCounts.TryGetValue(projectileId, out int count);
        count++;
        packetCounts[projectileId] = count;

        return count <= DebugInitialPackets || count % DebugPacketInterval == 0;
    }

    private static string FormatVector(Vector3d vector)
    {
        return $"{vector.X:F2},{vector.Y:F2},{vector.Z:F2}";
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
        Cuboidf collisionBox = entity.CollisionBox.Clone();
        EntityPos position = useServerPosition ? entity.ServerPos : entity.Pos;
        collisionBox.X1 += (float)position.X;
        collisionBox.Y1 += (float)position.Y;
        collisionBox.Z1 += (float)position.Z;
        collisionBox.X2 += (float)position.X;
        collisionBox.Y2 += (float)position.Y;
        collisionBox.Z2 += (float)position.Z;
        return new(collisionBox);
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
    }
    private void ClearId(Guid id) => _projectiles.Remove(id);
    private void HandleCollision(IServerPlayer player, ProjectileCollisionPacket packet)
    {
        if (_projectiles.TryGetValue(packet.Id, out ProjectileServer? projectileServer))
        {
            bool isFirearmsProjectile = IsFirearmsProjectile(projectileServer._entity);
            if (projectileServer.PacketVersion != packet.PacketVersion)
            {
                if (isFirearmsProjectile)
                {
                    _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Server rejected stale collision id={packet.Id} from={player.PlayerName} packetVersion={packet.PacketVersion} expected={projectileServer.PacketVersion} target={packet.ReceiverEntity}");
                }

                return;
            }
            projectileServer.PacketVersion++;

            projectileServer._entity.CollidedWith.Add(packet.ReceiverEntity);
            projectileServer._entity.Stuck = false;
            projectileServer._entity.CollidedVertically = false;
            projectileServer._entity.CollidedHorizontally = false;
            if (isFirearmsProjectile)
            {
                Entity? receiver = _api.World.GetEntityById(packet.ReceiverEntity);
                string targetInfo = receiver != null ? $"{receiver.Code}#{receiver.EntityId}" : packet.ReceiverEntity.ToString();
                string mode = receiver?.GetBehavior<CollidersEntityBehavior>() == null ? "AABBNoColliders" : "CO";
                string colliderType = receiver != null ? ProjectileSystemClient.GetColliderTypeName(receiver, packet.Collider) : "";
                _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Server accepted collision id={packet.Id} from={player.PlayerName} target={targetInfo} mode={mode} collider='{packet.Collider}' colliderType='{colliderType}' point={string.Join(",", packet.CollisionPoint.Select(value => value.ToString("F2")))} relativeSpeed={packet.RelativeSpeed:F3} penLoss={packet.PenetrationStrengthLoss:F3}");
            }
            projectileServer.OnCollision(packet);
        }
        else
        {
            _api.Logger.VerboseDebug($"[OverhaulLib:FirearmsProjectile] Server received collision for unknown projectile id={packet.Id} from={player.PlayerName} target={packet.ReceiverEntity} packetVersion={packet.PacketVersion}");
        }
    }
}
