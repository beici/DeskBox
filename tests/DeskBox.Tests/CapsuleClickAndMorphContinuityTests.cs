using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Locks the second capsule-motion round, measured on the same 165Hz display.
/// <para>
/// A plain title-bar click used to run a full raise/restore round trip (owner
/// detach, TOPMOST pulse, HWND_TOP, owner re-attach, then a twelve-window idle
/// normalization). Widgets 14px apart cast shadows across the gap, so both
/// facing edges re-composited on each of the two Z-order flips - the edge
/// flicker users saw on every click.
/// </para>
/// <para>
/// The morph timeline read absolute elapsed time, so the first rasterization of
/// a freshly revealed expanded tree (117ms on a cold File widget) advanced
/// progress nearly halfway on the first frame: the panel jumped open and only
/// six of its 46 frames were animated. Progress is now accumulated from clamped
/// frame steps, and the compositor-owned fades start with the first committed
/// frame instead of at setup time so the two timelines share one origin.
/// </para>
/// </summary>
public sealed class CapsuleClickAndMorphContinuityTests
{
    [Fact]
    public void TitleBarPress_ArmsTheDragWithoutTouchingTheDesktopLayer()
    {
        string interaction = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Interaction.cs"));
        string arm = SliceMethod(
            interaction,
            "    protected void BeginWindowDragCore(\n        PointerRoutedEventArgs e,\n        FrameworkElement captureElement,\n        bool activatesTitleGroup)",
            "    private void EngageWindowDrag()");

        // Every visible side effect must be absent from the press path.
        Assert.DoesNotContain("ElevateForInteraction", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("SimplifyBackdropForInteraction", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("ResizeGuideOverlay", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBeginCoordinatedMove", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("ActivateAllVisibleWidgetsFromTitle", arm, StringComparison.Ordinal);
        Assert.DoesNotContain("BeginCompactArrangementDrag", arm, StringComparison.Ordinal);

        // The press still has to capture the pointer and the start geometry,
        // otherwise the deferred drag would have no origin to move from.
        Assert.Contains("CapturePointer", arm, StringComparison.Ordinal);
        Assert.Contains("InitialWindowPos", arm, StringComparison.Ordinal);
        Assert.Contains("IsDragging = true", arm, StringComparison.Ordinal);
    }

    [Fact]
    public void DragEngagement_OwnsEveryTransactionAndRunsAtTheMoveThreshold()
    {
        string interaction = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Interaction.cs")));
        string engage = SliceMethod(
            interaction,
            "    private void EngageWindowDrag()",
            "    protected void ContinueWindowDragCore(");

        Assert.Contains("SimplifyBackdropForInteraction", engage, StringComparison.Ordinal);
        Assert.Contains("TryBeginCoordinatedMove", engage, StringComparison.Ordinal);
        Assert.Contains("ActivateAllVisibleWidgetsFromTitle", engage, StringComparison.Ordinal);
        Assert.Contains("ElevateForInteraction", engage, StringComparison.Ordinal);
        Assert.Contains("BeginCompactArrangementDrag", engage, StringComparison.Ordinal);
        Assert.Contains("ResizeGuideOverlay.BeginDrag", engage, StringComparison.Ordinal);

        string continueDrag = SliceMethod(
            interaction,
            "    protected void ContinueWindowDragCore(",
            "    private void QueueTitleBarDragFrame(");
        int threshold = continueDrag.IndexOf(
            "HasMovedTitleBarDrag = true;",
            StringComparison.Ordinal);
        int engaged = continueDrag.IndexOf("EngageWindowDrag();", StringComparison.Ordinal);
        int queued = continueDrag.IndexOf("QueueTitleBarDragFrame(", StringComparison.Ordinal);
        Assert.True(threshold > 0, "The move threshold branch is gone.");
        Assert.True(
            engaged > threshold,
            "The drag setup must run from the threshold crossing.");
        Assert.True(
            queued > engaged,
            "The first bounds frame must never be queued before the snap " +
            "session and the raise exist.");
    }

    [Fact]
    public void DragRelease_OnlyUndoesWhatAnEngagedDragDid()
    {
        string interaction = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Interaction.cs")));
        string release = SliceMethod(
            interaction,
            "    protected void EndWindowDragCore(",
            "    // ── Resize logic");

        Assert.Contains("bool wasEngaged = _isWindowDragEngaged;", release, StringComparison.Ordinal);

        // A click that never engaged must not restore a layer it never took,
        // refresh a backdrop it never downgraded, or persist bounds that never
        // changed - each of those is a visible or on-disk side effect.
        int guard = release.LastIndexOf("if (wasEngaged)", StringComparison.Ordinal);
        Assert.True(guard > 0, "The release-side guard is gone.");
        string guarded = release[guard..];
        Assert.Contains("QueueBackdropRefresh", guarded, StringComparison.Ordinal);
        Assert.Contains(
            "RestoreTemporarilyRaisedWidgetsToDesktopLayer",
            guarded,
            StringComparison.Ordinal);

        int persistGuard = release.IndexOf("if (hasMoved)", StringComparison.Ordinal);
        Assert.True(persistGuard > 0, "The persist guard is gone.");
        Assert.Contains(
            "UpdateConfigBoundsFromPhysical",
            release[persistGuard..guard],
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContentTitlePress_DefersTheGroupRaiseToTheDragItself()
    {
        string content = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/ContentWidgetWindow.WindowInteraction.cs")));
        string press = SliceMethod(
            content,
            "    private void TitleBarGrid_PointerPressed(",
            "    private bool ShouldOpenTitleBarFlyout(");

        Assert.DoesNotContain(
            "WidgetManager?.ActivateAllVisibleWidgetsFromTitle",
            press,
            StringComparison.Ordinal);
        Assert.Contains("activatesTitleGroup:", press, StringComparison.Ordinal);
    }

    [Theory]
    // 165Hz: a full-rate morph may advance at most two commit intervals per
    // frame, so a 117ms first-paint stall contributes 12ms of progress instead
    // of 117ms and the window keeps animating from where it actually is.
    [InlineData(165, 1, 117.1, 12.12)]
    [InlineData(165, 1, 6.1, 6.1)]
    [InlineData(165, 2, 40.0, 24.24)]
    [InlineData(60, 1, 250.0, 33.33)]
    [InlineData(60, 1, 12.0, 12.0)]
    public void MorphStep_IsClampedToTwoCommitIntervals(
        int refreshRateHz,
        int frameSkip,
        double rawStepMs,
        double expectedStepMs)
    {
        double frameBudgetMs = 1000.0 / refreshRateHz;
        double maximumStepMs = WidgetCompactTransitionProgressPolicy.ResolveMaximumStepMs(
            frameBudgetMs,
            frameSkip);

        double stepMs = WidgetCompactTransitionProgressPolicy.ClampStepMs(
            rawStepMs,
            maximumStepMs);

        Assert.Equal(expectedStepMs, stepMs, 2);
    }

    [Fact]
    public void MorphTimeline_AnimatesEveryFrameThroughAStall()
    {
        // Replays the measured cold File expand: one 117ms stall before the
        // first frame, then 165Hz ticks. Raw elapsed time resolved progress
        // 0.44 on the first frame; the clamped timeline must start from zero
        // and never skip more than a couple of frames' worth at a time.
        const double durationMs = 265;
        const double frameBudgetMs = 1000.0 / 165;
        double maximumStepMs = WidgetCompactTransitionProgressPolicy.ResolveMaximumStepMs(
            frameBudgetMs,
            1);
        double maximumStallMs =
            WidgetCompactTransitionProgressPolicy.ResolveMaximumStallMs(durationMs);

        double progressMs = 0;
        double stalledMs = 0;
        var progressSamples = new List<double>();
        double[] rawSteps = [117.1, frameBudgetMs, frameBudgetMs, 51.1, frameBudgetMs];
        foreach (double rawStepMs in rawSteps)
        {
            double stepMs = stalledMs < maximumStallMs
                ? WidgetCompactTransitionProgressPolicy.ClampStepMs(rawStepMs, maximumStepMs)
                : rawStepMs;
            progressMs += stepMs;
            stalledMs += rawStepMs - stepMs;
            progressSamples.Add(Math.Clamp(progressMs / durationMs, 0, 1));
        }

        // The first stall is absorbed: no frame may jump the morph by more
        // than two commit intervals of progress.
        double previous = 0;
        foreach (double progress in progressSamples)
        {
            Assert.True(
                progress - previous <= (maximumStepMs / durationMs) + 1e-9,
                $"A single frame advanced the morph by {progress - previous:F3}.");
            previous = progress;
        }

        Assert.True(stalledMs > 100, "The stall was not absorbed at all.");
        Assert.True(
            progressSamples[^1] < 0.35,
            "Two stalls plus three frames must not have finished the morph.");
    }

    [Theory]
    [InlineData(265, 198.75)]
    [InlineData(40, 60)]
    [InlineData(1000, 220)]
    public void StallBudget_StaysBoundedSoAMorphNeverDrags(
        double durationMs,
        double expectedBudgetMs)
    {
        Assert.Equal(
            expectedBudgetMs,
            WidgetCompactTransitionProgressPolicy.ResolveMaximumStallMs(durationMs),
            2);
    }

    [Fact]
    public void CompletionWatchdog_AllowsTheClampedStretch()
    {
        string collapse = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Collapse.cs")));
        string start = SliceMethod(
            collapse,
            "    private void StartBoundsTransition(",
            "    private void CompleteBoundsTransitionAfterTimeout()");

        // Absorbing a stall makes the morph legitimately longer than its
        // nominal duration; a watchdog derived from the duration alone would
        // snap exactly the transitions the clamp is there to smooth.
        int watchdog = start.IndexOf(
            "_collapseAnimationWatchdogTimer",
            StringComparison.Ordinal);
        Assert.True(watchdog > 0, "The watchdog schedule is gone.");
        Assert.Contains(
            "_collapseAnimationMaximumStallMs",
            start[watchdog..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void ContentFades_StartWithTheFirstCommittedGeometryFrame()
    {
        string shell = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetShell.xaml.cs")));
        string prepare = SliceMethod(
            shell,
            "    public bool PrepareCompactTransition(",
            "    private bool CanRunCompactCompositionTransition()");

        // Prepare reveals the expanded tree, which is what forces its first
        // rasterization. Starting the compositor fades here let them run while
        // the UI thread was still paying for that paint.
        Assert.DoesNotContain(
            "StartCompactCompositionTransition(",
            prepare,
            StringComparison.Ordinal);
        Assert.Contains("_hasStartedCompactCompositionFades = false;", prepare, StringComparison.Ordinal);

        string fades = SliceMethod(
            shell,
            "    public void StartCompactTransitionFades()",
            "    public void ResyncCompactTransitionFades(");
        Assert.Contains("StartCompactCompositionTransition(", fades, StringComparison.Ordinal);

        string collapse = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Collapse.cs")));
        string rendering = SliceMethod(
            collapse,
            "    private void CollapseAnimationRendering()",
            "    private void RecordCollapseAnimationTickCadence(");
        int firstFrame = rendering.IndexOf(
            "if (!_hasCommittedCollapseAnimationFrame)",
            StringComparison.Ordinal);
        int startFades = rendering.IndexOf(
            "WidgetShellControl.StartCompactTransitionFades();",
            StringComparison.Ordinal);
        int accumulate = rendering.IndexOf(
            "_collapseAnimationProgressMs += stepMs;",
            StringComparison.Ordinal);
        Assert.True(firstFrame > 0 && startFades > firstFrame);
        Assert.True(
            accumulate > startFades,
            "Progress must not accumulate before the fades share its origin.");
        Assert.Contains(
            "ResyncCompactTransitionFades(progress)",
            rendering,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MorphFrames_ReuseTheirNativeCommitCallbacks()
    {
        string collapse = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Collapse.cs")));
        string move = SliceMethod(
            collapse,
            "    private void MoveWindowWithoutPersisting(",
            "    private void StopCollapseAnimation(");

        // Fresh closures here allocated four objects on every animation frame
        // of every animating widget.
        Assert.Contains("_beginApplyingBoundsCallback ??=", move, StringComparison.Ordinal);
        Assert.Contains("_endApplyingBoundsCallback ??=", move, StringComparison.Ordinal);
        Assert.Contains("_pendingBoundsMoveFallbackCallback ??=", move, StringComparison.Ordinal);
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        string normalizedSource = NormalizeNewlines(source);
        string normalizedStart = NormalizeNewlines(startMarker);
        string normalizedEnd = NormalizeNewlines(endMarker);
        int start = normalizedSource.IndexOf(normalizedStart, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        int end = normalizedSource.IndexOf(normalizedEnd, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return normalizedSource[start..end];
    }

    private static string NormalizeNewlines(string source)
    {
        return source.Replace("\r\n", "\n").Replace('\r', '\n');
    }
}
