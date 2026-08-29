using DeskBox.Helpers;
using DeskBox.Views;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    /// <summary>
    /// Restores resting widgets that the shell hid behind our back during a
    /// Show Desktop / Win+D minimize storm.
    ///
    /// Resting widgets are normally protected from Show Desktop because they
    /// are owner-attached to Explorer's desktop icon layer. That protection
    /// depends on a successful attach; widgets whose attach failed (transient
    /// desktop-icon-view lookup, Explorer transition) or whose last resting
    /// placement detached them sit in the normal top-level band, and the shell
    /// minimizes or DWM-cloaks them together with the application windows.
    /// Nothing re-shows such a widget until an unrelated foreground change
    /// happens to trigger new z-order work — the reported "some widgets do not
    /// come back after Show Desktop until a window is opened" symptom.
    ///
    /// The check is strictly conservative: it only touches windows that DeskBox
    /// itself considers visible, are not intentionally cloaked for a tray hide,
    /// and are found iconic or shell-cloaked.
    /// </summary>
    internal void VerifyRestingWidgetsAfterShellMinimize(string reason)
    {
        if (_widgetsRaisedFromTray || _isTogglingWidgetsDesktopLayer)
        {
            return;
        }

        if (IsWidgetInteractionActive ||
            !WidgetLayerService.ShouldKeepWidgetsVisibleOnShowDesktop())
        {
            return;
        }

        int restored = 0;
        int uncloaked = 0;
        foreach (IDesktopWidgetWindow window in GetLoadedDesktopWindows())
        {
            if (!window.Visible)
            {
                continue;
            }

            IntPtr hwnd = window.WindowHandle;
            if (hwnd == IntPtr.Zero || !Win32Helper.IsWindow(hwnd))
            {
                continue;
            }

            if (window is WidgetWindowBase widgetWindow && widgetWindow.IsTrayCloakActive)
            {
                // The cloak was applied by our own tray-hide animation; it is
                // not a Show Desktop casualty.
                continue;
            }

            if (Win32Helper.IsIconic(hwnd))
            {
                Win32Helper.ShowWindow(hwnd, Win32Helper.SW_SHOWNOACTIVATE);
                // Re-establish the desktop-layer attach so the next Show
                // Desktop does not hit the same unprotected resting spot.
                WidgetLayerService.MoveToDesktopBottom(hwnd);
                restored++;
                App.Log(
                    $"[ShowDesktop] Restored iconic resting widget " +
                    $"hwnd=0x{hwnd.ToInt64():X} kind={window.Config.WidgetKind} reason={reason}");
                continue;
            }

            if (Win32Helper.TryGetDwmCloakState(hwnd) == 1)
            {
                int visible = 0;
                Win32Helper.TrySetDwmWindowAttribute(hwnd, Win32Helper.DWMWA_CLOAK, ref visible);
                uncloaked++;
                App.Log(
                    $"[ShowDesktop] Uncloaked shell-cloaked resting widget " +
                    $"hwnd=0x{hwnd.ToInt64():X} kind={window.Config.WidgetKind} reason={reason}");
            }
        }

        if (restored > 0 || uncloaked > 0)
        {
            App.Log(
                $"[ShowDesktop] Self-heal completed reason={reason} " +
                $"restored={restored} uncloaked={uncloaked}");
        }
        else
        {
            App.LogVerbose($"[ShowDesktop] Self-heal verified, nothing to restore reason={reason}");
        }
    }
}
