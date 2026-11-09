using DeskBox.Services;
using Windows.Foundation;

namespace DeskBox.Tests;

public sealed class ScreenshotSelectionSnapshotTests
{
    [Fact]
    public void HitTest_PreservesFrontToBackOrder()
    {
        ScreenshotSelectionTarget[] targets =
        [new(1, new Rect(20, 20, 80, 80)), new(2, new Rect(0, 0, 200, 200))];
        var selected = ScreenshotSelectionSnapshot.FindAt(targets, new Point(40, 40), new Rect(0, 0, 200, 200));
        Assert.Equal((nint)1, selected.Window);
        Assert.Equal(targets[0].Bounds, selected.Bounds);
    }

    [Fact]
    public void HitTest_ClipsSpanningWindowToFrozenMonitor()
    {
        var monitor = new Rect(-1920, 0, 1920, 1080);
        ScreenshotSelectionTarget[] targets = [new(1, new Rect(-500, -100, 1000, 1300))];
        var selected = ScreenshotSelectionSnapshot.FindAt(targets, new Point(-100, 400), monitor);
        Assert.Equal(new Rect(-500, 0, 500, 1080), selected.Bounds);
    }

    [Fact]
    public void HitTest_UsesExclusiveRightAndBottomEdges()
    {
        ScreenshotSelectionTarget[] targets =
        [new(1, new Rect(0, 0, 100, 100)), new(2, new Rect(100, 0, 100, 100))];
        Assert.Equal((nint)2, ScreenshotSelectionSnapshot.FindAt(
            targets, new Point(100, 50), new Rect(0, 0, 200, 100)).Window);
    }

    [Fact]
    public void HitTest_DesktopGapFallsBackToFullMonitor()
    {
        var monitor = new Rect(0, 0, 200, 100);
        ScreenshotSelectionTarget[] targets = [new(1, new Rect(20, 20, 50, 50))];
        var selected = ScreenshotSelectionSnapshot.FindAt(targets, new Point(190, 90), monitor);
        Assert.Equal((nint)0, selected.Window);
        Assert.Equal(monitor, selected.Bounds);
    }
}
