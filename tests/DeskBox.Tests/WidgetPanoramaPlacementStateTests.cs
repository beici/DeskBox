using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetPanoramaPlacementStateTests
{
    [Fact]
    public void FirstPlacement_InitializesDimensionsAndOriginIncludingZero()
    {
        var state = new WidgetPanoramaPlacementState();

        Assert.Equal(WidgetPanoramaPlacementChanges.All, state.Update(1920, 1080, 0, 0));
        Assert.Equal(WidgetPanoramaPlacementChanges.None, state.Update(1920, 1080, 0, 0));
    }

    [Fact]
    public void ContinuousWindowMovement_OnlyUpdatesRenderTranslation()
    {
        var state = new WidgetPanoramaPlacementState();
        state.Update(3840, 2160, 0, 0);
        int layoutWrites = 0;
        int translationWrites = 0;

        for (int position = 1; position <= 1000; position++)
        {
            WidgetPanoramaPlacementChanges changes = state.Update(3840, 2160, -position, -position / 2d);
            if ((changes & WidgetPanoramaPlacementChanges.Width) != 0) layoutWrites++;
            if ((changes & WidgetPanoramaPlacementChanges.Height) != 0) layoutWrites++;
            if ((changes & WidgetPanoramaPlacementChanges.Left) != 0) translationWrites++;
            if ((changes & WidgetPanoramaPlacementChanges.Top) != 0) translationWrites++;
        }

        Assert.Equal(0, layoutWrites);
        Assert.Equal(2000, translationWrites);
        Assert.Equal(WidgetPanoramaPlacementChanges.None, state.Update(3840, 2160, -1000, -500));
    }

    [Fact]
    public void HorizontalMovement_DoesNotRewriteOtherProperties()
    {
        var state = new WidgetPanoramaPlacementState();
        state.Update(1920, 1080, -500, -100);

        Assert.Equal(WidgetPanoramaPlacementChanges.Left, state.Update(1920, 1080, -501, -100));
        Assert.Equal(WidgetPanoramaPlacementChanges.Left, state.Update(1920, 1080, 0, -100));
    }

    [Fact]
    public void ImageAspectRatioChange_UpdatesOnlyChangedDimension()
    {
        var state = new WidgetPanoramaPlacementState();
        state.Update(1920, 1080, 0, 0);

        Assert.Equal(WidgetPanoramaPlacementChanges.Height, state.Update(1920, 1440, 0, 0));
    }

    [Theory]
    [InlineData(1920, 1080, 1d, 0, 0)]
    [InlineData(3840, 2160, 2d, 1920, 0)]
    [InlineData(2560, 1440, 1.5d, -2560, -1440)]
    public void PerMonitorPlacement_PreservesThePhysicalSampleAcrossDpiAndNegativeOrigins(
        double monitorWidth, double monitorHeight, double dpi, double monitorX, double monitorY)
    {
        var state = new WidgetPanoramaPlacementState();
        const double windowOffsetX = 175;
        const double windowOffsetY = 230;
        var fit = WidgetBackgroundPanoramaCalculator.ComputeMonitorFit(4000, 3000, monitorWidth, monitorHeight);
        double left = (monitorX + fit.OffsetX - (monitorX + windowOffsetX)) / dpi;
        double top = (monitorY + fit.OffsetY - (monitorY + windowOffsetY)) / dpi;

        Assert.Equal(WidgetPanoramaPlacementChanges.All,
            state.Update(fit.Width / dpi, fit.Height / dpi, left, top));
        Assert.Equal(windowOffsetX - fit.OffsetX, -left * dpi, 6);
        Assert.Equal(windowOffsetY - fit.OffsetY, -top * dpi, 6);
        Assert.Equal(WidgetPanoramaPlacementChanges.Translation,
            state.Update(fit.Width / dpi, fit.Height / dpi, left - 1 / dpi, top - 1 / dpi));
    }

    [Fact]
    public void MovingToAnotherDpi_ReappliesFittedSizeAndTranslation()
    {
        var state = new WidgetPanoramaPlacementState();
        state.Update(3840, 2160, -1200, -400);

        Assert.Equal(WidgetPanoramaPlacementChanges.All, state.Update(1920, 1080, -600, -200));
    }

    [Fact]
    public void SourceReplacementOrRecovery_ReappliesEvenIdenticalGeometry()
    {
        var state = new WidgetPanoramaPlacementState();
        state.Update(1920, 1080, -100, -50);

        state.Reset();

        Assert.Equal(WidgetPanoramaPlacementChanges.All, state.Update(1920, 1080, -100, -50));
        Assert.Equal(WidgetPanoramaPlacementChanges.None, state.Update(1920, 1080, -100, -50));
    }

    [Theory]
    [InlineData(0, 1080, 0, 0)]
    [InlineData(1920, -1, 0, 0)]
    [InlineData(double.NaN, 1080, 0, 0)]
    [InlineData(1920, double.PositiveInfinity, 0, 0)]
    [InlineData(1920, 1080, double.NegativeInfinity, 0)]
    [InlineData(1920, 1080, 0, double.NaN)]
    public void IncompleteGeometry_DoesNotPoisonThePreviousPlacement(double width, double height, double left, double top)
    {
        var state = new WidgetPanoramaPlacementState();
        state.Update(1920, 1080, -100, -50);

        Assert.Equal(WidgetPanoramaPlacementChanges.None, state.Update(width, height, left, top));
        Assert.Equal(WidgetPanoramaPlacementChanges.None, state.Update(1920, 1080, -100, -50));
        Assert.Equal(WidgetPanoramaPlacementChanges.Left, state.Update(1920, 1080, -101, -50));
    }
}
