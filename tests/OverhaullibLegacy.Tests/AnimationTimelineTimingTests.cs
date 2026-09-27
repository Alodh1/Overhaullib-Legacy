using CombatOverhaul.Animations;

namespace OverhaullibLegacy.Tests;

public sealed class AnimationTimelineTimingTests
{
    [Fact]
    public void ManualPlayerTime_IsClampedBetweenAdjacentKeyframes()
    {
        Assert.Equal(501, AnimationTimelineTiming.ClampPlayerKeyframeTime(0, 750, 500, 1000));
        Assert.Equal(999, AnimationTimelineTiming.ClampPlayerKeyframeTime(1200, 750, 500, 1000));
        Assert.Equal(850, AnimationTimelineTiming.ClampPlayerKeyframeTime(850, 750, 500, 1000));
    }

    [Fact]
    public void ManualPlayerTime_RejectsNonFiniteInputAndAllowsLastFrameExtension()
    {
        Assert.Equal(750, AnimationTimelineTiming.ClampPlayerKeyframeTime(double.NaN, 750, 500, 1000));
        Assert.Equal(1500, AnimationTimelineTiming.ClampPlayerKeyframeTime(1500, 1000, 500, double.NaN));
    }

    [Fact]
    public void PlaybackClamp_ToleratesTemporarilyInvertedBounds()
    {
        Assert.Equal(600, AnimationTimelineTiming.ClampPlaybackTime(600, 1000, 500));
        Assert.Equal(500, AnimationTimelineTiming.ClampPlaybackTime(100, 1000, 500));
    }
}
