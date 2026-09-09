using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Tests;

public sealed class WidgetCapsuleArrangementCalculatorTests
{
    [Fact]
    public void Horizontal_ArrangesOneRowWithSharedHeight()
    {
        var items = new[]
        {
            new WidgetCapsuleArrangementItem("one", 120, 40),
            new WidgetCapsuleArrangementItem("two", 180, 52)
        };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 800, 600),
                new PointInt32(40, 30),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionHorizontal,
                8);

        Assert.Equal(new RectInt32(40, 30, 120, 52), result["one"]);
        Assert.Equal(new RectInt32(168, 30, 180, 52), result["two"]);
    }

    [Fact]
    public void Horizontal_OverflowCompressesInsteadOfWrapping()
    {
        var items = new[]
        {
            new WidgetCapsuleArrangementItem("one", 120, 40),
            new WidgetCapsuleArrangementItem("two", 120, 52),
            new WidgetCapsuleArrangementItem("three", 120, 42)
        };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 300, 200),
                new PointInt32(290, 190),
                WidgetPositionAnchors.RightBottom,
                SettingsService.WidgetCapsuleBarDirectionHorizontal,
                10);

        RectInt32[] bounds = items.Select(item => result[item.Id]).ToArray();
        Assert.All(bounds, item => Assert.Equal(138, item.Y));
        Assert.Equal(0, bounds.Min(item => item.X));
        Assert.Equal(300, bounds.Max(item => item.X + item.Width));
        Assert.True(bounds[0].X > bounds[1].X);
        Assert.True(bounds[1].X > bounds[2].X);
    }

    [Fact]
    public void Vertical_ArrangesOneColumnWithSharedWidth()
    {
        var items = new[]
        {
            new WidgetCapsuleArrangementItem("one", 100, 80),
            new WidgetCapsuleArrangementItem("two", 140, 80),
            new WidgetCapsuleArrangementItem("three", 90, 80)
        };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 500, 200),
                new PointInt32(20, 10),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionVertical,
                8);

        Assert.Equal(new RectInt32(20, 0, 140, 62), result["one"]);
        Assert.Equal(new RectInt32(20, 70, 140, 61), result["two"]);
        Assert.Equal(new RectInt32(20, 139, 140, 61), result["three"]);
    }

    [Fact]
    public void Vertical_RightBottomAnchorExtendsUpAndStaysInsideWorkArea()
    {
        var items = new[]
        {
            new WidgetCapsuleArrangementItem("one", 100, 60),
            new WidgetCapsuleArrangementItem("two", 120, 60)
        };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 500, 200),
                new PointInt32(480, 190),
                WidgetPositionAnchors.RightBottom,
                SettingsService.WidgetCapsuleBarDirectionVertical,
                8);

        Assert.Equal(new RectInt32(360, 130, 120, 60), result["one"]);
        Assert.Equal(new RectInt32(360, 62, 120, 60), result["two"]);
    }

    [Fact]
    public void OversizedItemIsClampedInsideTheWorkArea()
    {
        var items = new[] { new WidgetCapsuleArrangementItem("one", 900, 700) };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(100, 50, 320, 180),
                new PointInt32(-500, 900),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionHorizontal,
                8);

        Assert.Equal(new RectInt32(100, 50, 320, 180), result["one"]);
    }

    [Fact]
    public void TinyWorkArea_TruncatesOverflowingCapsulesInsteadOfSpillingOut()
    {
        // DEF-026 (LAY-04): count × 1px + spacing > work-area length used to
        // overflow silently. The bar must fit: dropped tail capsules are not
        // arranged (they fall back to free placement upstream), arranged ones
        // stay inside the work area, and spacing is squeezed before sizes.
        var items = Enumerable.Range(0, 80)
            .Select(index => new WidgetCapsuleArrangementItem($"c{index}", 120, 40))
            .ToArray();

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 50, 200),
                new PointInt32(25, 10),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionHorizontal,
                10);

        Assert.True(result.Count < items.Length,
            "an un-fittable bar must drop tail capsules instead of overflowing");
        Assert.All(result.Values, bounds =>
        {
            Assert.True(bounds.X >= 0 && bounds.X + bounds.Width <= 50,
                $"arranged capsule must stay inside the work area, got x={bounds.X} w={bounds.Width}");
            Assert.True(bounds.Y >= 0 && bounds.Y + bounds.Height <= 200);
        });
        // Arranged capsules are contiguous: no silent gaps from dropped
        // middle items — the tail is what gets truncated.
        RectInt32[] ordered = result.Values.OrderBy(b => b.X).ToArray();
        for (int index = 1; index < ordered.Length; index++)
        {
            Assert.True(ordered[index].X >= ordered[index - 1].X + ordered[index - 1].Width,
                "arranged capsules must not overlap");
        }
    }

    [Fact]
    public void TinyWorkArea_VerticalOrientation_AlsoBounded()
    {
        var items = Enumerable.Range(0, 80)
            .Select(index => new WidgetCapsuleArrangementItem($"c{index}", 120, 40))
            .ToArray();

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 200, 30),
                new PointInt32(10, 15),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionVertical,
                6);

        Assert.True(result.Count < items.Length);
        Assert.All(result.Values, bounds =>
        {
            Assert.True(bounds.X >= 0 && bounds.X + bounds.Width <= 200);
            Assert.True(bounds.Y >= 0 && bounds.Y + bounds.Height <= 30,
                $"arranged capsule must stay inside the work area, got y={bounds.Y} h={bounds.Height}");
        });
    }
}
