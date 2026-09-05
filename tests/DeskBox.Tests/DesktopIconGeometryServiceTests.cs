using DeskBox.Services;
using Windows.Graphics;
using Xunit.Abstractions;

namespace DeskBox.Tests;

/// <summary>
/// Smoke coverage for the cross-process desktop icon reader. The assertions
/// stay environment independent - a build agent has no desktop icons - but the
/// reported count is what confirms the LVM round trip on a real session.
/// </summary>
public sealed class DesktopIconGeometryServiceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Fact]
    public void GetIconRects_NeverThrowsAndNeverReturnsDegenerateRectangles()
    {
        DesktopIconGeometryService.Invalidate();
        IReadOnlyList<RectInt32> rects = DesktopIconGeometryService.GetIconRects();

        Assert.NotNull(rects);
        _output.WriteLine($"desktop icon rects: {rects.Count}");
        foreach (RectInt32 rect in rects)
        {
            Assert.True(rect.Width > 0, "An icon rectangle must have a positive width.");
            Assert.True(rect.Height > 0, "An icon rectangle must have a positive height.");
        }

        Assert.True(
            rects.Count <= DesktopIconGeometryService.MaximumInspectedIcons,
            "The reader must stay inside its inspection cap.");
    }

    [Fact]
    public void GetIconRects_ServesRepeatedCallsFromItsCache()
    {
        DesktopIconGeometryService.Invalidate();
        IReadOnlyList<RectInt32> first = DesktopIconGeometryService.GetIconRects();
        IReadOnlyList<RectInt32> second = DesktopIconGeometryService.GetIconRects();

        // A margin dialog re-reads the reference geometry on every keystroke, so
        // the cached instance must be handed back rather than re-marshalled.
        Assert.Same(first, second);
    }
}
