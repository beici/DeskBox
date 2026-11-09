// Copyright (c) DeskBox. All rights reserved.

using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;
using Windows.Graphics;
using WinRT;
using WinRT.Interop;

namespace DeskBox.Views;

public abstract partial class WidgetWindowBase
{
    protected void ElevateForInteraction()
    {
        if (App.Current.WidgetManager is { WidgetsRaisedFromTray: true })
        {
            return;
        }

        // While an expanded capsule lease is held, the expand flow already
        // owns this window's layer (band top below app windows, or band
        // bottom in pinned mode until collapse). The TOPMOST interaction
        // pulse would make it jump above application windows — and in
        // pinned mode would bury it — for the pulse duration.
        if (_expandedWidgetLayerLeaseGeneration != 0)
        {
            LastElevateForInteractionUtc = DateTime.UtcNow;
            OnElevated();
            return;
        }

        LastElevateForInteractionUtc = DateTime.UtcNow;
        HoldTemporaryTopMost();
        OnElevated();
    }

    protected void HoldTemporaryTopMost(bool showWindow = true)
    {
        if (WidgetLayerService.UsesDesktopPinnedMode())
        {
            IsAtDesktopLayer = true;
            IsRaisedFromManager = false;
            KeepRaisedUntilDeactivate = false;
            RestoreDesktopLayerWhenIdle = false;
            WidgetLayerService.MoveToDesktopBottom(HWnd, showWindow);
            App.LogVerbose($"[ZOrder] {LogPrefix} HoldTemporaryTopMost skipped pinned hwnd=0x{HWnd.ToInt64():X}");
            return;
        }

        IsAtDesktopLayer = false;
        KeepRaisedUntilDeactivate = true;
        RestoreDesktopLayerWhenIdle = false;
        WidgetLayerService.HoldTemporaryTopMost(HWnd, showWindow);
        App.LogVerbose($"[ZOrder] {LogPrefix} HoldTemporaryTopMost hwnd=0x{HWnd.ToInt64():X}");
        StartTopMostSafetyTimer();
    }

    public void RaiseTemporarilyFromManager()
    {
        if (!Visible || IsClosing)
        {
            return;
        }

        LastElevateForInteractionUtc = DateTime.UtcNow;
        HoldTemporaryTopMost();
        IsRaisedFromManager = !IsAtDesktopLayer;
    }

    internal void AdoptManagerRaisedStateAfterPreparedShow()
    {
        if (!Visible || IsClosing)
        {
            return;
        }

        // ShowPreparedRaisedFromTray already settled the native Z-order while
        // the HWND was cloaked. Preserve the manager-owned raised lifetime
        // without issuing a second visible Z-order transaction.
        LastElevateForInteractionUtc = DateTime.UtcNow;
        IsRaisedFromManager = !IsAtDesktopLayer;
    }

    protected void StartTopMostSafetyTimer()
    {
        // Temporary raises intentionally pulse TOPMOST and immediately return
        // to the normal band. Logical desktop-layer state, not WS_EX_TOPMOST,
        // determines whether a safety restoration is still required.
        if (!WidgetTemporaryRaiseLeasePolicy.CanRestoreDesktopLayer(
                Visible,
                IsHideAnimationRunning,
                IsClosing) ||
            !WidgetTemporaryRaiseLeasePolicy.ShouldArmSafetyRestore(
                IsAtDesktopLayer))
        {
            TopMostSafetyTimer?.Stop();
            return;
        }

        if (TopMostSafetyTimer is null)
        {
            TopMostSafetyTimer = DispatcherQueue.CreateTimer();
            TopMostSafetyTimer.IsRepeating = false;
            TopMostSafetyTimer.Interval = TimeSpan.FromSeconds(2);
            TopMostSafetyTimer.Tick += TopMostSafetyTimer_Tick;
            PerformanceLogger.RecordTransientUiTimerCreated();
        }
        else
        {
            TopMostSafetyTimer.Stop();
        }
        _safetyRestoreLeaseDeferrals = 0;
        TopMostSafetyTimer.Start();
    }

    private static int _safetyForceRestoreLoggedThisSession;

    // Lease deferrals are bounded (unlike the four routine defer predicates,
    // which re-arm indefinitely by design): if the lease's owner gave up, the
    // net must still reach the window within a few intervals.
    private const int MaxSafetyRestoreLeaseDeferrals = 3;
    private int _safetyRestoreLeaseDeferrals;

