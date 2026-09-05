using DeskBox.Helpers;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace DeskBox.Views;

public abstract partial class WidgetWindowBase
{
    /// <summary>
    /// Resolves how much room a widget-owned editor may take. The budget comes
    /// from the monitor work area, not from the widget: a widget is routinely
    /// only ~313x326 physical pixels, which is far too small to host a settings
    /// dialog without clipping it.
    /// </summary>
    protected WidgetDialogViewport ResolveToolDialogViewport(
        double desiredContentWidthDips,
        double desiredContentHeightDips)
    {
        RectInt32 ownerBounds = GetToolDialogOwnerBounds();
        RectInt32 workArea = ResolveWorkArea(ownerBounds);
        double scale = GetToolDialogScale();
        return WidgetDialogLayout.ResolveViewport(
            desiredContentWidthDips,
            desiredContentHeightDips,
            workArea.Width / scale,
            workArea.Height / scale);
    }

    /// <summary>
    /// Shows a widget-owned editor in its own always-on-top window, centred over
    /// the widget and clamped into the work area. Returns true when the primary
    /// button was used.
    /// </summary>
    protected async Task<bool> ShowToolDialogAsync(
        string title,
        FrameworkElement content,
        string primaryText,
        string closeText,
        WidgetDialogViewport viewport)
    {
        RectInt32 ownerBounds = GetToolDialogOwnerBounds();
        RectInt32 workArea = ResolveWorkArea(ownerBounds);
        double scale = GetToolDialogScale();
        RectInt32 bounds = WidgetDialogLayout.ResolveWindowBounds(
            workArea,
            ownerBounds,
            WidgetPositioningService.ToPhysicalPixels(viewport.WindowWidth, scale),
            WidgetPositioningService.ToPhysicalPixels(viewport.WindowHeight, scale));

        try
        {
            var host = new WidgetToolDialogWindow(
                title,
                content,
                primaryText,
                closeText,
                viewport);
            return await host.ShowAtAsync(bounds);
        }
        catch (Exception ex)
        {
            App.Log($"[WidgetToolDialog] Host failed: {ex.Message}");
            return false;
        }
    }

    private RectInt32 GetToolDialogOwnerBounds()
    {
        return Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT rect)
            ? new RectInt32(
                rect.Left,
                rect.Top,
                Math.Max(1, rect.Right - rect.Left),
                Math.Max(1, rect.Bottom - rect.Top))
            : new RectInt32(0, 0, 1, 1);
    }

    private double GetToolDialogScale()
    {
        double scale = Win32Helper.GetDpiScaleForWindow(HWnd, RootElement.XamlRoot);
        return double.IsFinite(scale) && scale > 0 ? scale : 1;
    }
}
