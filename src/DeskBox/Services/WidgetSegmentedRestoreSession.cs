namespace DeskBox.Services;

internal enum WidgetSegmentedRestoreFrameResult
{
    Waiting,
    Ready,
    Deferred,
    RetryAfterLayoutChange
}

/// <summary>
/// Owns a short visible-window attempt to wait for a safe Segmented layout.
/// Invalid geometry waits for SizeChanged instead of keeping Rendering alive.
/// </summary>
internal sealed class WidgetSegmentedRestoreSession
{
    internal const int RequiredStableFrames = 3;
    // Allow several settling passes, then give layout/input another turn.
    internal const int MaximumFramesPerAttempt = RequiredStableFrames * 4;
    private const double StableWidthTolerance = 0.5;

    private int _frameCount;
    private int _stableFrameCount;
    private double _candidateWidth;
    private long _layoutRevision;
    private long _attemptLayoutRevision;

    internal bool IsHostVisible { get; private set; }
    internal bool IsWaiting { get; private set; }

    internal void SetHostVisibility(bool visible)
    {
        IsHostVisible = visible;
        if (!visible)
        {
            Cancel();
        }
    }

    internal void NotifyLayoutChanged() => _layoutRevision++;

    internal bool TryStart(double candidateWidth)
    {
        if (!IsHostVisible || IsWaiting || !IsSafeWidth(candidateWidth))
        {
            return false;
        }

        IsWaiting = true;
        _frameCount = 0;
        _stableFrameCount = 0;
        _candidateWidth = candidateWidth;
        _attemptLayoutRevision = _layoutRevision;
        return true;
    }

    internal WidgetSegmentedRestoreFrameResult ObserveFrame(double candidateWidth)
    {
        if (!IsWaiting || !IsHostVisible || !IsSafeWidth(candidateWidth))
        {
            Cancel();
            return WidgetSegmentedRestoreFrameResult.Deferred;
        }

        _frameCount++;
        if (_stableFrameCount == 0 ||
            Math.Abs(candidateWidth - _candidateWidth) > StableWidthTolerance)
        {
            _candidateWidth = candidateWidth;
            _stableFrameCount = 1;
        }
        else
        {
            _stableFrameCount++;
        }

        if (_stableFrameCount >= RequiredStableFrames)
        {
            Cancel();
            return WidgetSegmentedRestoreFrameResult.Ready;
        }

        if (_frameCount < MaximumFramesPerAttempt)
        {
            return WidgetSegmentedRestoreFrameResult.Waiting;
        }

        // A final SizeChanged can arrive just before the last sampled frame.
        // Consume that event in one new attempt so a settled last size cannot
        // leave the tabs hidden merely because no further SizeChanged follows.
        bool layoutChanged = _layoutRevision != _attemptLayoutRevision;
        Cancel();
        return layoutChanged
            ? WidgetSegmentedRestoreFrameResult.RetryAfterLayoutChange
            : WidgetSegmentedRestoreFrameResult.Deferred;
    }

    internal void Cancel()
    {
        IsWaiting = false;
        _frameCount = 0;
        _stableFrameCount = 0;
        _candidateWidth = 0;
    }

    private static bool IsSafeWidth(double width) =>
        double.IsFinite(width) && width >= WidgetSegmentedLayoutHelper.MinimumSafeWidth;
}
