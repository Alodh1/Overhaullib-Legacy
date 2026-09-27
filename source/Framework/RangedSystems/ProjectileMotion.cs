using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;

namespace CombatOverhaul.RangedSystems;

/// <summary>
/// Supplies stable, server-authoritative projectile state to motion modifiers.
/// Modifiers run in ascending <see cref="IProjectileMotionModifier.Priority"/>
/// order immediately before the normal entity and projectile physics tick.
/// </summary>
public readonly record struct ProjectileMotionContext(
    ProjectileEntity Projectile,
    EntityPos Position,
    float FlightSeconds)
{
    public IWorldAccessor World => Projectile.World;
    public Entity? Shooter => World.GetEntityById(Projectile.ShooterId);
    public Entity? Owner => World.GetEntityById(Projectile.OwnerId);
}

/// <summary>
/// Implement on an entity behavior to adjust a Combat Overhaul projectile's
/// server-side motion without subclassing <see cref="ProjectileEntity"/>.
/// </summary>
public interface IProjectileMotionModifier
{
    int Priority => 0;
    void ModifyMotion(ProjectileMotionContext context, float dt);
}

public sealed class CurvedFlightConfig
{
    /// <summary>Horizontal turn rate in radians per second. Zero disables the outbound curve.</summary>
    public float HorizontalTurnRate { get; set; }

    /// <summary>Positive curves right and negative curves left.</summary>
    public int Direction { get; set; } = 1;

    /// <summary>Delay before horizontal curvature begins.</summary>
    public float StartAfterSeconds { get; set; }

    /// <summary>Negative values disable returning flight.</summary>
    public float ReturnAfterSeconds { get; set; } = -1;

    /// <summary>Acceleration toward the owner during return flight, in blocks per second squared.</summary>
    public float ReturnAcceleration { get; set; } = 12;

    /// <summary>Maximum return speed. Zero preserves the speed at the start of each steering tick.</summary>
    public float MaximumReturnSpeed { get; set; }

    /// <summary>Distance from the owner at which the projectile is returned to their inventory. Zero disables catching.</summary>
    public float CatchRadius { get; set; } = 1.2f;

    /// <summary>Vertical offset from the owner's feet used as the return target.</summary>
    public float ReturnTargetHeight { get; set; } = 1.2f;
}

/// <summary>
/// Optional projectile behavior providing a horizontal arc and an optional
/// owner-seeking return phase. All trajectory and catch decisions are server authoritative.
/// </summary>
public sealed class CurvedFlightBehavior : EntityBehavior, IProjectileMotionModifier
{
    public CurvedFlightBehavior(Entity entity) : base(entity)
    {
    }

    public CurvedFlightConfig Config { get; private set; } = new();
    public int Priority => 100;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        Config = attributes.AsObject<CurvedFlightConfig>() ?? new CurvedFlightConfig();
    }

    public void ModifyMotion(ProjectileMotionContext context, float dt)
    {
        if (dt <= 0 || context.Projectile.Stuck) return;

        bool returning = Config.ReturnAfterSeconds >= 0
            && context.FlightSeconds >= Config.ReturnAfterSeconds;

        if (returning)
        {
            ApplyReturnMotion(context, dt);
            return;
        }

        if (context.FlightSeconds < Math.Max(0, Config.StartAfterSeconds)) return;

        CurvedFlightMath.RotateHorizontal(
            context.Position.Motion,
            Math.Abs(Config.HorizontalTurnRate),
            Config.Direction,
            dt);
    }

    public override string PropertyName() => "combatOverhaulCurvedFlight";

    private void ApplyReturnMotion(ProjectileMotionContext context, float dt)
    {
        Entity? owner = context.Owner;
        if (owner == null || !owner.Alive) return;

        Vec3d target = new(
            owner.ServerPos.X,
            owner.ServerPos.Y + Config.ReturnTargetHeight,
            owner.ServerPos.Z);
        Vec3d toOwner = target.SubCopy(context.Position.XYZ);
        double distance = toOwner.Length();

        if (Config.CatchRadius > 0 && distance <= Config.CatchRadius && ProjectileCatch.TryCatch(context.Projectile, owner))
        {
            return;
        }

        if (distance <= 1e-6) return;

        double currentSpeed = context.Position.Motion.Length();
        double targetSpeed = Config.MaximumReturnSpeed > 0
            ? Config.MaximumReturnSpeed
            : currentSpeed;
        if (targetSpeed <= 1e-6) return;

        Vec3d desiredVelocity = toOwner.Mul(targetSpeed / distance);
        CurvedFlightMath.SteerToward(
            context.Position.Motion,
            desiredVelocity,
            Math.Max(0, Config.ReturnAcceleration) * dt);
    }

}

public sealed class OvalFlightConfig
{
    /// <summary>Maximum forward distance from the launch point, in blocks.</summary>
    public float MaximumDistance { get; set; } = 8;

