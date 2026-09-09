using Windows.Graphics;

namespace DeskBox.Helpers;

/// <summary>
/// The size a widget tool dialog is allowed to take: the window itself plus the
/// content box left inside it once the title row, button row and padding are
/// accounted for.
/// </summary>
public readonly record struct WidgetDialogViewport(
    double WindowWidth,
    double WindowHeight,
    double ContentWidth,
    double ContentHeight)
{
    /// <summary>
    /// True when the content box is too narrow for two input columns, so
    /// callers must stack their fields instead of letting one column be cut.
    /// </summary>
    public bool PrefersSingleColumn =>
        ContentWidth < WidgetDialogLayout.TwoColumnContentWidthDips;

    /// <summary>
    /// True when the height budget is tight enough that explanatory secondary
    /// text should give its space to the inputs.
    /// </summary>
    public bool PrefersCompactText =>
        ContentHeight < WidgetDialogLayout.SecondaryTextContentHeightDips;
}

/// <summary>
/// Sizing and placement rules for the widget tool dialog host.
///
/// Widget settings used to open as a <c>ContentDialog</c> in the widget's own
/// XamlRoot. A widget is only 313x326 physical pixels in a typical layout and
/// can be as small as 50x50 DIPs, while ContentDialog does not scroll its
/// content and takes its width from shared theme resources — so the dialog was
/// clipped by the host window and whole field rows disappeared (the margin
/// entry looked like it only supported Top and Left). The editor now lives in
/// its own small window, which means the budget is set by the monitor work
/// area instead of the widget. Both calculations are pure so the clamps stay
/// testable without a XAML host.
/// </summary>
public static class WidgetDialogLayout
{
    /// <summary>
    /// Vertical space the tool dialog reserves outside the content: title row,
    /// the button row and the window padding above and below them.
    /// </summary>
    public const double ChromeHeightDips = 128;

    /// <summary>Left plus right window padding of the same host.</summary>
    public const double ChromePaddingWidthDips = 40;

    /// <summary>
    /// Space kept between the tool dialog and the work-area edges so the window
    /// never looks glued to a screen border.
    /// </summary>
    public const double WorkAreaInsetDips = 24;

    public const double MinimumContentWidthDips = 200;
    public const double MaximumContentWidthDips = 380;
    public const double MinimumContentHeightDips = 132;

    /// <summary>
    /// Tallest content box a tool dialog may claim. The colour picker (spectrum,
    /// value slider and the RGB entries) is the tallest editor, and it has to fit
    /// without scrolling on a normal desktop.
    /// </summary>
    public const double MaximumContentHeightDips = 640;
    public const double TwoColumnContentWidthDips = 264;
    public const double SecondaryTextContentHeightDips = 260;

    /// <summary>Minimum gap in physical pixels between the window and the work-area edge.</summary>
    public const int WorkAreaMarginPixels = 8;

    /// <summary>
    /// Resolves the content box a tool dialog gets for the requested size: the
    /// request is first clamped into the readable policy range, then into what
    /// the monitor work area can actually show.
    /// </summary>
    public static WidgetDialogViewport ResolveViewport(
        double desiredContentWidthDips,
        double desiredContentHeightDips,
        double workAreaWidthDips,
        double workAreaHeightDips)
    {
        double contentWidth = ResolveContentExtent(
            desiredContentWidthDips,
            MinimumContentWidthDips,
            MaximumContentWidthDips,
            workAreaWidthDips,
            ChromePaddingWidthDips);
        double contentHeight = ResolveContentExtent(
            desiredContentHeightDips,
            MinimumContentHeightDips,
            MaximumContentHeightDips,
            workAreaHeightDips,
            ChromeHeightDips);

        return new WidgetDialogViewport(
            contentWidth + ChromePaddingWidthDips,
            contentHeight + ChromeHeightDips,
            contentWidth,
            contentHeight);
    }

    /// <summary>
    /// Centres the tool dialog over the widget it belongs to and keeps it fully
    /// inside the work area, so the editor is never half off-screen for a widget
    /// parked against a monitor edge. All values are physical pixels.
    /// </summary>
    public static RectInt32 ResolveWindowBounds(
        RectInt32 workArea,
        RectInt32 ownerBounds,
        int windowWidth,
        int windowHeight)
    {
        int width = Math.Max(1, Math.Min(windowWidth, workArea.Width));
        int height = Math.Max(1, Math.Min(windowHeight, workArea.Height));

        int centeredX = ownerBounds.X + ((ownerBounds.Width - width) / 2);
        int centeredY = ownerBounds.Y + ((ownerBounds.Height - height) / 2);

        return new RectInt32(
            ClampAxis(centeredX, width, workArea.X, workArea.Width),
            ClampAxis(centeredY, height, workArea.Y, workArea.Height),
            width,
            height);
    }

    private static double ResolveContentExtent(
        double desired,
        double minimum,
        double maximum,
        double workAreaExtent,
        double chrome)
    {
        double request = double.IsFinite(desired) ? desired : minimum;
        double policy = Math.Clamp(request, minimum, maximum);

        // Nothing may push the window past the screen, but a work area too small
        // for the floor keeps the floor: the content scrolls inside the host
        // rather than collapsing to an unusable sliver.
        double available = (double.IsFinite(workAreaExtent) ? workAreaExtent : 0)
            - WorkAreaInsetDips
            - chrome;
        return available > 0 ? Math.Min(policy, available) : policy;
    }

    private static int ClampAxis(int origin, int extent, int workAreaOrigin, int workAreaExtent)
    {
        int minimum = workAreaOrigin + WorkAreaMarginPixels;
        int maximum = workAreaOrigin + workAreaExtent - extent - WorkAreaMarginPixels;
        if (maximum < minimum)
        {
            // The window fills the axis: sit flush with the work-area origin
            // instead of inverting the clamp.
            return workAreaOrigin + Math.Max(0, (workAreaExtent - extent) / 2);
        }

        return Math.Clamp(origin, minimum, maximum);
    }
}
