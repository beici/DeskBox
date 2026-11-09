namespace DeskBox.Tests;

public sealed class WidgetCohesionExitZOrderContractTests
{
    [Fact]
    public void ExpandedLeaseRelease_RequestsCohesionExitNormalization()
    {
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs"));
        string release = SliceMethod(
            manager,
            "internal bool ReleaseExpandedWidgetLayer",
            "private bool HasActiveExpandedWidgetLayerLease");

        Assert.Contains(
            "QueueIdleWidgetZOrderNormalization(reason, TimeSpan.Zero, requirePeerOrderRestore: true)",
            release,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CohesionExitNormalization_BypassesTheDropShadowPolicy()
    {
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs"));

        string queue = SliceMethod(
            manager,
            "internal void QueueIdleWidgetZOrderNormalization",
            "private long TrackTemporarilyRaisedWidgets");
        Assert.Contains("!requirePeerOrderRestore", queue, StringComparison.Ordinal);

        string normalize = SliceMethod(
            manager,
            "private bool NormalizeIdleWidgetZOrder",
            "private static IReadOnlyList<IDesktopWidgetWindow> GetWindowsInIdleHighestFirstOrder");
        // The shadow gate stays conditional so an idle request is still
        // dropped when the system disables window drop shadows.
        Assert.Contains(
            "(!requirePeerOrderRestore && (_widgetsRaisedFromTray ||\r\n                !IdleWidgetZOrderPolicy.ShouldNormalizeIdlePeerOrder(dropShadowEnabled)))",
            normalize.ReplaceLineEndings("\r\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CohesionExitNormalization_WaitsForSettledGeometryAndPreservesRequiredWork()
    {
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs"));
        string normalize = SliceMethod(
            manager,
            "private bool NormalizeIdleWidgetZOrder",
            "private static IReadOnlyList<IDesktopWidgetWindow> GetWindowsInIdleHighestFirstOrder")
            .ReplaceLineEndings("\r\n");

        Assert.Contains(
            "if (candidates.Any(window => window.IsBoundsTransitionActive))",
            normalize,
            StringComparison.Ordinal);
        Assert.Contains(
            "requirePeerOrderRestore: requirePeerOrderRestore",
            normalize,
            StringComparison.Ordinal);
        Assert.Contains("requirePeerOrderRestore |= _idlePeerOrderRequest.RequiresPeerOrderRestore", manager);
        Assert.Contains("_idlePeerOrderRequest.Complete(generation)", manager);
        string collapse = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Collapse.cs"));
        Assert.Contains("ResumePendingIdleWidgetZOrderNormalization()", collapse);
    }

    [Fact]
    public void CohesionExitNormalization_RemainsPeerOnlyReordering()
    {
        // The unconditional exit must not smuggle in any global re-bedding:
        // collapse exit is still a peer-order repair inside the existing band.
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs"));
        string normalize = SliceMethod(
            manager,
            "private bool NormalizeIdleWidgetZOrder",
            "private static IReadOnlyList<IDesktopWidgetWindow> GetWindowsInIdleHighestFirstOrder");

        Assert.Contains("ApplyPeerOrderWithinCurrentBands", normalize, StringComparison.Ordinal);
        Assert.Contains("(requirePeerOrderRestore || !window.IsRaisedAboveDesktopLayer)", normalize);
        string sessionGuard = normalize[..normalize.IndexOf(
            "List<IDesktopWidgetWindow> candidates", StringComparison.Ordinal)];
        Assert.DoesNotContain("if (_widgetsRaisedFromTray ||", sessionGuard);
        Assert.DoesNotContain("MoveToDesktopBottom", normalize, StringComparison.Ordinal);
        Assert.DoesNotContain("SetWindowToBottom", normalize, StringComparison.Ordinal);
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        return source[start..end];
    }
}
