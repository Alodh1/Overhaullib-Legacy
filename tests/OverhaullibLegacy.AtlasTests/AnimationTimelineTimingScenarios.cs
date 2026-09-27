using System.Reflection;
using Atlas.XUnit;
using Xunit;

namespace OverhaullibLegacy.AtlasTests;

[Trait("Category", "E2E")]
public sealed class AnimationTimelineTimingScenarios : AtlasScenarioBase
{
    [AtlasScenario(TimeoutMs = 120000)]
    public async Task ManualKeyframeTiming_Should_RemainValidDuringTextReplacement()
    {
        await World.Ticks(5);

        Assembly overhaullib = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly => assembly.GetName().Name == "OverhaullibLegacyCompat")
            ?? throw new InvalidOperationException("OverhaullibLegacyCompat was not loaded by Atlas.");
        Type timingType = overhaullib.GetType("CombatOverhaul.Animations.AnimationTimelineTiming", throwOnError: true)!;
        MethodInfo clamp = timingType.GetMethod("ClampPlayerKeyframeTime", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(timingType.FullName, "ClampPlayerKeyframeTime");
        MethodInfo playbackClamp = timingType.GetMethod("ClampPlaybackTime", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new MissingMethodException(timingType.FullName, "ClampPlaybackTime");

        double transientReplacement = (double)clamp.Invoke(null, [0d, 750d, 500d, 1000d])!;
        double lastFrameExtension = (double)clamp.Invoke(null, [1500d, 1000d, 500d, double.NaN])!;
        double invertedPlaybackBounds = (double)playbackClamp.Invoke(null, [600d, 1000d, 500d])!;

        Assert.Equal(501d, transientReplacement);
        Assert.Equal(1500d, lastFrameExtension);
        Assert.Equal(600d, invertedPlaybackBounds);
    }
}
