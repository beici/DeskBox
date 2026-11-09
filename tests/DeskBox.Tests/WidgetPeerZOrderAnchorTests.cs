using System.Runtime.InteropServices;

namespace DeskBox.Tests;

public sealed class WidgetPeerZOrderAnchorTests
{
    private const int PopupStyle = unchecked((int)0x80000000);
    private const int ToolWindowNoActivateStyle = 0x08000080;
    private const int TopMostStyle = 0x00000008;
    private const uint PlacementFlags = Win32Helper.SWP_NOMOVE |
        Win32Helper.SWP_NOSIZE | Win32Helper.SWP_NOACTIVATE |
        Win32Helper.SWP_NOOWNERZORDER;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(
        int extendedStyle, string className, string? windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu,
        IntPtr instance, IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool DestroyWindow(IntPtr window);

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void PeerPlacement_PreservesTheNativeBand(bool peerTopMost, bool anchorTopMost)
    {
        // Hidden native windows exercise Windows' real insert-after semantics
        // without displaying UI or changing the foreground application.
        IntPtr anchor = CreateTestWindow(anchorTopMost);
        IntPtr peer = CreateTestWindow(peerTopMost);
        try
        {
            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.NotEqual(IntPtr.Zero, peer);
            IntPtr insertAfter = Win32Helper.ResolvePeerZOrderAnchor(peer, anchor);

            Assert.True(Win32Helper.SetWindowPos(peer, insertAfter, 0, 0, 0, 0, PlacementFlags));
            Assert.Equal(peerTopMost, Win32Helper.IsWindowTopMost(peer));
            Assert.Equal(anchorTopMost, Win32Helper.IsWindowTopMost(anchor));
        }
        finally
        {
            if (peer != IntPtr.Zero) DestroyWindow(peer);
            if (anchor != IntPtr.Zero) DestroyWindow(anchor);
        }
    }

    [Fact]
    public void TopmostBoundary_ReproducesPromotionAndResolvedAnchorPreventsIt()
    {
        IntPtr lowerTopmost = CreateTestWindow(topMost: true);
        IntPtr anchor = CreateTestWindow(topMost: true);
        IntPtr peer = CreateTestWindow(topMost: false);
        try
        {
            Assert.NotEqual(IntPtr.Zero, lowerTopmost);
            Assert.NotEqual(IntPtr.Zero, anchor);
            Assert.NotEqual(IntPtr.Zero, peer);
            Assert.True(Win32Helper.SetWindowPos(peer, anchor, 0, 0, 0, 0, PlacementFlags));
            Assert.True(Win32Helper.IsWindowTopMost(peer));

            Assert.True(Win32Helper.SetWindowPos(
                peer, Win32Helper.HWND_NOTOPMOST, 0, 0, 0, 0, PlacementFlags));
            IntPtr insertAfter = Win32Helper.ResolvePeerZOrderAnchor(peer, anchor);
            Assert.True(Win32Helper.SetWindowPos(peer, insertAfter, 0, 0, 0, 0, PlacementFlags));
            Assert.False(Win32Helper.IsWindowTopMost(peer));
        }
        finally
        {
            if (peer != IntPtr.Zero) DestroyWindow(peer);
            if (anchor != IntPtr.Zero) DestroyWindow(anchor);
            if (lowerTopmost != IntPtr.Zero) DestroyWindow(lowerTopmost);
        }
    }

    private static IntPtr CreateTestWindow(bool topMost) => CreateWindowExW(
        ToolWindowNoActivateStyle | (topMost ? TopMostStyle : 0),
        "STATIC", null, PopupStyle, 0, 0, 1, 1,
        IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
}
