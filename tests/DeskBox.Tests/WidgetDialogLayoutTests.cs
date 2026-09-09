using DeskBox.Helpers;
using Windows.Graphics;

namespace DeskBox.Tests;

/// <summary>
/// Widget settings editors used to open as a ContentDialog inside the widget's
/// own XamlRoot. A widget is routinely ~313x326 physical pixels, ContentDialog
/// does not scroll its content, and its width floor comes from shared theme
/// resources — so the margin editor was clipped by the host window and the whole
/// Bottom/Right row disappeared, which is what the report described as "only Top
/// and Left". The editors now live in their own window, so the budget comes from
/// the monitor work area. These tests pin that budget and the placement math.
/// </summary>
public sealed class WidgetDialogLayoutTests
{
    [Fact]
    public void MarginEditorRequest_OnANormalDesktop_GetsItsFullContentBox()
    {
        WidgetDialogViewport viewport = WidgetDialogLayout.ResolveViewport(380, 300, 1920, 1040);

        Assert.Equal(380, viewport.ContentWidth);
        Assert.Equal(300, viewport.ContentHeight);
        Assert.Equal(380 + WidgetDialogLayout.ChromePaddingWidthDips, viewport.WindowWidth);
        Assert.Equal(300 + WidgetDialogLayout.ChromeHeightDips, viewport.WindowHeight);

        // Room for both side columns and the explanatory hint: nothing has to be
        // dropped or scrolled on a normal desktop.
        Assert.False(viewport.PrefersSingleColumn);
        Assert.False(viewport.PrefersCompactText);
    }

    [Fact]
    public void SmallWorkArea_ShrinksTheEditorInsteadOfOverflowingTheScreen()
    {
        WidgetDialogViewport viewport = WidgetDialogLayout.ResolveViewport(380, 300, 300, 260);

        Assert.Equal(300 - WidgetDialogLayout.WorkAreaInsetDips, viewport.WindowWidth);
        Assert.Equal(260 - WidgetDialogLayout.WorkAreaInsetDips, viewport.WindowHeight);
        Assert.True(viewport.PrefersSingleColumn, "236 DIPs cannot hold two input columns");
        Assert.True(viewport.PrefersCompactText, "108 DIPs must go to the inputs, not the hint");
    }

    [Fact]
    public void OversizedRequest_StopsAtTheReadabilityCaps()
    {
        WidgetDialogViewport viewport = WidgetDialogLayout.ResolveViewport(2000, 2000, 3840, 2160);

        Assert.Equal(WidgetDialogLayout.MaximumContentWidthDips, viewport.ContentWidth);
        Assert.Equal(WidgetDialogLayout.MaximumContentHeightDips, viewport.ContentHeight);
    }

    [Fact]
    public void UnmeasuredOrTinyRequest_FallsBackToTheReadableFloor()
    {
        WidgetDialogViewport viewport = WidgetDialogLayout.ResolveViewport(0, double.NaN, 1920, 1040);

        Assert.Equal(WidgetDialogLayout.MinimumContentWidthDips, viewport.ContentWidth);
        Assert.Equal(WidgetDialogLayout.MinimumContentHeightDips, viewport.ContentHeight);
    }

    [Fact]
    public void Placement_CentresTheEditorOverTheWidgetItBelongsTo()
    {
        var workArea = new RectInt32(0, 0, 2560, 1440);
        var widget = new RectInt32(1735, 68, 313, 326);

        RectInt32 bounds = WidgetDialogLayout.ResolveWindowBounds(workArea, widget, 420, 428);

        Assert.Equal(1735 + ((313 - 420) / 2), bounds.X);
        Assert.Equal(68 + ((326 - 428) / 2), bounds.Y);
        Assert.Equal(420, bounds.Width);
        Assert.Equal(428, bounds.Height);
    }

    [Fact]
    public void Placement_KeepsTheEditorOnScreen_ForAWidgetParkedAtTheEdge()
    {
        var workArea = new RectInt32(0, 0, 2560, 1440);
        var widget = new RectInt32(2400, 1300, 313, 42);

        RectInt32 bounds = WidgetDialogLayout.ResolveWindowBounds(workArea, widget, 420, 428);

        Assert.True(bounds.X >= workArea.X, "the editor may not start left of the work area");
        Assert.True(
            bounds.X + bounds.Width <= workArea.X + workArea.Width,
            "the editor may not run past the right edge");
        Assert.True(
            bounds.Y + bounds.Height <= workArea.Y + workArea.Height,
            "the editor may not run past the bottom edge, where the buttons live");
    }

    [Fact]
    public void Placement_UsesTheWidgetsOwnMonitorWorkArea()
    {
        // Secondary monitor to the right of the primary one.
        var workArea = new RectInt32(2560, 0, 1920, 1080);
        var widget = new RectInt32(2600, 40, 313, 326);

        RectInt32 bounds = WidgetDialogLayout.ResolveWindowBounds(workArea, widget, 420, 428);

        Assert.True(bounds.X >= workArea.X + WidgetDialogLayout.WorkAreaMarginPixels);
        Assert.True(bounds.X + bounds.Width <= workArea.X + workArea.Width);
        Assert.True(bounds.Y >= workArea.Y + WidgetDialogLayout.WorkAreaMarginPixels);
    }

    [Fact]
    public void Placement_FillsAWorkAreaTooSmallForTheEditor_WithoutInvertingTheClamp()
    {
        var workArea = new RectInt32(0, 0, 300, 200);
        var widget = new RectInt32(10, 10, 50, 50);

        RectInt32 bounds = WidgetDialogLayout.ResolveWindowBounds(workArea, widget, 420, 428);

        Assert.Equal(new RectInt32(0, 0, 300, 200), bounds);
    }
}
