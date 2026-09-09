// Copyright (c) DeskBox. All rights reserved.

namespace DeskBox.Services;

/// <summary>
/// Advances a capsule morph by clamped frame deltas instead of raw wall-clock
/// elapsed time.
/// <para>
/// The geometry timeline used to read the absolute elapsed time on every
/// compositor tick. When the UI thread stalled - the first rasterization of a
/// freshly revealed expanded tree costs over 100ms on a cold widget - the next
/// tick resolved a progress far ahead of the last committed frame, so the
/// window jumped most of the way open and only the tail of the morph was
/// animated. Clamping each step to a small multiple of the frame budget turns
/// that stall into a slightly longer transition instead of a jump, which is
/// what "smooth" means to the eye: consistent increments, not a correct total
/// duration.
/// </para>
/// </summary>
internal static class WidgetCompactTransitionProgressPolicy
{
    /// <summary>
    /// Largest advance a single committed frame may contribute, expressed in
    /// commit intervals. Two keeps a genuine one-frame miss (the common case on
    /// a busy machine) at full speed while capping a real stall.
    /// </summary>
    public const double MaximumStepIntervals = 2.0;

    /// <summary>
    /// A stall only needs recovering when it cost more than this. Below it the
    /// clamp is indistinguishable from normal jitter.
    /// </summary>
    public const double StallRecoveryThresholdMs = 12.0;

    /// <summary>
    /// Upper bound on how much a single morph may be stretched by clamping.
    /// Absorbing a stall keeps the motion continuous, but an unbounded stretch
    /// would turn a pathological freeze into a visibly sluggish transition (and
    /// eventually collide with the completion watchdog), so past this budget
    /// the timeline gives up and converges on real time.
    /// </summary>
    public static double ResolveMaximumStallMs(double durationMs)
    {
        double duration = durationMs > 0 ? durationMs : 240;
        return Math.Clamp(duration * 0.75, 60, 220);
    }

    public static double ResolveMaximumStepMs(double frameBudgetMs, int frameSkip)
    {
        double budget = frameBudgetMs > 0 ? frameBudgetMs : 1000.0 / 60.0;
        int skip = Math.Max(1, frameSkip);
        return Math.Max(1.0, budget * skip * MaximumStepIntervals);
    }

    public static double ClampStepMs(double rawStepMs, double maximumStepMs)
    {
        if (double.IsNaN(rawStepMs) || rawStepMs <= 0)
        {
            return 0;
        }

        double limit = maximumStepMs > 0 ? maximumStepMs : rawStepMs;
        return Math.Min(rawStepMs, limit);
    }

    public static bool ShouldReportStall(double stalledMs)
    {
        return stalledMs >= StallRecoveryThresholdMs;
    }
}
