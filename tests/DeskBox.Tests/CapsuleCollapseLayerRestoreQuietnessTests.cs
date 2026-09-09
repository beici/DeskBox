using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Locks the quiet end-of-collapse layer restore (DEF-061).
/// <para>
/// Every capsule collapse used to finish by sinking the widget with
/// HWND_BOTTOM. That call is the one Z-order primitive which must let Windows
/// move the shared Explorer owner - blocking it drops the widget under the
/// wallpaper (DEF-058) - and moving the owner re-stacks every widget attached
/// to the desktop view, so all of them re-sample their acrylic backdrop. Users
/// saw the whole group dim for a frame at the end of every collapse, and the
/// same DWM work made the tail of the collapse animation stutter. A widget that
/// only needs to rejoin a band it already belongs to is now inserted directly
/// below its idle-order predecessor with one owner-preserving move.
/// </para>
/// </summary>
public sealed class CapsuleCollapseLayerRestoreQuietnessTests
{
    [Fact]
    public void CollapseRestore_PrefersTheRestingBandRejoinOverTheBottomSink()
    {
        string collapse = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Collapse.cs")));
        string restore = SliceMethod(
            collapse,
            "    private void RestoreLayerAfterExpandedState()",
            "    private void ScheduleSmartCollapse(");

        int rejoin = restore.IndexOf(
            "TryReturnWidgetToRestingBand(HWnd)",
            StringComparison.Ordinal);
        int sink = restore.IndexOf(
            "WidgetLayerService.MoveToDesktopBottom(HWnd)",
            StringComparison.Ordinal);
        Assert.True(rejoin > 0, "The quiet rejoin attempt is gone.");
        Assert.True(sink > rejoin, "The bottom sink must stay a fallback only.");

        // Already-resting widgets must not pay for any Z-order call at all.
        Assert.Contains("IsPhysicallyAtDesktopBottom()", restore, StringComparison.Ordinal);
    }

    [Fact]
    public void RestingBandRejoin_NeverRepositionsTheSharedExplorerOwner()
    {
        string layer = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetLayerService.cs")));
        string rejoin = SliceMethod(
            layer,
            "    internal static bool TryReturnToRestingBandBelow(",
            "    public static IntPtr ClearTopMostPreservingForeground(");

        // Owner attach without the bottom move, then one insert-after.
        Assert.Contains("placeAtBottom: false", rejoin, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.PlaceWindowBelow(", rejoin, StringComparison.Ordinal);
        Assert.DoesNotContain("HWND_BOTTOM", rejoin, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveToDesktopBottom", rejoin, StringComparison.Ordinal);

        // A topmost anchor would leave the widget above every application
        // window, which is exactly the band violation this path must not cause.
        Assert.Contains(
            "Win32Helper.IsWindowTopMost(insertAfter)",
            rejoin,
            StringComparison.Ordinal);

        string helper = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Helpers/Win32Helper.cs")));
        string primitive = SliceMethod(
            helper,
            "    public static bool PlaceWindowBelow(",
            "    public static bool IsWindowTopMost(");
        Assert.Contains("ZOrderRaiseFlags", primitive, StringComparison.Ordinal);
        Assert.DoesNotContain("ZOrderBottomFlags", primitive, StringComparison.Ordinal);
    }

    [Fact]
    public void RestingBandAnchor_IsTheSlotTheIdleOrderGivesTheWidget()
    {
        string manager = NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs")));
        string anchor = SliceMethod(
            manager,
            "    internal bool TryReturnWidgetToRestingBand(",
            "    public void BringAllVisibleWidgetsToFront(");

        // Landing in the final slot is what keeps the peer normalization that
        // runs next from issuing a second move on the same window.
        Assert.Contains(
            "GetWindowsInIdleHighestFirstOrder(",
            anchor,
            StringComparison.Ordinal);
        Assert.Contains("ordered[index - 1].WindowHandle", anchor, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.GW_HWNDPREV", anchor, StringComparison.Ordinal);

        // Tray-raised and desktop-pinned sessions own the band themselves.
        Assert.Contains("_widgetsRaisedFromTray", anchor, StringComparison.Ordinal);
        Assert.Contains("UsesDesktopPinnedMode()", anchor, StringComparison.Ordinal);
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
}
