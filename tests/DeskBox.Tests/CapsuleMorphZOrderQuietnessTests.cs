using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Locks the fixes for the two capsule-motion defects measured on a 165Hz
/// display: the hover expand morph lost 34 of 46 frames to a 110ms compositor
/// stall caused by a peer-order DeferWindowPos burst on its first frame, and
/// every title-bar release re-stacked widgets whose acrylic backdrops then
/// re-sampled as an edge flash. Both traced back to Win32 Z-order primitives
/// that let Windows reposition the shared Explorer owner, which permanently
/// scrambled the widget chain.
/// </summary>
public sealed class CapsuleMorphZOrderQuietnessTests
{
    [Theory]
    // 165Hz is not a multiple of any tier, which is where the old
    // round-to-nearest divisor undershot: 60fps resolved to 55fps.
    [InlineData(165, 30, 33)]
    [InlineData(165, 60, 82)]
    [InlineData(165, 90, 165)]
    [InlineData(165, 120, 165)]
    [InlineData(144, 30, 36)]
    [InlineData(144, 60, 72)]
    [InlineData(75, 60, 75)]
    [InlineData(240, 60, 60)]
    [InlineData(240, 120, 120)]
    [InlineData(60, 60, 60)]
    [InlineData(60, 30, 30)]
    public void ResolveSkipForFrameRate_NeverDeliversLessThanTheSelectedRate(
        int refreshRateHz,
        int targetFrameRateHz,
        int expectedDeliveredFps)
    {
        int skip = WidgetCompactFrameSkipPolicy.ResolveSkipForFrameRate(
            refreshRateHz,
            targetFrameRateHz);

        Assert.True(skip >= 1);
        int deliveredFps = refreshRateHz / skip;
        Assert.Equal(expectedDeliveredFps, deliveredFps);
        Assert.True(
            deliveredFps >= targetFrameRateHz,
            $"{refreshRateHz}Hz capped at {targetFrameRateHz}fps delivered " +
            $"{deliveredFps}fps, below the selected rate.");
    }

