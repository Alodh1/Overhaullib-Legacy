namespace CombatOverhaul.Animations;

internal static class AnimationTimelineTiming
{
    private const double MaximumManualTimeMilliseconds = int.MaxValue;
    private const double MinimumKeyframeGapMilliseconds = 1;

    internal static int ToManualInputMilliseconds(double timeMilliseconds)
    {
        if (!double.IsFinite(timeMilliseconds)) return 0;

        return (int)Math.Clamp(timeMilliseconds, 0, MaximumManualTimeMilliseconds);
    }

    internal static double ClampPlayerKeyframeTime(
        double requestedMilliseconds,
        double currentMilliseconds,
        double previousMilliseconds,
        double nextMilliseconds)
    {
        double fallback = double.IsFinite(currentMilliseconds)
            ? Math.Clamp(currentMilliseconds, 0, MaximumManualTimeMilliseconds)
            : 0;
        double minimum = double.IsFinite(previousMilliseconds)
            ? Math.Clamp(previousMilliseconds + MinimumKeyframeGapMilliseconds, 0, MaximumManualTimeMilliseconds)
            : 0;
        double maximum = double.IsFinite(nextMilliseconds)
            ? Math.Clamp(nextMilliseconds - MinimumKeyframeGapMilliseconds, 0, MaximumManualTimeMilliseconds)
            : MaximumManualTimeMilliseconds;

        // An already-corrupt or sub-millisecond-spaced timeline has no valid integer value
        // between its neighbours. Preserve the current finite value rather than throwing while
        // the user is editing it; the validation panel can then describe the source problem.
        if (maximum < minimum) return fallback;

        if (!double.IsFinite(requestedMilliseconds)) requestedMilliseconds = fallback;
        return Math.Clamp(requestedMilliseconds, minimum, maximum);
    }

    internal static double ClampPlaybackTime(double requestedMilliseconds, double startMilliseconds, double endMilliseconds)
    {
        if (!double.IsFinite(startMilliseconds)) startMilliseconds = 0;
        if (!double.IsFinite(endMilliseconds)) endMilliseconds = startMilliseconds;

        double minimum = Math.Min(startMilliseconds, endMilliseconds);
        double maximum = Math.Max(startMilliseconds, endMilliseconds);
        if (!double.IsFinite(requestedMilliseconds)) requestedMilliseconds = minimum;
        return Math.Clamp(requestedMilliseconds, minimum, maximum);
    }
}
