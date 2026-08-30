using DeskBox.Controls.WidgetContents;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Views;
using Microsoft.UI.Windowing;
using System.Collections.Generic;
using System.Linq;
using Windows.Graphics;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    /// <summary>
    /// Re-applies the clipboard record colors on every loaded Quick Capture
    /// surface. Used by the Settings entry so live widgets update the moment
    /// a color is committed from the settings window.
    /// </summary>
    internal void ApplyQuickCaptureClipboardColorsToLoadedWidgets()
    {
        foreach (ContentWidgetWindow window in _contentWidgets.Values)
        {
            if (window.Config.WidgetKind is WidgetKind.QuickCapture &&
                window.CurrentContent is QuickCaptureSurfaceContent quickCaptureSurface)
            {
                quickCaptureSurface.ApplyClipboardItemColors();
            }
        }
    }

    /// <summary>
    /// Physical rects of every other visible widget window, for the margin
    /// dialog's "nearest widget" reference geometry. The moving window
    /// itself is excluded.
    /// </summary>
    internal IReadOnlyList<Windows.Graphics.RectInt32> GetOtherVisibleWidgetRects(IntPtr excludeHwnd)
    {
        var rects = new List<Windows.Graphics.RectInt32>();
        foreach (IDesktopWidgetWindow window in GetLoadedDesktopWindows())
        {
            IntPtr hwnd = window.WindowHandle;
            if (hwnd == excludeHwnd ||
                hwnd == IntPtr.Zero ||
                !window.Visible ||
                !Win32Helper.IsWindow(hwnd) ||
                !Win32Helper.GetWindowRect(hwnd, out Win32Helper.RECT rect))
            {
                continue;
            }

            rects.Add(new Windows.Graphics.RectInt32(
                rect.Left,
                rect.Top,
                Math.Max(1, rect.Right - rect.Left),
                Math.Max(1, rect.Bottom - rect.Top)));
        }

        return rects;
    }

    /// <summary>
    /// Re-runs the appearance preview (which includes the title appearance
    /// pass) on every visible widget window. Used by batch title-alignment
    /// and icon-reset operations.
    /// </summary>
    internal void ApplyTitleAppearanceToVisibleWidgets()
    {
        foreach (IDesktopWidgetWindow window in GetLoadedDesktopWindows())
        {
            if (window.Visible)
            {
                window.ApplyAppearancePreview();
            }
        }
    }

    /// <summary>
    /// Moves every visible widget window whose bounds the transform accepts.
    /// Used by the margin entry feature for its "apply to all widgets" mode.
    ///
    /// The window is moved with a physical SetWindowPos and the result is
    /// persisted through the same positioning service the interactive
    /// drag/resize path uses, so anchor/monitor bookkeeping stays consistent.
    /// Windows whose transform returns null are skipped, so callers can
    /// express "only when this edge is the nearest" purely as data.
    /// </summary>
    /// <returns>The number of windows that were moved and persisted.</returns>
    internal int MoveVisibleWidgets(TransformWidgetBounds transform)
    {
        int applied = 0;
        bool anyChanged = false;

        // Physical rects of all eligible moving windows. Every transform
        // receives the full snapshot minus its own rect, so the margin
        // geometry can resolve its "nearest widget" reference per window.
        var snapshot = new List<(IntPtr Hwnd, RectInt32 Bounds)>();
        foreach (IDesktopWidgetWindow window in GetLoadedDesktopWindows())
        {
            if (!window.Visible)
            {
                continue;
            }

            IntPtr hwnd = window.WindowHandle;
            if (hwnd == IntPtr.Zero ||
                !Win32Helper.IsWindow(hwnd) ||
                !Win32Helper.GetWindowRect(hwnd, out Win32Helper.RECT rect))
            {
                continue;
            }

            snapshot.Add((hwnd, new RectInt32(
                rect.Left,
                rect.Top,
                Math.Max(1, rect.Right - rect.Left),
                Math.Max(1, rect.Bottom - rect.Top))));
        }

        foreach (IDesktopWidgetWindow window in GetLoadedDesktopWindows())
        {
            if (!window.Visible)
            {
                continue;
            }

            // Position-locked widgets are moved by no path other than the
            // user dragging them unlocked (CoordinatedMove enforces the same
            // lock), and collapsed capsules must never write their transient
            // geometry into the resting config — both hosts reject compact
            // bounds in UpdateConfigBoundsFromPhysical for that reason.
            if (window.Config.IsPositionLocked ||
                window.IsCompactArrangementActive ||
                window.IsCompactCollapsedState)
            {
                continue;
            }

            (IntPtr hwnd, RectInt32 current) = snapshot
                .FirstOrDefault(entry => entry.Hwnd == window.WindowHandle);
            if (hwnd == IntPtr.Zero)
            {
                continue;
            }

            var others = snapshot
                .Where(entry => entry.Hwnd != window.WindowHandle)
                .Select(entry => entry.Bounds)
                .ToArray();
            if (transform(window.Config, current, others) is not RectInt32 target ||
                target.X == current.X && target.Y == current.Y)
            {
                continue;
            }

            if (!Win32Helper.SetWindowPos(
                    hwnd,
                    IntPtr.Zero,
                    target.X,
                    target.Y,
                    target.Width,
                    target.Height,
                    Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE))
            {
                continue;
            }

            var center = new Windows.Graphics.PointInt32(
                target.X + Math.Max(1, target.Width) / 2,
                target.Y + Math.Max(1, target.Height) / 2);
            RectInt32 workArea = DisplayArea.GetFromPoint(center, DisplayAreaFallback.Nearest).WorkArea;
            WidgetPositioningService.UpdateConfigFromPhysicalBounds(window.Config, target, workArea);
            _settingsService.UpdateWidget(window.Config, notifySubscribers: false);
            applied++;
            anyChanged = true;
        }

        if (anyChanged)
        {
            _settingsService.SaveDebounced(notifySubscribers: false);
        }

        return applied;
    }
}

/// <summary>
/// Returns the new physical bounds for the widget, or null to leave it in
/// place. Implementations must be pure; the manager applies persistence.
/// </summary>
public delegate RectInt32? TransformWidgetBounds(WidgetConfig config, RectInt32 currentBounds, IReadOnlyList<RectInt32> otherBounds);
