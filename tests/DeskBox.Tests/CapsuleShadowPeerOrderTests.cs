using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class CapsuleShadowPeerOrderTests
{
    [Fact]
    public void NativeCapsuleActivation_QueuesRequiredRepairAfterGroupReassert()
    {
        string source = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/ContentWidgetWindow.WindowInteraction.cs"));
        int start = source.IndexOf("private void ContentWidgetWindow_Activated", StringComparison.Ordinal);
        int end = source.IndexOf("private void QueueRestoreDesktopLayerIfForegroundLeavesDeskBox", start, StringComparison.Ordinal);
        string activation = source[start..end];

        int deactivated = activation.IndexOf("WindowActivationState.Deactivated", StringComparison.Ordinal);
        int reassert = activation.IndexOf("ReassertRaisedWidgetGroupAfterDeskBoxActivation", StringComparison.Ordinal);
        int compact = activation.IndexOf("if (IsCompactCollapsedState)", StringComparison.Ordinal);
        int repair = activation.IndexOf("\"compact-window-activated\", TimeSpan.Zero, requirePeerOrderRestore: true", StringComparison.Ordinal);
        Assert.True(deactivated >= 0 && reassert > deactivated && compact > reassert && repair > compact);
        Assert.DoesNotContain("MoveToDesktopBottom", activation[compact..]);
        Assert.DoesNotContain("SetWindowPos", activation[compact..]);
    }

    [Fact]
    public void CollapseRepair_SurvivesIdleCoalescingAndAnimationRetries()
    {
        var request = new IdleWidgetZOrderRequest();
        long collapse = request.Queue(requirePeerOrderRestore: true);
        long idle = request.Queue(requirePeerOrderRestore: false);
        long retry = request.Queue(requirePeerOrderRestore: false);

        Assert.True(request.RequiresPeerOrderRestore);
        Assert.False(request.Complete(collapse));
        Assert.False(request.Complete(idle));
        Assert.True(request.RequiresPeerOrderRestore);
        Assert.True(request.Complete(retry));
        Assert.False(request.RequiresPeerOrderRestore);
    }

    [Fact]
    public void NewExpansion_InvalidatesOldCallbackWithoutLosingRepair()
    {
        var request = new IdleWidgetZOrderRequest();
        long previous = request.Queue(requirePeerOrderRestore: true);
        request.Invalidate();

        Assert.False(request.Complete(previous));
        Assert.True(request.RequiresPeerOrderRestore);
        long resumed = request.Queue(requirePeerOrderRestore: false);
        Assert.True(request.Complete(resumed));
        Assert.False(request.RequiresPeerOrderRestore);
    }

    [Fact]
    public void IdleRequest_DoesNotBecomeARequiredRepairAfterCompletion()
    {
        var request = new IdleWidgetZOrderRequest();
        Assert.True(request.Complete(request.Queue(requirePeerOrderRestore: true)));
        request.Queue(requirePeerOrderRestore: false);
        Assert.False(request.RequiresPeerOrderRestore);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollapsedUpperCapsule_ReturnsBehindLowerRowsInEitherTopmostBand(bool topMost)
    {
        // Upper row 1 was lifted for hover expansion and stayed above row 5.
        // Its downward shadow must no longer cover row 5 after collapse.
        IntPtr[] desired = Handles(5, 4, 3, 2, 1);
        var observed = new[] { 1, 5, 4, 3, 2 }
            .Select(id => Window(id, topMost: topMost)).ToArray();

        var bands = WidgetPeerOrderBandPolicy.Plan(desired, observed);

        Assert.NotNull(bands);
        Assert.Equal(desired, Assert.Single(bands!));
        Assert.Equal(new IntPtr(1), Assert.Single(
            WidgetPeerOrderMovePlanner.Plan(desired, Handles(1, 5, 4, 3, 2), new IntPtr(99))!).Handle);
    }

    [Fact]
    public void ForeignApplication_RemainsBetweenItsExistingPeerBands()
    {
        var bands = WidgetPeerOrderBandPolicy.Plan(
            Handles(5, 4, 3, 2, 1),
            [Window(1), Window(2), Window(99), Window(3), Window(5), Window(4)]);

        Assert.NotNull(bands);
        Assert.Equal(2, bands!.Count);
        Assert.Equal(Handles(2, 1), bands[0]);
        Assert.Equal(Handles(5, 4, 3), bands[1]);
    }

    [Fact]
    public void TopmostBoundary_IsPreservedEvenWithoutAForeignWindowBetweenPeers()
    {
        var bands = WidgetPeerOrderBandPolicy.Plan(
            Handles(4, 3, 2, 1),
            [Window(1, topMost: true), Window(2, topMost: true), Window(3), Window(4)]);

        Assert.NotNull(bands);
        Assert.Equal(2, bands!.Count);
        Assert.Equal(Handles(2, 1), bands[0]);
        Assert.Equal(Handles(4, 3), bands[1]);
    }

    [Fact]
    public void HiddenOrCloakedApplication_DoesNotSplitVisiblePeers()
    {
        var bands = WidgetPeerOrderBandPolicy.Plan(
            Handles(2, 1), [Window(1), Window(99, visible: false), Window(2)]);

        Assert.Equal(Handles(2, 1), Assert.Single(bands!));
    }

    [Fact]
    public void IncompleteNativeSnapshot_CannotMoveTheRemainingPeers()
    {
        Assert.Null(WidgetPeerOrderBandPolicy.Plan(Handles(2, 1), [Window(1)]));
        Assert.Null(WidgetPeerOrderBandPolicy.Plan(
            Handles(2, 1), [Window(1), Window(2, visible: false)]));
        Assert.Null(WidgetPeerOrderBandPolicy.Plan(
            Handles(2, 1), [Window(1), Window(1), Window(2)]));
    }

    [Fact]
    public void InvalidRequestedHandles_CannotProduceAnAmbiguousPlan()
    {
        Assert.Null(WidgetPeerOrderBandPolicy.Plan(Handles(1, 1), [Window(1)]));
        Assert.Null(WidgetPeerOrderBandPolicy.Plan(Handles(0, 1), [Window(1)]));
        Assert.Empty(WidgetPeerOrderBandPolicy.Plan([], [])!);
    }

    [Fact]
    public void AlreadyOrderedBand_RequiresNoNativeMoves()
    {
        IntPtr[] desired = Handles(3, 2, 1);
        var band = Assert.Single(WidgetPeerOrderBandPolicy.Plan(
            desired, [Window(3), Window(2), Window(1)])!);

        Assert.Empty(WidgetPeerOrderMovePlanner.Plan(band, desired, new IntPtr(99))!);
    }

    private static IntPtr[] Handles(params int[] ids) => ids.Select(id => new IntPtr(id)).ToArray();
    private static WidgetPeerOrderWindow Window(int id, bool visible = true, bool topMost = false) =>
        new(new IntPtr(id), visible, topMost);
}