    private void TopMostSafetyTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        sender.Stop();
        if (!WidgetTemporaryRaiseLeasePolicy.CanRestoreDesktopLayer(
                Visible,
                IsHideAnimationRunning,
                IsClosing))
        {
            CancelPendingDesktopLayerRestore();
            return;
        }

        if (!IsAtDesktopLayer &&
            App.Current.WidgetManager is not { WidgetsRaisedFromTray: true })
        {
            // The manager's temporary-raise lease owns this window's descent
            // (settle ~2.3s plus busy retries) and cancels this timer when it
            // lands; deferring while it is held stops the net from stealing
            // the restore 300ms early on every startup raise.
            bool routineDefer = WidgetTemporaryRaiseLeasePolicy.ShouldDeferSafetyRestore(
                IsDragging,
                IsResizing,
                HasBlockingFlyoutOpen(),
                App.Current.WidgetManager is { IsWidgetInteractionActive: true });
            bool leaseDefer = IsRaisedFromManager &&
                _safetyRestoreLeaseDeferrals < MaxSafetyRestoreLeaseDeferrals;
            if (routineDefer || leaseDefer)
            {
                if (leaseDefer && !routineDefer)
                {
                    _safetyRestoreLeaseDeferrals++;
                }

                App.LogVerbose(
                    $"[ZOrder] {LogPrefix} safety timer: defer restore hwnd=0x{HWnd.ToInt64():X} " +
                    $"routine={routineDefer} lease={leaseDefer}");
                sender.Start();
                return;
            }

            // The restore itself is routine — the normal descent path
            // missed and this net is the designed backstop. Only the
            // first hit per session stays at the default level so a
            // regression still surfaces in field logs.
            string forceRestoreLine =
                $"[ZOrder] {LogPrefix} safety timer: force restore hwnd=0x{HWnd.ToInt64():X}";
            if (System.Threading.Interlocked.Exchange(ref _safetyForceRestoreLoggedThisSession, 1) == 0)
            {
                App.Log(forceRestoreLine);
            }
            else
            {
                App.LogVerbose(forceRestoreLine);
            }

            RestoreDesktopLayer(force: true);
        }
    }

    protected void ReleaseTopMostSafetyTimer()
    {
        DispatcherQueueTimer? timer = TopMostSafetyTimer;
        if (timer is null)
        {
            return;
        }

        TopMostSafetyTimer = null;
        timer.Stop();
        timer.Tick -= TopMostSafetyTimer_Tick;
        PerformanceLogger.RecordTransientUiTimerReleased();
    }

    protected bool ShouldDeferDesktopLayerRestore()
    {
        if (IsDragging ||
            IsResizing ||
            HasBlockingFlyoutOpen() ||
            App.Current.WidgetManager is { IsWidgetInteractionActive: true })
        {
            return true;
        }

        IntPtr foregroundWindow = Win32Helper.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        return foregroundWindow == HWnd ||
               Win32Helper.GetAncestor(foregroundWindow, Win32Helper.GA_ROOTOWNER) == HWnd;
    }

    protected void RestoreDesktopLayer(bool force = false, bool absoluteDesktopBottom = false)
    {
        if (!WidgetTemporaryRaiseLeasePolicy.CanRestoreDesktopLayer(
                Visible,
                IsHideAnimationRunning,
                IsClosing))
        {
            CancelPendingDesktopLayerRestore();
            return;
        }

        if (!force && !RestoreDesktopLayerWhenIdle && KeepRaisedUntilDeactivate)
        {
            return;
        }

        if (!force && (IsDragging || IsResizing || HasBlockingFlyoutOpen()))
        {
            if (force || RestoreDesktopLayerWhenIdle)
            {
                RestoreDesktopLayerWhenIdle = true;
            }

            return;
        }

        CancelPendingDesktopLayerRestore();
        ClearTopMostOnly(absoluteDesktopBottom);
        ApplyBackdropPreference();
    }

    protected void CancelPendingDesktopLayerRestore()
    {
        TopMostSafetyTimer?.Stop();
        IsRaisedFromManager = false;
        KeepRaisedUntilDeactivate = false;
        RestoreDesktopLayerWhenIdle = false;
    }

    protected void ClearTopMostOnly(bool absoluteDesktopBottom = false)
    {
        IsRaisedFromManager = false;
        if (absoluteDesktopBottom)
        {
            // Startup settle: ignore the foreground owner and rest directly on
            // the desktop layer so widgets cannot stay floating above apps
            // when a login-time window still holds the foreground.
            IsAtDesktopLayer = true;
            WidgetLayerService.MoveToDesktopBottom(HWnd);
            App.LogVerbose($"[ZOrder] {LogPrefix} ClearTopMostOnly absolute hwnd=0x{HWnd.ToInt64():X}");
        }
        else
        {
            IntPtr foreground = WidgetLayerService.ClearTopMostPreservingForeground(
                HWnd,
                out RelativeLayerRestoreDisposition disposition);
            // PreservePeerOrder deliberately leaves the window at its current
            // level: raised before the restore, still raised after it. Marking it
            // bedded here would blind the Deactivated funnel that owns its later
            // descent (the flyout-Esc lingering window). The placed branches
            // (DesktopBottom / BehindForeground) end on a resting window.
            if (disposition != RelativeLayerRestoreDisposition.PreservePeerOrder)
            {
                IsAtDesktopLayer = true;
            }

            App.LogVerbose($"[ZOrder] {LogPrefix} ClearTopMostOnly hwnd=0x{HWnd.ToInt64():X} fg=0x{foreground.ToInt64():X} disposition={disposition}");
        }

        App.Current.WidgetManager?.QueueIdleWidgetZOrderNormalization(
            $"{LogPrefix}-desktop-layer-restored");
    }

    // ── Drag logic ─────────────────────────────────────────────

    protected void BeginWindowDragCore(PointerRoutedEventArgs e, FrameworkElement captureElement)
    {
        BeginWindowDragCore(e, captureElement, activatesTitleGroup: false);
    }

    /// <summary>
    /// Arms a window drag without touching the desktop layer. Pressing a title
    /// bar used to raise the widget, downgrade its backdrop and open a snap
    /// session immediately; every one of those is a visible transaction, and a
    /// click that never moves paid for all of them twice (once on press, once
    /// when the release restored the resting layer). Neighbouring widgets whose
    /// drop shadows reach across the gap re-composited on both transitions,
    /// which is the edge flicker users reported on a plain title click. The
    /// work now happens in <see cref="EngageWindowDrag"/> once the pointer
    /// crosses the same 4px threshold that already gated the first move.
    /// </summary>
    protected void BeginWindowDragCore(
        PointerRoutedEventArgs e,
        FrameworkElement captureElement,
        bool activatesTitleGroup)
    {
        CancelPendingTitleBarDragFrame();
        _titleBarDragFrameMetrics = new BoundsInteractionFrameMetrics();
        BeginWidgetBoundsInteraction();
        IsDragging = true;
        _deferTitleBarDragConfigUpdates = true;
        HasMovedTitleBarDrag = false;
        _isWindowDragEngaged = false;
        _isCoordinatedMoveDrag = false;
        _windowDragActivatesTitleGroup = activatesTitleGroup;
        _windowDragRequestsCoordinatedMove =
            Win32Helper.IsKeyPressed(Windows.System.VirtualKey.Control);
        DisplayChangeWatcher?.SuppressRestore();
        if (!Win32Helper.TryGetPointerScreenPoint(HWnd, RootElement, e, out InitialPointerPt))
        {
            Win32Helper.GetCursorPos(out InitialPointerPt);
        }
        RectInt32 initialBounds = GetActualWindowBounds();
        InitialWindowPos = new PointInt32(initialBounds.X, initialBounds.Y);
        InitialWindowSize = new SizeInt32(initialBounds.Width, initialBounds.Height);
        DragCaptureElement = captureElement;
        captureElement.CapturePointer(e.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// Performs the once-per-drag setup that a real move needs. Called from the
    /// movement-threshold crossing, before the first bounds frame is applied.
    /// </summary>
    private void EngageWindowDrag()
    {
        if (_isWindowDragEngaged)
        {
            return;
        }

        _isWindowDragEngaged = true;
        SimplifyBackdropForInteraction();
        _isCoordinatedMoveDrag =
            _windowDragRequestsCoordinatedMove &&
            App.Current?.WidgetManager?.TryBeginCoordinatedMove(HWnd) == true;
        if (!_isCoordinatedMoveDrag)
        {
            if (_windowDragActivatesTitleGroup &&
                !_windowDragRequestsCoordinatedMove)
            {
                App.Current?.WidgetManager?.ActivateAllVisibleWidgetsFromTitle(HWnd);
            }

            ElevateForInteraction();
        }

        bool movesCapsuleBar = !_isCoordinatedMoveDrag && BeginCompactArrangementDrag();
        if (!movesCapsuleBar && !_isCoordinatedMoveDrag)
        {
            App.Current?.ResizeGuideOverlay.BeginDrag(HWnd, RootElement);
        }
    }

    protected void ContinueWindowDragCore(PointerRoutedEventArgs e)
    {
        if (!IsDragging)
        {
            return;
        }

        if (!Win32Helper.TryGetPointerScreenPoint(HWnd, RootElement, e, out var currentPt) &&
            !Win32Helper.GetCursorPos(out currentPt))
        {
            e.Handled = true;
            return;
        }
        int deltaX = currentPt.X - InitialPointerPt.X;
        int deltaY = currentPt.Y - InitialPointerPt.Y;
        int dragDistanceSquared = (deltaX * deltaX) + (deltaY * deltaY);

        if (!HasMovedTitleBarDrag)
        {
            if (dragDistanceSquared < 16)
            {
                e.Handled = true;
                return;
            }

            HasMovedTitleBarDrag = true;
            EngageWindowDrag();
            WidgetShellControl.NotifyCompactDragMoved();
        }

        QueueTitleBarDragFrame(deltaX, deltaY);
        e.Handled = true;
    }

    private void QueueTitleBarDragFrame(int deltaX, int deltaY)
    {
        _titleBarDragFrameMetrics?.RecordPointerSample();
        _pendingTitleBarDragFrame = new PendingTitleBarDragFrame(
            new RectInt32(
                InitialWindowPos.X + deltaX,
                InitialWindowPos.Y + deltaY,
                InitialWindowSize.Width,
                InitialWindowSize.Height),
            deltaX,
            deltaY);
        _titleBarDragFrameRegistration ??=
            WidgetCompactAnimationCoordinator.Register(ApplyPendingTitleBarDragFrame, HWnd, paceToDisplay: true);
    }

    private void ApplyPendingTitleBarDragFrame()
    {
        if (!IsDragging || _pendingTitleBarDragFrame is not { } frame)
        {
            return;
        }

        // PointerMoved may arrive much faster than DWM can present frames,
        // especially on Win10 with several Acrylic HWNDs. Consume only the
        // latest pointer sample for this frame so snap/group work and native
        // window commits never scale with raw mouse-report frequency.
        _pendingTitleBarDragFrame = null;
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        try
        {
            ApplyTitleBarDragFrame(frame);
        }
        finally
        {
            _titleBarDragFrameMetrics?.RecordUpdate(
                System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                WidgetCompactAnimationCoordinator.GetFrameBudgetMilliseconds(HWnd));
        }
    }

    private void ApplyTitleBarDragFrame(PendingTitleBarDragFrame frame)
    {
        if (_isCoordinatedMoveDrag)
        {
            App.Current?.WidgetManager?.UpdateCoordinatedMove(
                HWnd,
                frame.DeltaX,
                frame.DeltaY);
            return;
        }

        if (TryMoveCompactArrangement(frame.ProposedBounds, out _))
        {
            return;
        }

        RectInt32 snappedBounds =
            App.Current?.ResizeGuideOverlay.UpdateGuidesAndSnapForDrag(
                frame.ProposedBounds)
            ?? frame.ProposedBounds;
        ApplyWindowBounds(
            snappedBounds.X,
            snappedBounds.Y,
            snappedBounds.Width,
            snappedBounds.Height,
            persist: false,
            updateConfig: false);
        if (!IsCompactBoundsStateActive)
        {
            App.Current?.WidgetManager?.UpdateWidgetGroupDragPreview(Config.Id);
        }
    }

    private void FlushPendingTitleBarDragFrame()
    {
        ApplyPendingTitleBarDragFrame();
        CancelPendingTitleBarDragFrame();
        CompleteBoundsInteractionFrameMetrics(ref _titleBarDragFrameMetrics, "drag");
    }

    private void CancelPendingTitleBarDragFrame()
    {
        _pendingTitleBarDragFrame = null;
        _titleBarDragFrameRegistration?.Dispose();
        _titleBarDragFrameRegistration = null;
    }

    protected void EndWindowDragCore(PointerRoutedEventArgs e)
    {
        if (!IsDragging)
        {
            return;
        }

        FlushPendingTitleBarDragFrame();
        _deferTitleBarDragConfigUpdates = false;
        IsDragging = false;
        bool hasMoved = HasMovedTitleBarDrag;
        bool wasEngaged = _isWindowDragEngaged;
        _isWindowDragEngaged = false;
        _windowDragActivatesTitleGroup = false;
        _windowDragRequestsCoordinatedMove = false;
        DragCaptureElement?.ReleasePointerCapture(e.Pointer);
        DragCaptureElement = null;

        if (wasEngaged)
        {
            App.Current?.ResizeGuideOverlay.EndDrag();
        }

        if (_isCoordinatedMoveDrag &&
            App.Current?.WidgetManager?.CompleteCoordinatedMove(HWnd, hasMoved) == true)
        {
            _isCoordinatedMoveDrag = false;
            App.Current.WidgetManager.CancelWidgetGroupDrag(Config.Id);
            HasMovedTitleBarDrag = false;
            App.Current.WidgetManager.RestoreTemporarilyRaisedWidgetsToDesktopLayer(
                "coordinated-drag-ended");
            e.Handled = true;
            return;
        }

        _isCoordinatedMoveDrag = false;

        if (hasMoved)
        {
            CompleteCompactArrangementDrag();
            RectInt32 finalBounds = GetActualWindowBounds();
            finalBounds = CompleteExpandedWidgetDrag(finalBounds);
            CommitUserPlacementFromWindow(WidgetPlacementSource.Drag);
            if (_targetCollapsed)
            {
                // The pointer is still resting on the just-dropped capsule; an
                // immediate Smart hover-expand would look like the placement
                // snapped back on its own.
                _suppressHoverExpansionAfterDragUntilPointerExit = true;
            }
        }
        EndWidgetBoundsInteraction();
        OnDragEnd(hasMoved);
        if (hasMoved && !IsCompactBoundsStateActive)
        {
            _ = App.Current?.WidgetManager?.CompleteWidgetGroupDragAsync(Config.Id);
        }
        else if (wasEngaged)
        {
            App.Current?.WidgetManager?.CancelWidgetGroupDrag(Config.Id);
        }

        DisplayChangeWatcher?.ResumeRestore();
        HasMovedTitleBarDrag = false;
        RestoreBackdropAfterInteraction();
        if (wasEngaged)
        {
            QueueBackdropRefresh();
            App.Current?.WidgetManager?.RestoreTemporarilyRaisedWidgetsToDesktopLayer(
                "drag-ended");
        }

        e.Handled = true;
    }

    // ── Resize logic ───────────────────────────────────────────

    protected void ResizeBorder_PointerPressedCore(object sender, PointerRoutedEventArgs e)
    {
        if (IsSizeLocked || IsResizing || sender is not FrameworkElement element)
        {
            return;
        }

        string direction = WidgetCompactInteractionPolicy.ResolveResizeDirection(
            IsCompactBoundsStateActive,
            element.Tag as string);
        if (!CanResizeCurrentWidgetState(direction))
        {
            return;
        }

        var properties = e.GetCurrentPoint(element).Properties;
        if (!properties.IsLeftButtonPressed)
        {
            return;
        }

        BeginWidgetBoundsInteraction();
        IsResizing = true;
        // Per-commit config sync from AppWindow.Changed would fight the
        // frame-coordinator cadence; the final bounds are persisted once in
        // ResizeBorder_PointerReleasedCore instead.
        _deferInteractiveResizeConfigUpdates = true;
        ResizeDirection = direction;
        DisplayChangeWatcher?.SuppressRestore();
        OnResizeStart();
        if (!Win32Helper.TryGetPointerScreenPoint(HWnd, RootElement, e, out InitialPointerPt))
        {
            Win32Helper.GetCursorPos(out InitialPointerPt);
        }
        RectInt32 initialBounds = GetActualWindowBounds();
        InitialWindowPos = new PointInt32(initialBounds.X, initialBounds.Y);
        InitialWindowSize = new SizeInt32(initialBounds.Width, initialBounds.Height);
        _interactiveResizeMinimumSize = GetPhysicalMinimumWindowSize(
            initialBounds.X,
            initialBounds.Y,
            initialBounds.Width,
            initialBounds.Height);
        BeginInteractiveResizePerformanceSession();
        SimplifyBackdropForInteraction();
        element.CapturePointer(e.Pointer);
        App.Current.ResizeGuideOverlay.BeginResize(HWnd, RootElement);
        e.Handled = true;
    }

    protected void ResizeBorder_PointerMovedCore(object sender, PointerRoutedEventArgs e)
    {
        if (!IsResizing)
        {
            return;
        }

        if (Win32Helper.TryGetPointerScreenPoint(HWnd, RootElement, e, out var currentPt) ||
            Win32Helper.GetCursorPos(out currentPt))
        {
            QueueInteractiveResizePointer(new PointInt32(currentPt.X, currentPt.Y));
        }
        e.Handled = true;
    }

    private RectInt32 ResolveInteractiveResizeBounds(PointInt32 currentPt)
    {
        int deltaX = currentPt.X - InitialPointerPt.X;
        int deltaY = currentPt.Y - InitialPointerPt.Y;

        int newWidth = InitialWindowSize.Width;
        int newHeight = InitialWindowSize.Height;
        int newX = InitialWindowPos.X;
        int newY = InitialWindowPos.Y;

        if (IsCompactBoundsStateActive)
        {
            var limits = GetCompactPhysicalWidthLimits();
            if (ResizeDirection == "Right")
            {
                newWidth = Math.Clamp(InitialWindowSize.Width + deltaX, limits.MinWidth, limits.MaxWidth);
            }
            else if (ResizeDirection == "Left")
            {
                int rightEdge = InitialWindowPos.X + InitialWindowSize.Width;
                newWidth = Math.Clamp(InitialWindowSize.Width - deltaX, limits.MinWidth, limits.MaxWidth);
                newX = rightEdge - newWidth;
            }

            var compactProposed = new RectInt32(newX, newY, newWidth, newHeight);
            var compactSnapped = App.Current.ResizeGuideOverlay.UpdateGuidesAndSnap(
                compactProposed,
                ResizeDirection,
                limits.MinWidth,
                limits.MaxWidth);
            return compactSnapped;
        }

        SizeInt32 minSize = _interactiveResizeMinimumSize;

        if (ResizeDirection.Contains("Right"))
        {
            newWidth = Math.Max(minSize.Width, InitialWindowSize.Width + deltaX);
        }
        else if (ResizeDirection.Contains("Left"))
        {
            int rightEdge = InitialWindowPos.X + InitialWindowSize.Width;
            newWidth = Math.Max(minSize.Width, InitialWindowSize.Width - deltaX);
            newX = rightEdge - newWidth;
        }

        if (ResizeDirection.Contains("Bottom"))
        {
            newHeight = Math.Max(minSize.Height, InitialWindowSize.Height + deltaY);
        }
        else if (ResizeDirection.Contains("Top"))
        {
            int bottomEdge = InitialWindowPos.Y + InitialWindowSize.Height;
            newHeight = Math.Max(minSize.Height, InitialWindowSize.Height - deltaY);
            newY = bottomEdge - newHeight;
        }

        var proposed = new RectInt32(newX, newY, newWidth, newHeight);
        var snapped = App.Current.ResizeGuideOverlay.UpdateGuidesAndSnap(proposed, ResizeDirection);
        return AnchorExpandedResizeBounds(snapped);
    }

    private void CommitInteractiveResizeBounds()
    {
        // Keep the resize guard active while the final coalesced frame is
        // committed. In particular, compact-bound settlement must not restore
        // the pre-drag capsule width before Win10 has exposed the final HWND
        // rectangle to GetWindowRect.
        FlushPendingInteractiveResizeBounds();
        RectInt32 finalBounds = GetActualWindowBounds();

        // Capture-lost can be raised synchronously when pointer capture is
        // released below. Mark the gesture complete first, but keep AppWindow
        // config synchronization deferred until the authoritative bounds have
        // been persisted and any capsule-bar constraint has been refreshed.
        IsResizing = false;
        try
        {
            PersistCompletedWidgetResize(finalBounds);
        }
        catch (Exception ex)
        {
            // R21-S2-01: persisting the final bounds reaches the settings store
            // and can throw. Letting it escape skipped the whole resize cleanup
            // chain in both pointer handlers (guide, capture, interaction depth,
            // backdrop), leaving the window stuck in resize mode. The failure is
            // logged instead; cleanup then runs on its normal path.
            App.Log($"[Resize] Failed to persist completed widget resize: {ex}");
        }
        finally
        {
            _deferInteractiveResizeConfigUpdates = false;
        }
    }

    protected void ResizeBorder_PointerReleasedCore(object sender, PointerRoutedEventArgs e)
    {
        if (!IsResizing || sender is not FrameworkElement element)
        {
            return;
        }

        CommitInteractiveResizeBounds();
        element.ReleasePointerCapture(e.Pointer);
        App.Current.ResizeGuideOverlay.EndResize();
        ResizeDirection = string.Empty;
        EndWidgetBoundsInteraction();
        OnResizeEnd();
        EndInteractiveResizePerformanceSession();
        DisplayChangeWatcher?.ResumeRestore();
        RestoreBackdropAfterInteraction();
        QueueBackdropRefresh();
        e.Handled = true;
    }

    /// <summary>
    /// Handles PointerCaptureLost for resize borders.  If the system steals
    /// pointer capture mid-resize (e.g., alt-tab, UAC, tablet mode), the
    /// PointerReleased event never fires and the resize guide highlights
    /// would be leaked forever.  This method ensures cleanup.
    /// </summary>
    protected void ResizeBorder_PointerCaptureLostCore(object sender, PointerRoutedEventArgs e)
    {
        if (!IsResizing)
        {
            return;
        }

        CommitInteractiveResizeBounds();
        DragCaptureElement = null;
        App.Current?.ResizeGuideOverlay.EndResize();
        ResizeDirection = string.Empty;
        EndWidgetBoundsInteraction();
        OnResizeEnd();
        EndInteractiveResizePerformanceSession();
        DisplayChangeWatcher?.ResumeRestore();
        RestoreBackdropAfterInteraction();
        QueueBackdropRefresh();
        e.Handled = true;
    }

    protected InputSystemCursorShape GetResizeCursorShapeForCurrentState(string? direction)
    {
        string resolvedDirection = WidgetCompactInteractionPolicy.ResolveResizeDirection(
            IsCompactBoundsStateActive,
            direction);
        if (IsSizeLocked || !CanResizeCurrentWidgetState(resolvedDirection))
        {
            return InputSystemCursorShape.Arrow;
        }

        return resolvedDirection switch
        {
            "Left" or "Right" => InputSystemCursorShape.SizeWestEast,
            "Top" or "Bottom" => InputSystemCursorShape.SizeNorthSouth,
            "TopLeft" or "BottomRight" => InputSystemCursorShape.SizeNorthwestSoutheast,
            "TopRight" or "BottomLeft" => InputSystemCursorShape.SizeNortheastSouthwest,
            _ => InputSystemCursorShape.Arrow
        };
    }

    /// <summary>
    /// Handles PointerCaptureLost for drag-move.  Same rationale as
    /// ResizeBorder_PointerCaptureLostCore — ensures EndDrag is called
    /// even when the system steals pointer capture mid-drag.
    /// </summary>
    protected void DragPointerCaptureLostCore(object sender, PointerRoutedEventArgs e)
    {
        if (!IsDragging)
        {
            return;
        }

        FlushPendingTitleBarDragFrame();
        _deferTitleBarDragConfigUpdates = false;
        IsDragging = false;
        bool hasMoved = HasMovedTitleBarDrag;
        // DEF-094: mirror the pointer-released path's wasEngaged gate — a
        // capture loss without an engaged drag (system gesture, focus steal)
        // must not churn the desktop-layer restore state.
        bool wasEngaged = _isWindowDragEngaged;
        _isWindowDragEngaged = false;
        DragCaptureElement = null;
        if (wasEngaged)
        {
            App.Current?.ResizeGuideOverlay.EndDrag();
        }

        if (_isCoordinatedMoveDrag &&
            App.Current?.WidgetManager?.CompleteCoordinatedMove(HWnd, hasMoved) == true)
        {
            _isCoordinatedMoveDrag = false;
            App.Current.WidgetManager.CancelWidgetGroupDrag(Config.Id);
            HasMovedTitleBarDrag = false;
            App.Current.WidgetManager.RestoreTemporarilyRaisedWidgetsToDesktopLayer(
                "coordinated-drag-capture-lost");
            e.Handled = true;
            return;
        }

        _isCoordinatedMoveDrag = false;
        if (hasMoved)
        {
            CompleteCompactArrangementDrag();
            RectInt32 finalBounds = GetActualWindowBounds();
            finalBounds = CompleteExpandedWidgetDrag(finalBounds);
            CommitUserPlacementFromWindow(WidgetPlacementSource.Drag);
            if (_targetCollapsed)
            {
                _suppressHoverExpansionAfterDragUntilPointerExit = true;
            }
        }
        EndWidgetBoundsInteraction();
        OnDragEnd(hasMoved);
        if (hasMoved && !IsCompactBoundsStateActive)
        {
            _ = App.Current?.WidgetManager?.CompleteWidgetGroupDragAsync(Config.Id);
        }
        else
        {
            App.Current?.WidgetManager?.CancelWidgetGroupDrag(Config.Id);
        }
        DisplayChangeWatcher?.ResumeRestore();
        HasMovedTitleBarDrag = false;
        RestoreBackdropAfterInteraction();
        // DEF-094: same wasEngaged gate as the pointer-released path — a
        // capture loss during a click that never engaged the drag must not
        // churn the desktop-layer restore state.
        if (wasEngaged)
        {
            QueueBackdropRefresh();
            App.Current?.WidgetManager?.RestoreTemporarilyRaisedWidgetsToDesktopLayer(
                "drag-capture-lost");
        }

        e.Handled = true;
    }

    // ── Interaction layer helpers ──────────────────────────────

    protected void BeginInteractionLayer(string reason, bool elevate = true)
    {
        BeginCompactInteraction();
        App.Current.WidgetManager?.BeginWidgetInteraction(reason);
        if (elevate)
        {
            ElevateForInteraction();
        }
    }

    protected void ReleaseInteractionLayer(string reason)
    {
        EndCompactInteraction();
        App.Current.WidgetManager?.EndWidgetInteraction(reason);
        // While the tray raised session is alive its restore monitor owns the
        // layer; outside a session this window restores itself.
        if (App.Current.WidgetManager is { WidgetsRaisedFromTray: true })
        {
            return;
        }

        RestoreDesktopLayer();
    }

    // ── Chained flyout handoff ───────────────────────────────

    /// <summary>
    /// Holds the compact and widget-manager interaction leases across a
    /// chained flyout transition, where one flyout closes and a successor
    /// opens on a later dispatcher turn (for example a context menu handing
    /// off to the foreground color picker). The lease intentionally does not
    /// elevate; the successor flyout's own show path owns Z-order.
    /// </summary>
    protected IDisposable AcquireFlyoutHandoff(string reason)
    {
        BeginCompactInteraction();
        WidgetManager? widgetManager = App.Current.WidgetManager;
        widgetManager?.BeginWidgetInteraction(reason);
        return new FlyoutInteractionHandoff(this, widgetManager, reason);
    }

    protected void QueueInteractionGuardedShow(
        IDisposable? interactionHandoff,
        Func<Task> showAsync)
    {
        if (DispatcherQueue.TryEnqueue(async () =>
        {
            try
            {
                // The show delegate acquires its own interaction (see the
                // window-level flyout show helpers) before this handoff is
                // released, so a grouped Smart capsule cannot collapse
                // between the two flyouts.
                await showAsync();
            }
            catch (Exception ex)
            {
                App.Log($"{LogPrefix} Queued flyout failed: {ex}");
            }
            finally
            {
                interactionHandoff?.Dispose();
            }
        }))
        {
            return;
        }

        interactionHandoff?.Dispose();
    }

    private sealed class FlyoutInteractionHandoff : IDisposable
    {
        private WidgetWindowBase? _owner;
        private WidgetManager? _widgetManager;
        private readonly string _reason;

        public FlyoutInteractionHandoff(
            WidgetWindowBase owner,
            WidgetManager? widgetManager,
            string reason)
        {
            _owner = owner;
            _widgetManager = widgetManager;
            _reason = reason;
        }

        public void Dispose()
        {
            WidgetWindowBase? owner = Interlocked.Exchange(ref _owner, null);
            WidgetManager? widgetManager = Interlocked.Exchange(
                ref _widgetManager,
                null);
            if (owner is null)
            {
                return;
            }

            owner.EndCompactInteraction();
            widgetManager?.EndWidgetInteraction($"{_reason}-completed");
        }
    }

    // ── Tray animation helpers ─────────────────────────────────

    protected WidgetTrayAnimationProfile GetTrayAnimationProfile()
    {
        return TrayAnimation.CreateProfile(WidgetAnimationSettings.From(SettingsService.Settings));
    }

    protected void LogTrayWindow(string message)
    {
        App.LogVerbose(Diagnostics.FormatTrayWindowMessage(message));
    }

    // ── Cleanup ────────────────────────────────────────────────
}
