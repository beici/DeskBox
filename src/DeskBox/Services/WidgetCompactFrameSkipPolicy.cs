namespace DeskBox.Services;

/// <summary>
/// Budget-driven HWND resize cadence for capsule transitions. Animations start
/// at the display's native rate and escalate one level only when ticks keep
/// overrunning the frame budget, so high-refresh machines animate at full
/// rate while saturated machines fall back to the previous ~60fps behavior.
/// </summary>
internal static class WidgetCompactFrameSkipPolicy
{
    public const int FullRateLevel = 1;
    public const int SixtyFpsLevel = 2;
    public const int ThirtyFpsLevel = 3;

    /// <summary>
    /// User-selectable animation frame-rate tiers in fps (the General
    /// settings tab exposes exactly these four, labeled 低/中/高/最高 via
    /// localization).
    /// </summary>
    public static readonly int[] SelectableFrameRates = { 30, 60, 90, 120 };

    public const int DefaultFrameRate = 60;

    /// <summary>
    /// Clamps an arbitrary stored value onto the four selectable tiers
    /// (30/60/90/120); unknown values fall back to
    /// <see cref="DefaultFrameRate"/>.
    /// </summary>
    public static int NormalizeFrameRate(int value)
    {
        return value switch
        {
            30 => 30,
            90 => 90,
            120 => 120,
            _ => DefaultFrameRate
        };
    }

    // A tick counts as overrun when its interval exceeds the frame budget by
    // this factor (same threshold as WidgetCompactAnimationFrameTracker).
    public const double OverrunBudgetFactor = 1.5;

    public const int TickWindow = 8;
    public const int MinimumOverrunTicksToEscalate = 6;

    /// <summary>
    /// Resolves the ladder level for a user frame-rate cap when the cap maps
    /// onto the classic ladder (60 → SixtyFpsLevel, 30 → ThirtyFpsLevel).
    /// Caps of 90/120 have no ladder rung — use
    /// <see cref="ResolveSkipForFrameRate"/> for those.
    /// </summary>
    public static int ResolveLevelForFrameRate(int refreshRateHz, int targetFrameRateHz)
    {
        int rate = Math.Max(1, refreshRateHz);
        int target = Math.Max(1, targetFrameRateHz);
        if (target >= rate)
        {
            return FullRateLevel;
        }

        return Math.Max(SixtyFpsLevel, (int)Math.Round(rate / (double)target));
    }

    public static int ResolveSkip(int refreshRateHz, int level)
    {
        int rate = Math.Max(1, refreshRateHz);
        return level switch
        {
            SixtyFpsLevel => Math.Max(1, (int)Math.Round(rate / 60.0)),
            >= ThirtyFpsLevel => Math.Max(1, (int)Math.Round(rate / 30.0)),
            _ => 1
        };
    }

    /// <summary>
    /// Per-tick skip for an explicit frame-rate cap: the HWND resize advances
    /// only on every N-th tick, delivering refresh/N fps — always at or under
    /// the target. At 165Hz: 120 → skip 2 (≈82fps, the closest cadence at or
    /// under 120), 90 → skip 2, 60 → skip 3 (55fps), 30 → skip 6 (27fps).
    /// </summary>
    public static int ResolveSkipForFrameRate(int refreshRateHz, int targetFrameRateHz)
    {
        int rate = Math.Max(1, refreshRateHz);
        int target = Math.Max(1, targetFrameRateHz);
        if (target >= rate)
        {
            return 1;
        }

        return Math.Max(2, (int)Math.Round(rate / (double)target));
    }

    public static bool IsOverrun(double intervalMs, double frameBudgetMs)
    {
        return intervalMs > frameBudgetMs * OverrunBudgetFactor;
    }

    public static bool ShouldEscalate(int overrunTicks, int sampledTicks)
    {
        return sampledTicks >= TickWindow &&
            overrunTicks >= MinimumOverrunTicksToEscalate;
    }

    public static int Escalate(int level)
    {
        return Math.Min(ThirtyFpsLevel, level + 1);
    }

    public static int ClampLevel(int level)
    {
        return Math.Clamp(level, FullRateLevel, ThirtyFpsLevel);
    }
}
