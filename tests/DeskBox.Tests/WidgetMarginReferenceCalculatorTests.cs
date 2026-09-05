using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Tests;

/// <summary>
/// The margin reference model: a side's distance is measured to the closest
/// object beside it - another widget or a desktop icon - and only falls back to
/// the monitor work area when that side has nothing next to it. Measuring
/// against the screen edge instead produced numbers users could not act on,
/// because the gap the eye judges is the one to the neighbour.
/// </summary>
public sealed class WidgetMarginReferenceCalculatorTests
{
    private static readonly RectInt32 WorkArea = new(0, 0, 1920, 1040);

    [Fact]
    public void SideWithNothingBesideIt_FallsBackToTheWorkAreaEdge()
    {
        var bounds = new RectInt32(600, 400, 300, 200);

        WidgetMarginEdge left = WidgetMarginReferenceCalculator.ResolveMargin(
            bounds,
            WidgetMarginSide.Left,
            [],
            WorkArea,
            8);

        Assert.Equal(WidgetMarginReferenceKind.WorkArea, left.Kind);
        Assert.Equal(600, left.Distance);
    }

    [Fact]
    public void DesktopIconBesideTheWidget_BecomesTheReference()
    {
        var bounds = new RectInt32(600, 400, 300, 200);
        // Icon column ending 40px to the left, vertically overlapping.
        var icon = new RectInt32(480, 420, 80, 90);

        WidgetMarginEdge left = WidgetMarginReferenceCalculator.ResolveMargin(
            bounds,
            WidgetMarginSide.Left,
            [new WidgetMarginNeighbour(icon, WidgetMarginReferenceKind.DesktopIcon)],
            WorkArea,
            8);

        Assert.Equal(WidgetMarginReferenceKind.DesktopIcon, left.Kind);
        Assert.Equal(40, left.Distance);
    }

    [Fact]
    public void ClosestNeighbourWins_RegardlessOfKind()
    {
        var bounds = new RectInt32(600, 400, 300, 200);
        var icon = new RectInt32(300, 420, 80, 90);
        var widget = new RectInt32(400, 410, 170, 120);

        WidgetMarginEdge left = WidgetMarginReferenceCalculator.ResolveMargin(
            bounds,
            WidgetMarginSide.Left,
            [
                new WidgetMarginNeighbour(icon, WidgetMarginReferenceKind.DesktopIcon),
                new WidgetMarginNeighbour(widget, WidgetMarginReferenceKind.Widget)
            ],
            WorkArea,
            8);

        // The widget's right edge (570) is closer than the icon's (380).
        Assert.Equal(WidgetMarginReferenceKind.Widget, left.Kind);
        Assert.Equal(30, left.Distance);
    }

    [Fact]
    public void CornerTouchingRectangle_IsNotBesideTheWidget()
    {
        var bounds = new RectInt32(600, 400, 300, 200);
        // Sits entirely above the widget's top edge, so nothing on the left
        // side overlaps it vertically beyond the tolerance.
        var icon = new RectInt32(480, 320, 80, 84);

        WidgetMarginEdge left = WidgetMarginReferenceCalculator.ResolveMargin(
            bounds,
            WidgetMarginSide.Left,
            [new WidgetMarginNeighbour(icon, WidgetMarginReferenceKind.DesktopIcon)],
            WorkArea,
            8);

        Assert.Equal(WidgetMarginReferenceKind.WorkArea, left.Kind);
    }

    [Theory]
    [InlineData(WidgetMarginSide.Left, 20, 500)]
    [InlineData(WidgetMarginSide.Top, 25, 410)]
    public void ShiftedOrigin_PutsTheSideAtTheRequestedDistance(
        WidgetMarginSide side,
        int margin,
        int expectedCoordinate)
    {
        var bounds = new RectInt32(600, 400, 300, 200);
        var neighbours = new[]
        {
            // Left reference at x=480, top reference at y=385.
            new WidgetMarginNeighbour(
                new RectInt32(400, 420, 80, 90),
                WidgetMarginReferenceKind.DesktopIcon),
            new WidgetMarginNeighbour(
                new RectInt32(620, 300, 90, 85),
                WidgetMarginReferenceKind.DesktopIcon)
        };

        PointInt32? origin = WidgetMarginReferenceCalculator.ResolveShiftedOrigin(
            bounds,
            side,
            margin,
            neighbours,
            WorkArea,
            8);

        Assert.NotNull(origin);
        Assert.Equal(
            expectedCoordinate,
            side == WidgetMarginSide.Left ? origin!.Value.X : origin!.Value.Y);
    }

    [Fact]
    public void ShiftedOrigin_ReturnsNullWhenTheWidgetAlreadySitsThere()
    {
        var bounds = new RectInt32(600, 400, 300, 200);
        var icon = new RectInt32(480, 420, 80, 90);

        PointInt32? origin = WidgetMarginReferenceCalculator.ResolveShiftedOrigin(
            bounds,
            WidgetMarginSide.Left,
            40,
            [new WidgetMarginNeighbour(icon, WidgetMarginReferenceKind.DesktopIcon)],
            WorkArea,
            8);

        Assert.Null(origin);
    }

    [Theory]
    [InlineData(1.0, 8)]
    [InlineData(1.25, 10)]
    [InlineData(1.5, 12)]
    [InlineData(2.0, 16)]
    [InlineData(0, 8)]
    public void OverlapTolerance_ScalesWithTheMonitor(double dpiScale, int expected)
    {
        Assert.Equal(
            expected,
            WidgetMarginReferenceCalculator.ResolveParallelOverlapTolerance(dpiScale));
    }

    [Fact]
    public void RightAndBottomSides_MeasureTowardsTheNeighbourOrigin()
    {
        var bounds = new RectInt32(600, 400, 300, 200);
        var neighbours = new[]
        {
            new WidgetMarginNeighbour(
                new RectInt32(950, 420, 80, 90),
                WidgetMarginReferenceKind.DesktopIcon),
            new WidgetMarginNeighbour(
                new RectInt32(620, 640, 90, 85),
                WidgetMarginReferenceKind.Widget)
        };

        WidgetMarginEdge right = WidgetMarginReferenceCalculator.ResolveMargin(
            bounds, WidgetMarginSide.Right, neighbours, WorkArea, 8);
        WidgetMarginEdge bottom = WidgetMarginReferenceCalculator.ResolveMargin(
            bounds, WidgetMarginSide.Bottom, neighbours, WorkArea, 8);

        Assert.Equal(WidgetMarginReferenceKind.DesktopIcon, right.Kind);
        Assert.Equal(50, right.Distance);
        Assert.Equal(WidgetMarginReferenceKind.Widget, bottom.Kind);
        Assert.Equal(40, bottom.Distance);
    }
}
