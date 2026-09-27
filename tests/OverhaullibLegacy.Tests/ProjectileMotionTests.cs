using CombatOverhaul.RangedSystems;
using Vintagestory.API.MathTools;

namespace OverhaullibLegacy.Tests;

public sealed class ProjectileMotionTests
{
    [Fact]
    public void RotateHorizontal_PreservesSpeedAndVerticalMotion()
    {
        Vec3d motion = new(0, 0.25, 12);

        CurvedFlightMath.RotateHorizontal(motion, MathF.PI / 2, direction: 1, dt: 0.5f);

        Assert.Equal(12, Math.Sqrt(motion.X * motion.X + motion.Z * motion.Z), precision: 10);
        Assert.Equal(0.25, motion.Y, precision: 10);
        Assert.True(motion.X > 0);
        Assert.True(motion.Z > 0);
    }

    [Fact]
    public void RotateHorizontal_NegativeDirectionCurvesOppositeWay()
    {
        Vec3d right = new(0, 0, 10);
        Vec3d left = new(0, 0, 10);

        CurvedFlightMath.RotateHorizontal(right, 1, direction: 1, dt: 0.25f);
        CurvedFlightMath.RotateHorizontal(left, 1, direction: -1, dt: 0.25f);

        Assert.Equal(-right.X, left.X, precision: 10);
        Assert.Equal(right.Z, left.Z, precision: 10);
    }

    [Fact]
    public void SteerToward_ClampsVelocityChange()
    {
        Vec3d motion = new(10, 0, 0);
        Vec3d desired = new(0, 10, 0);

        CurvedFlightMath.SteerToward(motion, desired, maximumDelta: 2);

        Vec3d appliedDelta = motion.SubCopy(new Vec3d(10, 0, 0));
        Assert.Equal(2, appliedDelta.Length(), precision: 10);
    }

    [Fact]
    public void SteerToward_ZeroAccelerationDoesNotChangeVelocity()
    {
        Vec3d motion = new(10, 0, 0);

        CurvedFlightMath.SteerToward(motion, new Vec3d(0, 10, 0), maximumDelta: 0);

        Assert.Equal(10, motion.X);
        Assert.Equal(0, motion.Y);
        Assert.Equal(0, motion.Z);
    }

    [Fact]
    public void CurvedFlightDefaults_AreNoOpAndReturnIsDisabled()
    {
        CurvedFlightConfig config = new();

        Assert.Equal(0, config.HorizontalTurnRate);
        Assert.True(config.ReturnAfterSeconds < 0);
    }

    [Fact]
    public void OvalFlight_HasSideTipsAndSymmetricHalves()
    {
        OvalFlightMath.Sample(8, 5, 0, out double startForward, out double startLateral);
        OvalFlightMath.Sample(8, 5, 0.25, out double rightForward, out double rightLateral);
        OvalFlightMath.Sample(8, 5, 0.5, out double frontForward, out double frontLateral);
        OvalFlightMath.Sample(8, 5, 0.75, out double leftForward, out double leftLateral);
        OvalFlightMath.Sample(8, 5, 1, out double endForward, out double endLateral);

        Assert.Equal(0, startForward, 10);
        Assert.Equal(0, startLateral, 10);
        Assert.Equal(4, rightForward, 10);
        Assert.Equal(5, rightLateral, 10);
        Assert.Equal(8, frontForward, 10);
        Assert.Equal(0, frontLateral, 10);
        Assert.Equal(rightForward, leftForward, 10);
        Assert.Equal(-rightLateral, leftLateral, 10);
        Assert.Equal(0, endForward, 10);
        Assert.Equal(0, endLateral, 10);
    }

    [Fact]
    public void OvalFlight_NeverExceedsMaximumDistanceFromLaunch()
    {
        double maximum = 0;
        for (int index = 0; index <= 1000; index++)
        {
            OvalFlightMath.Sample(8, 5, index / 1000d, out double forward, out double lateral);
            maximum = Math.Max(maximum, Math.Sqrt(forward * forward + lateral * lateral));
        }

        Assert.Equal(8, maximum, 6);
    }

    [Fact]
    public void OwnerCatch_RequiresSweptCollision()
    {
        Vec3d start = new(0, 1, 2);

        Assert.True(OvalFlightMath.SegmentIntersectsAabb(
            start, new Vec3d(0, 1, -1),
            -0.4, 0, -0.4, 0.4, 2, 0.4));
        Assert.False(OvalFlightMath.SegmentIntersectsAabb(
            start, new Vec3d(0, 1, -1),
            2.6, 0, -0.4, 3.4, 2, 0.4));
    }
}
