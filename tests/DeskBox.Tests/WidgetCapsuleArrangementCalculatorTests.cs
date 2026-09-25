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

        // FLAY-01: the overflow ladder gives up the spacing first (8 → 0) and
        // compresses the capsule heights against the full work-area length
        // (3 × 80 → 67/67/66, still proportional), instead of compressing
        // harder against the spacing-reduced budget while keeping the gaps.
        Assert.Equal(new RectInt32(20, 0, 140, 67), result["one"]);
        Assert.Equal(new RectInt32(20, 67, 140, 67), result["two"]);
        Assert.Equal(new RectInt32(20, 134, 140, 66), result["three"]);
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
    public void Horizontal_ModerateOverflow_SqueezesSpacingBeforeSizes()
    {
        // FLAY-01: on overflow the gaps give way before the capsule sizes —
        // both capsules keep their requested width and the spacing collapses
        // to zero instead of shrinking the capsules (the old order compressed
        // the sizes first, the reverse of the documented ladder).
        var items = new[]
        {
            new WidgetCapsuleArrangementItem("one", 120, 40),
            new WidgetCapsuleArrangementItem("two", 120, 40)
        };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 250, 200),
                new PointInt32(0, 10),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionHorizontal,
                20);

        Assert.Equal(new RectInt32(0, 10, 120, 40), result["one"]);
        Assert.Equal(new RectInt32(120, 10, 120, 40), result["two"]);
    }

    [Fact]
    public void Vertical_DeepOverflow_CompressesSizesToFillAfterSpacingIsGone()
    {
        // FLAY-01 ladder, step ③: after the spacing is gone the sizes are
        // compressed proportionally toward the floor. The arranged sizes must
        // still sum to exactly what the work area holds (caller-visible
        // invariant), and no capsule may be dropped for a solvable overload.
        var items = new[]
        {
            new WidgetCapsuleArrangementItem("one", 100, 200),
            new WidgetCapsuleArrangementItem("two", 100, 200),
            new WidgetCapsuleArrangementItem("three", 100, 200)
        };

        IReadOnlyDictionary<string, RectInt32> result =
            WidgetCapsuleArrangementCalculator.Calculate(
                items,
                new RectInt32(0, 0, 300, 400),
                new PointInt32(10, 0),
                WidgetPositionAnchors.LeftTop,
                SettingsService.WidgetCapsuleBarDirectionVertical,
                10);

        Assert.True(result.ContainsKey("one") && result.ContainsKey("two") && result.ContainsKey("three"),
            "a mid-size overload must compress sizes, not drop capsules");
        RectInt32[] ordered = result.Values.OrderBy(bounds => bounds.Y).ToArray();
        int totalHeight = ordered.Sum(bounds => bounds.Height);
        Assert.True(totalHeight <= 400, $"arranged sizes must fit the work area, got {totalHeight}");
        Assert.True(totalHeight >= 399,
            "sizes must be compressed to fill the work area, not shrunk arbitrarily");
        for (int index = 1; index < ordered.Length; index++)
        {
            Assert.Equal(
                ordered[index - 1].Y + ordered[index - 1].Height,
                ordered[index].Y);
        }
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