    /// <summary>Horizontal semi-axis of the oval, in blocks.</summary>
    public float LateralRadius { get; set; } = 5;

    /// <summary>Seconds required to fly the complete oval.</summary>
    public float FlightDurationSeconds { get; set; } = 1.2f;

    /// <summary>Positive starts around the right half of the oval; negative starts left.</summary>
    public int Direction { get; set; } = 1;

    /// <summary>Only attempt an owner catch after this fraction of the loop has elapsed.</summary>
    public float CatchAfterProgress { get; set; } = 0.5f;

    /// <summary>Additional radius applied to the projectile-versus-owner collision sweep.</summary>
    public float CatchCollisionPadding { get; set; } = 0.1f;
}

/// <summary>
/// Drives a projectile around a fixed, launch-relative horizontal oval. The
/// oval never follows the owner: returning the projectile requires its swept
/// path to intersect the owner's collision box.
/// </summary>
public sealed class OvalFlightBehavior : EntityBehavior, IProjectileMotionModifier
{
    private readonly Vec3d _origin = new();
    private double _forwardX;
    private double _forwardZ;
    private double _lateralX;
    private double _lateralZ;
    private float _startFlightSeconds;
    private bool _initialized;

    public OvalFlightBehavior(Entity entity) : base(entity)
    {
    }

    public OvalFlightConfig Config { get; private set; } = new();
    public int Priority => 100;

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        Config = attributes.AsObject<OvalFlightConfig>() ?? new OvalFlightConfig();
    }

    public void ModifyMotion(ProjectileMotionContext context, float dt)
    {
        if (dt <= 0 || context.Projectile.Stuck) return;

        if (!_initialized && !InitializePath(context, dt)) return;

        double duration = Math.Max(0.1, Config.FlightDurationSeconds);
        double progress = GameMath.Clamp(
            (context.FlightSeconds - _startFlightSeconds) / duration,
            0,
            1);
        OvalFlightMath.Sample(Config.MaximumDistance, Config.LateralRadius, progress,
            out double forwardOffset, out double lateralOffset);
        lateralOffset *= Math.Sign(Config.Direction == 0 ? 1 : Config.Direction);

        Vec3d target = new(
            _origin.X + _forwardX * forwardOffset + _lateralX * lateralOffset,
            _origin.Y,
            _origin.Z + _forwardZ * forwardOffset + _lateralZ * lateralOffset);

        if (progress >= GameMath.Clamp(Config.CatchAfterProgress, 0, 1)
            && TryCatchOnOwnerCollision(context, target))
        {
            return;
        }

        if (progress >= 1 && context.Position.XYZ.SquareDistanceTo(target) <= 1e-6)
        {
            context.Position.Motion.Set(0, 0, 0);
            return;
        }

        double dtFactor = dt * 60;
        context.Position.Motion.Set(
            (target.X - context.Position.X) / dtFactor,
            (target.Y - context.Position.Y) / dtFactor,
            (target.Z - context.Position.Z) / dtFactor);
    }

    public override string PropertyName() => "combatOverhaulOvalFlight";

    private bool InitializePath(ProjectileMotionContext context, float dt)
    {
        double horizontalLength = Math.Sqrt(
            context.Position.Motion.X * context.Position.Motion.X
            + context.Position.Motion.Z * context.Position.Motion.Z);
        if (horizontalLength <= 1e-8) return false;

        _origin.Set(context.Position.X, context.Position.Y, context.Position.Z);
        _forwardX = context.Position.Motion.X / horizontalLength;
        _forwardZ = context.Position.Motion.Z / horizontalLength;
        _lateralX = _forwardZ;
        _lateralZ = -_forwardX;
        _startFlightSeconds = Math.Max(0, context.FlightSeconds - dt);
        _initialized = true;
        return true;
    }

    private bool TryCatchOnOwnerCollision(ProjectileMotionContext context, Vec3d target)
    {
        Entity? owner = context.Owner;
        if (owner == null || !owner.Alive) return false;

        Cuboidf box = owner.CollisionBox;
        EntityPos ownerPosition = owner.Pos;
        double padding = Math.Max(0, context.Projectile.ColliderRadius + Config.CatchCollisionPadding);
        bool intersects = OvalFlightMath.SegmentIntersectsAabb(
            context.Position.XYZ,
            target,
            box.X1 + ownerPosition.X - padding,
            box.Y1 + ownerPosition.Y - padding,
            box.Z1 + ownerPosition.Z - padding,
            box.X2 + ownerPosition.X + padding,
            box.Y2 + ownerPosition.Y + padding,
            box.Z2 + ownerPosition.Z + padding);

        return intersects && ProjectileCatch.TryCatch(context.Projectile, owner);
    }
}

public sealed class AutoAnimationConfig
{
    public string Animation { get; set; } = "";
    public bool StopWhenStuck { get; set; } = true;
    public float MinimumSpeed { get; set; }
}

