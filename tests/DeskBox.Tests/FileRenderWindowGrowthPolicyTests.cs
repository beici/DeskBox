using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class FileRenderWindowGrowthPolicyTests
{
    [Fact]
    public void EnlargedViewport_FillsOnceBeforeUsingTheNewExtent()
    {
        // The old extent still looks near the end while a resize raises the
        // prefix from 30 to 180. Do not immediately append another 64 items
        // and cancel the hydration pass that the first request just started.
        int target = FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
            renderedCount: 30, viewportMinimum: 180, canGrow: true, isNearRenderedEnd: true);
        Assert.Equal(180, target);

        // A settled layout after coverage reports enough scrollable content.
        Assert.Equal(target, FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
            renderedCount: target, viewportMinimum: 180, canGrow: true, isNearRenderedEnd: false));
    }

    [Theory]
    [InlineData(30, 10, 46)]
    [InlineData(30, 30, 60)]
    [InlineData(180, 180, 244)]
    [InlineData(1000, 180, 1064)]
    [InlineData(int.MaxValue - 10, 180, int.MaxValue)]
    public void ScrollingNearTheEnd_PrefetchesABoundedPage(
        int renderedCount, int viewportMinimum, int expected)
    {
        Assert.Equal(expected, FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
            renderedCount, viewportMinimum, canGrow: true, isNearRenderedEnd: true));
    }

    [Theory]
    [InlineData(0, 180)]
    [InlineData(250, 30)]
    public void FullOrEmptyFolder_PreservesTheViewportBudgetWithoutPrefetch(
        int renderedCount, int viewportMinimum)
    {
        Assert.Equal(viewportMinimum, FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
            renderedCount, viewportMinimum, canGrow: false, isNearRenderedEnd: true));
    }

    [Fact]
    public void ScrollingAwayFromTheEnd_DoesNotRaiseTheBudget()
    {
        Assert.Equal(30, FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
            renderedCount: 500, viewportMinimum: 30, canGrow: true, isNearRenderedEnd: false));
    }

    [Theory]
    [InlineData(-1, 30)]
    [InlineData(30, 0)]
    [InlineData(30, -1)]
    public void InvalidCounts_AreRejected(int renderedCount, int viewportMinimum)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
                renderedCount, viewportMinimum, canGrow: true, isNearRenderedEnd: true));
    }
}
