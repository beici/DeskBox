using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Views;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    /// <summary>
    /// Tracks whether the last Show Desktop storm forced resting widgets into
    /// the normal band's top. Cleared once the desktop hands the foreground
    /// back, which is when the widgets return to the resting band.
    /// </summary>
    private bool _showDesktopRaisedWidgets;

    /// <summary>
    /// True once the steady "desktop is raised and every widget is already in
    /// place" conclusion has been logged for the current Show Desktop episode.
    /// The verification runs on a 1.5 s watchdog, so logging that conclusion on
    /// every tick turned a state into an event: leaving the desktop in front for
    /// a few minutes pushed everything else out of the 5 MB log rotation window.
    /// Reset whenever a pass actually restores/raises/uncloaks a widget or the
    /// desktop stops being raised, so each episode still records one line.
    /// </summary>
    private bool _showDesktopSteadyStateLogged;

    /// <summary>
    /// Last observed value of "the shell desktop layer is raised", used to turn
    /// the 1.5 s watchdog's steady state into an edge. Every raise and release
    /// of the desktop root moves the windows that are owner-attached to it, and
    /// a window inside a move/size transaction has its acrylic backdrop sample
    /// frozen by DWM — the capsules come back from Show Desktop dimmed and stay
    /// dimmed until something forces a frame (a hover). The bounds-transition
    /// nudge already exists for exactly this failure mode; this edge is where it
    /// has to fire for the Show Desktop family.
    /// </summary>
    private bool _showDesktopLayerObservedRaised;

    /// <summary>
    /// FWIN-01: DWMWA_CLOAK is a bit mask rather than a boolean — 0 visible,
    /// 1 cloaked by its owner app, 2 cloaked by the shell, 4 cloak inherited
    /// from the owner. "Cloaked in any way" is therefore any value above
    /// zero; -1 is the dwmapi-unavailable failure sentinel and must never be
    /// treated as cloaked (it would also fail the &gt; 0 test).
    /// </summary>
    internal static bool IsDwmCloakStateCloaked(int cloakState)
    {
        return cloakState > 0;
    }

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
            App.LogVerbose(
                $"[ShowDesktop] Self-heal skipped reason={reason} " +
                $"raised={_widgetsRaisedFromTray} toggling={_isTogglingWidgetsDesktopLayer}");
            return;
        }

        // Show Desktop raises the desktop layer WITHOUT taking the foreground,
        // so the foreground comparison alone is a hint at best; the raised
        // layer check is what actually observes it.
        bool shellDesktopForeground = WidgetLayerService.IsShellDesktopForeground();
        bool desktopLayerRaised = WidgetLayerService.IsDesktopLayerRaised();
        bool desktopRaised = shellDesktopForeground || desktopLayerRaised;
        bool desktopLayerToggled = desktopRaised != _showDesktopLayerObservedRaised;
        _showDesktopLayerObservedRaised = desktopRaised;
        if (!desktopRaised && !_showDesktopRaisedWidgets)
        {
            // Nothing to heal and nothing to hand back. The watchdog runs this
            // on a fixed cadence, so the desktop-idle case must stay free.
            App.LogVerbose(
                $"[ShowDesktop] Self-heal idle reason={reason} " +
                $"foreground={shellDesktopForeground} layerRaised={desktopLayerRaised}");
            if (desktopLayerToggled)
            {
                // The desktop layer just dropped back below the widgets. The
                // release moves the owner-attached windows even though this pass
                // has nothing to restore, so the acrylic sample needs the same
                // rebuild a bounds transition gets.
                NudgeBackdropAfterShowDesktopLayerChange(reason, desktopRaised);
            }

            return;
        }

        if (IsWidgetInteractionActive ||
            !WidgetLayerService.ShouldKeepWidgetsVisibleOnShowDesktop())
        {
            // While the shell desktop owns the foreground the user is looking
            // at Show Desktop right now, so a skipped heal is exactly the
            // difference between "the widgets are there" and "the widgets are
            // gone". Log those skips at the visible level; the ordinary
            // background storms stay verbose.
            if (desktopRaised)
            {
                App.Log(
                    $"[ShowDesktop] Self-heal skipped reason={reason} " +
                    $"interactionActive={IsWidgetInteractionActive} " +
                    $"keepVisible={WidgetLayerService.ShouldKeepWidgetsVisibleOnShowDesktop()}");
            }
            else
            {
                App.LogVerbose(
                    $"[ShowDesktop] Self-heal skipped reason={reason} " +
                    $"interactionActive={IsWidgetInteractionActive} " +
                    $"keepVisible={WidgetLayerService.ShouldKeepWidgetsVisibleOnShowDesktop()}");
            }

            // A skipped heal still observed the desktop layer change, and that
            // change is what froze the backdrop sample — repairing it does not
            // depend on whether this pass was allowed to move windows.
            if (desktopLayerToggled)
            {
                NudgeBackdropAfterShowDesktopLayerChange(reason, desktopRaised);
            }

            return;
        }

        int restored = 0;
        int uncloaked = 0;
        int raised = 0;
        int returned = 0;
        List<(IntPtr Hwnd, WidgetKind Kind)>? pendingRaises = null;
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

            // FWIN-01: DWMWA_CLOAK is a bit mask, not a boolean — see
            // IsDwmCloakStateCloaked. -1 is the dwmapi failure sentinel and
            // must not uncloak.
            int cloakState = Win32Helper.TryGetDwmCloakState(hwnd);
            if (IsDwmCloakStateCloaked(cloakState))
            {
                int visible = 0;
                Win32Helper.TrySetDwmWindowAttribute(hwnd, Win32Helper.DWMWA_CLOAK, ref visible);
                uncloaked++;
                App.Log(
                    $"[ShowDesktop] Uncloaked shell-cloaked resting widget " +
                    $"hwnd=0x{hwnd.ToInt64():X} kind={window.Config.WidgetKind} reason={reason}");
                continue;
            }

            // Show Desktop does not have to minimize or cloak a widget to hide
            // it: the shell raises the desktop above the whole normal band, so
            // a widget that is neither iconic nor cloaked simply ends up
            // underneath it. Neither check above can see that state, which is
            // why the symptom was "nothing comes back until I open a window".
            // The detector compares the widget's own Z-order position against
            // the desktop root rather than reading the foreground, because a
            // raise does not move the foreground. It is also the only condition
            // the raise needs: "the desktop is above this widget" already means
            // the widget is hidden, so an additional "is the desktop raised"
            // gate would just re-introduce the foreground dependency that let
            // the widget stay hidden. The widgets are handed back to the resting
            // band below once the desktop lets go.
            if (WidgetLayerService.IsDesktopLayerAboveWindow(hwnd))
            {
                if (WidgetLayerService.RaiseAboveDesktopLayer(hwnd))
                {
                    raised++;
                    App.Log(
                        $"[ShowDesktop] Raised desktop-covered resting widget " +
                        $"hwnd=0x{hwnd.ToInt64():X} kind={window.Config.WidgetKind} reason={reason}");
                }
                else
                {
                    // The raise is a Z-order transaction against a moving band:
                    // while the desktop root is still on top, the first widget of
                    // the batch can read as covered again right after its own
                    // round-trip, and every later raise then pushes the root one
                    // slot further down. Verifying the whole batch first keeps a
                    // batch-ordering artefact out of the log — measured on the
                    // reporting machine: the first of twelve widgets reported a
                    // failure at 01:35:28.464 and every one of them was above the
                    // desktop root five seconds later.
                    (pendingRaises ??= []).Add((hwnd, window.Config.WidgetKind));
                }
            }
        }

        if (pendingRaises is not null)
        {
            foreach ((IntPtr hwnd, WidgetKind kind) in pendingRaises)
            {
                if (Win32Helper.IsWindow(hwnd) &&
                    WidgetLayerService.RaiseAboveDesktopLayer(hwnd))
                {
                    raised++;
                    App.Log(
                        $"[ShowDesktop] Raised desktop-covered resting widget " +
                        $"hwnd=0x{hwnd.ToInt64():X} kind={kind} reason={reason} retry=true");
                }
                else
                {
                    App.Log(
                        $"[ShowDesktop] Failed to raise desktop-covered resting widget " +
                        $"hwnd=0x{hwnd.ToInt64():X} kind={kind} reason={reason}");
                }
            }
        }

        if (raised > 0)
        {
            _showDesktopRaisedWidgets = true;
        }
        else if (!desktopRaised && _showDesktopRaisedWidgets)
        {
            // Show Desktop ended: send the widgets back to the resting band so
            // ordinary application windows cover them again.
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

                WidgetLayerService.MoveToDesktopBottom(hwnd);
                returned++;
            }

            _showDesktopRaisedWidgets = false;
            App.Log($"[ShowDesktop] Returned widgets to the resting band reason={reason} count={returned}");
        }

        // Every branch above that touched a widget issued SetWindowPos, and the
        // shell's own raise/release moves the owner-attached windows even when
        // this pass had nothing to do. A window inside that transaction has its
        // acrylic backdrop sample frozen by DWM, which is the "capsules come
        // back from Show Desktop dimmed and stay dimmed until hover" symptom —
        // the same failure mode the bounds-transition settle point already
        // repairs. Do it here, once per episode edge or per pass that moved a
        // window, instead of on every 1.5 s watchdog tick.
        if (restored > 0 || uncloaked > 0 || raised > 0 || returned > 0 || desktopLayerToggled)
        {
            NudgeBackdropAfterShowDesktopLayerChange(reason, desktopRaised);
        }

        if (restored > 0 || uncloaked > 0 || raised > 0)
        {
            _showDesktopSteadyStateLogged = false;
            App.Log(
                $"[ShowDesktop] Self-heal completed reason={reason} " +
                $"restored={restored} uncloaked={uncloaked} raised={raised} " +
                $"desktopRaised={desktopRaised}");
        }
        else if (desktopRaised)
        {
            // One line per Show Desktop episode, not one per 1.5 s tick.
            if (!_showDesktopSteadyStateLogged)
            {
                _showDesktopSteadyStateLogged = true;
                App.Log(
                    $"[ShowDesktop] Desktop raised, widgets already in place reason={reason}");
            }
        }
        else
        {
            _showDesktopSteadyStateLogged = false;
            App.LogVerbose($"[ShowDesktop] Self-heal verified, nothing to restore reason={reason}");
        }
    }

    /// <summary>
    /// Rebuilds the DWM composition of every loaded widget window after a Show
    /// Desktop layer change.
    ///
    /// The capsules come back from Show Desktop with a darker acrylic backdrop
    /// and stay that way until a hover forces a frame. That is the same frozen
    /// sample the bounds-transition settle point already repairs: DWM freezes
    /// the backdrop sample of a window that is inside a move/size transaction,
    /// and the shell's desktop raise and release move the windows that are
    /// owner-attached to the desktop root exactly that way. The widget windows
    /// are not resized here, so nothing else would ever invalidate them.
    ///
    /// The call is deliberately unconditional per transition and per pass that
    /// moved a window: the nudge is a single no-z-order SetWindowPos per window,
    /// and the alternative is leaving the sample frozen until the user's first
    /// hover.
    /// </summary>
    private void NudgeBackdropAfterShowDesktopLayerChange(string reason, bool desktopRaised)
    {
        App.Log(
            $"[ShowDesktop] Backdrop nudge reason={reason} desktopRaised={desktopRaised}");
        RefreshBackdropsAfterDesktopLayerChange();

        // The shell animates the desktop raise and the desktop release for well
        // over a second, and every step of that animation moves the windows that
        // are owner-attached to the desktop root again, which freezes the
        // backdrop sample again. A single nudge at the edge would therefore be
        // re-frozen before the animation ends, so the sample is invalidated once
        // more after the animation has settled.
        _ = Task.Run(async () =>
        {
            foreach (int delayMs in ShowDesktopBackdropNudgeFollowUpDelaysMs)
            {
                await Task.Delay(delayMs).ConfigureAwait(false);
                RefreshBackdropsAfterDesktopLayerChange();
            }
        });
    }

    /// <summary>
    /// Follow-up delays, measured from the desktop-layer edge, after which the
    /// frozen backdrop sample is invalidated again. They sit past the shell's
    /// own raise and release animations so the last word belongs to a settled
    /// window rather than to an animation frame.
    /// </summary>
    private static readonly int[] ShowDesktopBackdropNudgeFollowUpDelaysMs = [350, 1100];
}