/// <summary>
/// Starts a configured client-side entity animation, including on base Entity
/// types which do not process EntityAgent default-animation triggers.
/// </summary>
public sealed class AutoAnimationBehavior : EntityBehavior
{
    private bool _started;

    public AutoAnimationBehavior(Entity entity) : base(entity)
    {
    }

    public AutoAnimationConfig Config { get; private set; } = new();

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        base.Initialize(properties, attributes);
        Config = attributes.AsObject<AutoAnimationConfig>() ?? new AutoAnimationConfig();
    }

    public override void OnTesselated()
    {
        TryUpdateAnimation();
    }

    public override void OnGameTick(float deltaTime)
    {
        TryUpdateAnimation();
    }

    public override string PropertyName() => "combatOverhaulAutoAnimation";

    private void TryUpdateAnimation()
    {
        if (entity.Api.Side != EnumAppSide.Client || string.IsNullOrWhiteSpace(Config.Animation)) return;

        bool shouldStop = Config.StopWhenStuck
            && entity is ProjectileEntity { Stuck: true };
        shouldStop |= Config.MinimumSpeed > 0
            && entity.SidedPos.Motion.Length() < Config.MinimumSpeed;

        if (shouldStop)
        {
            if (_started) entity.AnimManager.StopAnimation(Config.Animation);
            _started = false;
            return;
        }

        if (!_started)
        {
            _started = entity.AnimManager.StartAnimation(Config.Animation);
        }
    }
}

internal static class CurvedFlightMath
{
    internal static void RotateHorizontal(Vec3d motion, float turnRate, int direction, float dt)
    {
        if (turnRate <= 0 || direction == 0 || dt <= 0) return;

        double horizontalSpeedSquared = motion.X * motion.X + motion.Z * motion.Z;
        if (horizontalSpeedSquared <= 1e-12) return;

        double angle = turnRate * Math.Sign(direction) * dt;
        double cosine = Math.Cos(angle);
        double sine = Math.Sin(angle);
        double x = motion.X;
        double z = motion.Z;

        motion.X = x * cosine + z * sine;
        motion.Z = -x * sine + z * cosine;
    }

    internal static void SteerToward(Vec3d motion, Vec3d desiredVelocity, double maximumDelta)
    {
        if (maximumDelta <= 0) return;

        Vec3d delta = desiredVelocity.SubCopy(motion);
        double deltaLength = delta.Length();
        if (deltaLength <= 1e-12) return;

        if (deltaLength > maximumDelta)
        {
            delta.Mul(maximumDelta / deltaLength);
        }

        motion.Add(delta);
    }
}

internal static class OvalFlightMath
{
    internal static void Sample(
        float maximumDistance,
        float lateralRadius,
        double progress,
        out double forward,
        out double lateral)
    {
        double angle = GameMath.Clamp(progress, 0, 1) * Math.PI * 2;
        double forwardRadius = Math.Max(0, maximumDistance) / 2;
        forward = forwardRadius * (1 - Math.Cos(angle));
        lateral = Math.Max(0, lateralRadius) * Math.Sin(angle);
    }

    internal static bool SegmentIntersectsAabb(
        Vec3d start,
        Vec3d end,
        double minX,
        double minY,
        double minZ,
        double maxX,
        double maxY,
        double maxZ)
    {
        double tMin = 0;
        double tMax = 1;
        return IntersectsSlab(start.X, end.X - start.X, minX, maxX, ref tMin, ref tMax)
            && IntersectsSlab(start.Y, end.Y - start.Y, minY, maxY, ref tMin, ref tMax)
            && IntersectsSlab(start.Z, end.Z - start.Z, minZ, maxZ, ref tMin, ref tMax);
    }

    private static bool IntersectsSlab(
        double start,
        double delta,
        double minimum,
        double maximum,
        ref double tMin,
        ref double tMax)
    {
        if (Math.Abs(delta) <= 1e-12)
        {
            return start >= minimum && start <= maximum;
        }

        double inverse = 1 / delta;
        double near = (minimum - start) * inverse;
        double far = (maximum - start) * inverse;
        if (near > far) (near, far) = (far, near);

        tMin = Math.Max(tMin, near);
        tMax = Math.Min(tMax, far);
        return tMin <= tMax;
    }
}

internal static class ProjectileCatch
{
    internal static bool TryCatch(ProjectileEntity projectile, Entity owner)
    {
        ItemStack? stack = projectile.ProjectileStack;
        if (stack == null || stack.StackSize <= 0) return false;

        ItemStack returnedStack = stack.Clone();
        if (!owner.TryGiveItemStack(returnedStack)) return false;

        projectile.ProjectileStack = null;
        projectile.CanBeCollected = false;
        projectile.Die(EnumDespawnReason.PickedUp);
        return true;
    }
}