    [Fact]
    public void ZOrderRaises_NeverRepositionTheSharedExplorerOwner()
    {
        string helper = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Helpers/Win32Helper.cs"));
        string convenience = helper[helper.IndexOf(
            "public static void SetWindowToBottom",
            StringComparison.Ordinal)..];
        convenience = convenience[..convenience.IndexOf(
            "public static bool IsWindowTopMost",
            StringComparison.Ordinal)];

        // Resting widgets are owned by Explorer's SHELLDLL_DefView so Win+D
        // cannot hide them. Every raise must therefore opt out of owner
        // reordering, or Windows drags the whole owner group.
        Assert.Contains(
            "SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER",
            helper,
            StringComparison.Ordinal);
        foreach (string raise in new[]
                 {
                     "public static void SetWindowToDesktopLevel",
                     "public static void BringWindowToFront",
                     "public static void SetWindowTopMost(IntPtr hWnd, bool showWindow)",
                     "public static void ClearWindowTopMost"
                 })
        {
            int start = convenience.IndexOf(raise, StringComparison.Ordinal);
            Assert.True(start >= 0, $"Missing source marker: {raise}");
            Assert.Contains(
                "ZOrderRaiseFlags",
                Window(convenience, start, 420),
                StringComparison.Ordinal);
        }

        // HWND_BOTTOM is the exception: an owned window only stays visible at
        // the bottom because Windows sinks its owner with it, so blocking that
        // drops the widget under the wallpaper and it renders blank.
        int bottom = convenience.IndexOf("HWND_BOTTOM", StringComparison.Ordinal);
        Assert.True(bottom >= 0, "SetWindowToBottom no longer uses HWND_BOTTOM.");
        Assert.Contains(
            "ZOrderBottomFlags",
            Window(convenience, bottom, 120),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "SWP_NOOWNERZORDER",
            Window(convenience, bottom, 120),
            StringComparison.Ordinal);

        string layer = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetLayerService.cs"));
        string restoreOwner = SliceMethod(
            layer,
            "private static void RestoreOriginalOwner",
            "private static IntPtr FindDesktopIconView");
        Assert.Contains("SWP_NOOWNERZORDER", restoreOwner, StringComparison.Ordinal);

        // Same exception inside the desktop-owner attach: its bottom placement
        // must let the owner follow.
        string attach = SliceMethod(
            layer,
            "private static bool TryAttachToDesktopIconLayer",
            "private static void DetachFromDesktopIconLayerIfNeeded");
        Assert.Contains("Win32Helper.HWND_BOTTOM", attach, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Win32Helper.SWP_NOOWNERZORDER",
            attach,
            StringComparison.Ordinal);
        Assert.Contains(
            "Win32Helper.ClearWindowTopMost(windowHandle);",
            attach,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ExpandedPeerOrder_VerifiesBeforeItReordersTheWholeGroup()
    {
        string layer = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetLayerService.cs"));
        string method = SliceMethod(
            layer,
            "public static bool EnsurePeerOrderHighestToLowest",
            "public static bool IsHighestPeer");

        // The expand hot path runs this one statement before the morph starts.
        // The full-group batch must be the last resort, not the first action:
        // the first IsHighestPeer check has to precede any ApplyPeerOrder call.
        int firstVerify = method.IndexOf(
            "if (IsHighestPeer(activeWindow, handles))",
            StringComparison.Ordinal);
        int firstApply = method.IndexOf(
            "ApplyPeerOrderHighestToLowest(handles)",
            StringComparison.Ordinal);
        Assert.True(firstVerify > 0, "The verify-first fast path is gone.");
        Assert.True(
            firstVerify < firstApply,
            "EnsurePeerOrderHighestToLowest reorders the whole group before " +
            "checking whether the active capsule is already highest.");

        // Exactly one full-group batch remains, and the single-window raise
        // must come before it.
        Assert.Equal(
            1,
            method.Split("ApplyPeerOrderHighestToLowest(handles)").Length - 1);
        int singleWindowRaise = method.IndexOf(
            "TryBringAbovePeerWidgetsAtDesktopLayer(activeWindow)",
            StringComparison.Ordinal);
        Assert.True(
            singleWindowRaise > 0 && singleWindowRaise < firstApply,
            "The cheap single-window raise must be attempted before the batch.");
    }

    [Fact]
    public void AlreadyCorrectChain_IsRecognizedEvenWhenAnchoredAtHwndTop()
    {
        string layer = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetLayerService.cs"));
        string method = NormalizeNewlines(SliceMethod(
            layer,
            "private static bool IsWindowChainAlreadyHighestToLowest",
            "private static bool TryApplyMinimalWindowMoves"));

        // Returning false for the HWND_TOP sentinel forced an explicit reorder
        // on every title release, and each move re-samples the widget acrylic.
        int sentinel = method.IndexOf(
            "boundary == Win32Helper.HWND_TOP",
            StringComparison.Ordinal);
        Assert.True(sentinel > 0, "The HWND_TOP sentinel branch is gone.");
        string sentinelBranch = method[sentinel..];
        Assert.Contains("GW_HWNDPREV", sentinelBranch, StringComparison.Ordinal);
        Assert.Contains("IntPtr.Zero", sentinelBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("return false;", sentinelBranch, StringComparison.Ordinal);

        // The pairwise chain walk must run before the sentinel branch so a
        // HWND_TOP anchor still verifies the peer order itself.
        int pairwiseWalk = method.IndexOf("GW_HWNDNEXT", StringComparison.Ordinal);
        Assert.True(
            pairwiseWalk > 0 && pairwiseWalk < sentinel,
            "The chain walk must precede the boundary check.");
    }

    [Theory]
    // The pass used to anchor each mover to a predecessor that had not been
    // placed yet, so it reported success while leaving the chain out of order.
    [InlineData("CBA", "ABC")]
    [InlineData("ACB", "CBA")]
    [InlineData("BADC", "ABCD")]
    [InlineData("BCDEFGHIJKLA", "ABCDEFGHIJKL")]
    [InlineData("ABCDEFGLHIJK", "ABCDEFGHIJKL")]
    [InlineData("LKJIHGFEDCBA", "ABCDEFGHIJKL")]
    public void PeerOrderPlan_ConvergesInASinglePass(string live, string target)
    {
        IntPtr boundary = new(0x7000);
        List<IntPtr> targetChain = ToHandles(target);
        List<IntPtr> liveChain = ToHandles(live);

        IReadOnlyList<WidgetPeerOrderMovePlanner.PeerOrderMove>? plan =
            WidgetPeerOrderMovePlanner.Plan(targetChain, liveChain, boundary);

        Assert.NotNull(plan);
        List<IntPtr> applied = ApplyMoves(liveChain, plan!, boundary);
        Assert.Equal(targetChain, applied);

        // A second pass must find nothing left to do, otherwise every idle
        // normalization keeps re-stacking widgets and flashing their backdrops.
        IReadOnlyList<WidgetPeerOrderMovePlanner.PeerOrderMove>? second =
            WidgetPeerOrderMovePlanner.Plan(targetChain, applied, boundary);
        Assert.NotNull(second);
        Assert.Empty(second!);
    }

    [Fact]
    public void PeerOrderPlan_KeepsTheLongestInOrderRunInPlace()
    {
        IntPtr boundary = new(0x7000);
        List<IntPtr> targetChain = ToHandles("ABCDEF");
        // Only the clicked widget moved to the top; the five peers kept their
        // relative order, so exactly one move is required.
        List<IntPtr> liveChain = ToHandles("DABCEF");

        IReadOnlyList<WidgetPeerOrderMovePlanner.PeerOrderMove>? plan =
            WidgetPeerOrderMovePlanner.Plan(targetChain, liveChain, boundary);

        Assert.NotNull(plan);
        Assert.Single(plan!);
        Assert.Equal(ToHandles("D")[0], plan![0].Handle);
        Assert.Equal(ToHandles("C")[0], plan[0].InsertAfter);
        Assert.Equal(targetChain, ApplyMoves(liveChain, plan, boundary));
    }

    [Fact]
    public void PeerOrderPlan_RejectsAChainItCannotSeeCompletely()
    {
        IntPtr boundary = new(0x7000);

        Assert.Null(WidgetPeerOrderMovePlanner.Plan(
            ToHandles("ABC"),
            ToHandles("AB"),
            boundary));
        Assert.Null(WidgetPeerOrderMovePlanner.Plan(
            ToHandles("ABC"),
            ToHandles("ABD"),
            boundary));
    }

    private static List<IntPtr> ToHandles(string letters)
    {
        return letters.Select(letter => new IntPtr(0x1000 + letter)).ToList();
    }

    private static List<IntPtr> ApplyMoves(
        IReadOnlyList<IntPtr> liveChain,
        IReadOnlyList<WidgetPeerOrderMovePlanner.PeerOrderMove> plan,
        IntPtr boundary)
    {
        // Mirrors EndDeferWindowPos: each move is applied in sequence, and
        // "insert after boundary" means "become the highest tracked window".
        var chain = new List<IntPtr>(liveChain);
        foreach (WidgetPeerOrderMovePlanner.PeerOrderMove move in plan)
        {
            chain.Remove(move.Handle);
            int anchor = move.InsertAfter == boundary
                ? -1
                : chain.IndexOf(move.InsertAfter);
            Assert.True(
                anchor >= 0 || move.InsertAfter == boundary,
                "A move anchored on a window that is not in the chain.");
            chain.Insert(anchor + 1, move.Handle);
        }

        return chain;
    }

    [Fact]
    public void BoundsTransitionClock_StartsWithTheCompositionAnimations()
    {
        string collapse = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Collapse.cs"));
        string method = SliceMethod(
            collapse,
            "private void StartBoundsTransition",
            "private void CompleteBoundsTransitionAfterTimeout");

        int prepare = method.IndexOf(
            "WidgetShellControl.PrepareCompactTransition(",
            StringComparison.Ordinal);
        int clock = method.IndexOf(
            "_collapseAnimationStarted = Stopwatch.GetTimestamp();",
            StringComparison.Ordinal);
        int tracker = method.IndexOf(
            "_compactAnimationFrameTracker = new WidgetCompactAnimationFrameTracker(",
            StringComparison.Ordinal);

        Assert.True(prepare > 0 && clock > 0 && tracker > 0);
        Assert.True(
            clock > prepare,
            "The HWND geometry timeline must not start before the Composition " +
            "animations it is supposed to stay in phase with.");
        Assert.True(tracker > prepare, "The frame tracker must share the same origin.");
    }

    private static string SliceMethod(string source, string startMarker, string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing source marker: {startMarker}");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing source marker: {endMarker}");
        return source[start..end];
    }

    private static string NormalizeNewlines(string source)
    {
        return source.Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static string Window(string source, int start, int length)
    {
        return source[start..Math.Min(source.Length, start + length)];
    }
}
