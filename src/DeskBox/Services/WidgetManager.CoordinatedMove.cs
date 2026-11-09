using DeskBox.Helpers;
using DeskBox.Platform;
using Windows.Graphics;

namespace DeskBox.Services;

public sealed partial class WidgetManager
{
    private CoordinatedMoveSession? _coordinatedMoveSession;

    public bool TryBeginCoordinatedMove(IntPtr sourceWindowHandle)
    {
        if (_coordinatedMoveSession is not null || sourceWindowHandle == IntPtr.Zero)
        {
            return false;
        }

        IDesktopWidgetWindow? source = GetLoadedDesktopWindows().FirstOrDefault(window =>
            window.WindowHandle == sourceWindowHandle &&
            window.Visible &&
            window.CanParticipateInCoordinatedMove);
        if (source is null)
        {
            return false;
        }

        IntPtr sourceMonitor = Win32Helper.MonitorFromWindow(
            sourceWindowHandle,
            Win32Helper.MONITOR_DEFAULTTONEAREST);
        if (sourceMonitor == IntPtr.Zero)
        {
            return false;
        }

        CoordinatedMoveEntry[] entries = GetLoadedDesktopWindows()
            .Where(window =>
                window.Visible &&
                window.CanParticipateInCoordinatedMove &&
                Win32Helper.MonitorFromWindow(
                    window.WindowHandle,
                    Win32Helper.MONITOR_DEFAULTTONEAREST) == sourceMonitor)
            .Select(window => new CoordinatedMoveEntry(
                window,
                window.CoordinatedMoveBounds,
                window.WindowHandle == sourceWindowHandle))
            .OrderByDescending(entry => entry.IsSource)
            .ToArray();
        if (entries.Length < 2 || entries.All(entry => !entry.IsSource))
        {
            return false;
        }

        RectInt32 sourceBounds = source.CoordinatedMoveBounds;
        int centerX = sourceBounds.X + sourceBounds.Width / 2;
        int centerY = sourceBounds.Y + sourceBounds.Height / 2;
        if (!Win32Helper.TryGetMonitorWorkArea(centerX, centerY, out _, out var nativeWorkArea))
        {
            return false;
        }

        RectInt32 workArea = new(
            nativeWorkArea.Left,
            nativeWorkArea.Top,
            nativeWorkArea.Right - nativeWorkArea.Left,
            nativeWorkArea.Bottom - nativeWorkArea.Top);
        var session = new CoordinatedMoveSession(
            sourceWindowHandle,
            entries,
            WidgetCoordinatedMoveCalculator.GetUnion(entries.Select(entry => entry.StartBounds)),
            workArea);

        // Begin every participant BEFORE publishing the session: a Begin that
        // throws used to leave _coordinatedMoveSession assigned with half of
        // the group never armed, which permanently blocked all future
        // coordinated drags (:13 guard) until process restart. On failure,
        // unwind the already-armed participants and leave no session behind.
        int armed = 0;
        try
        {
            foreach (CoordinatedMoveEntry entry in entries)
            {
                entry.Window.BeginCoordinatedMoveParticipation(entry.IsSource);
                armed++;
            }
        }
        catch (Exception ex)
        {
            App.Log(
                $"[CoordinatedMove] Begin failed after {armed}/{entries.Length} " +
                $"participants (0x{sourceWindowHandle.ToInt64():X}): {ex.Message}");
            for (int i = 0; i < armed; i++)
            {
                try
                {
                    // Mirror AbortCoordinatedMoveForRemovedWindow's survivor
                    // handling: not-moved completion unwinds pit #4's
                    // interaction depth for followers and ends the source's
                    // drag cleanly.
                    entries[i].Window.CompleteCoordinatedMoveParticipation(
                        hasMoved: false,
                        entries[i].IsSource);
                }
                catch (Exception unwindEx)
                {
                    App.Log($"[CoordinatedMove] Unwind {i} failed: {unwindEx.Message}");
                }
            }

            return false;
        }

        _coordinatedMoveSession = session;

        App.Log(
            $"[CoordinatedMove] Begin source=0x{sourceWindowHandle.ToInt64():X} " +
            $"count={entries.Length} monitor=0x{sourceMonitor.ToInt64():X}");
        return true;
    }

    // R1-030: destroying a participating window (the source or a follower)
    // invalidates the session — the source's drag-end can never arrive for a
    // closed window, and a lingering session blocks every future coordinated
    // drag. Survivors take the not-moved completion so pit #4's interaction
    // depth unwinds; the removed window's own participation is skipped: its
    // teardown already ran and re-entering it would act on a dead HWND.
    internal void AbortCoordinatedMoveForRemovedWindow(IDesktopWidgetWindow removedWindow)
    {
        if (_coordinatedMoveSession is not { } session ||
            !session.Entries.Any(entry => ReferenceEquals(entry.Window, removedWindow)))
        {
            return;
        }

        _coordinatedMoveSession = null;
        foreach (CoordinatedMoveEntry entry in session.Entries)
        {
            if (ReferenceEquals(entry.Window, removedWindow))
            {
                continue;
            }

            try
            {
                entry.Window.CompleteCoordinatedMoveParticipation(
                    hasMoved: false,
                    entry.IsSource);
            }
            catch (Exception ex)
            {
                App.Log(
                    "[CoordinatedMove] Abort completion failed " +
                    $"hwnd=0x{entry.Window.WindowHandle.ToInt64():X}: {ex.Message}");
            }
        }

        App.Log(
            "[CoordinatedMove] Aborted by window removal " +
            $"source=0x{session.SourceWindowHandle.ToInt64():X} count={session.Entries.Length}");
    }

    public bool UpdateCoordinatedMove(
        IntPtr sourceWindowHandle,
        int requestedDeltaX,
        int requestedDeltaY)
    {
        if (_coordinatedMoveSession is not { } session ||
            session.SourceWindowHandle != sourceWindowHandle)
        {
            return false;
        }

        PointInt32 delta = WidgetCoordinatedMoveCalculator.ClampDelta(
            session.GroupStartBounds,
            new PointInt32(requestedDeltaX, requestedDeltaY),
            session.WorkArea);
        if (delta.X == session.LastAppliedDelta.X &&
            delta.Y == session.LastAppliedDelta.Y)
        {
            return true;
        }

        CoordinatedMoveTarget[] targets = session.Targets;
        for (int index = 0; index < session.Entries.Length; index++)
        {
            CoordinatedMoveEntry entry = session.Entries[index];
            targets[index] = new CoordinatedMoveTarget(
                entry,
                new RectInt32(
                    entry.StartBounds.X + delta.X,
                    entry.StartBounds.Y + delta.Y,
                    entry.StartBounds.Width,
                    entry.StartBounds.Height));
        }
        foreach (CoordinatedMoveTarget target in targets)
        {
            target.Entry.Window.PrepareCoordinatedMoveBounds(target.Bounds);
        }

        bool committed;
        try
        {
            committed = TryCommitCoordinatedMoveBatch(targets);
        }
        finally
        {
            foreach (CoordinatedMoveTarget target in targets)
            {
                target.Entry.Window.CompleteCoordinatedMoveBoundsPreview();
            }
        }

        if (!committed)
        {
            foreach (CoordinatedMoveTarget target in targets)
            {
                target.Entry.Window.ApplyCoordinatedMoveBoundsFallback(target.Bounds);
            }
        }

        NoteWidgetWindowsMoved();
        session.LastAppliedDelta = delta;
        return true;
    }

    public bool CompleteCoordinatedMove(
        IntPtr sourceWindowHandle,
        bool hasMoved)
    {
        if (_coordinatedMoveSession is not { } session ||
            session.SourceWindowHandle != sourceWindowHandle)
        {
            return false;
        }

        _coordinatedMoveSession = null;
        bool committedMove = hasMoved &&
            (session.LastAppliedDelta.X != 0 || session.LastAppliedDelta.Y != 0);
        foreach (CoordinatedMoveEntry entry in session.Entries)
        {
            try
            {
                entry.Window.CompleteCoordinatedMoveParticipation(
                    committedMove,
                    entry.IsSource);
            }
            catch (Exception ex)
            {
                App.Log(
                    $"[CoordinatedMove] Complete failed hwnd=0x{entry.Window.WindowHandle.ToInt64():X}: " +
                    ex.Message);
            }
        }

        if (committedMove)
        {
            _settingsService.UpdateWidgetsBatch(
                session.Entries.Select(entry => entry.Window.Config),
                notifySubscribers: false);
            RefreshCapsuleBarLayout();
        }

        App.Log(
            $"[CoordinatedMove] End source=0x{sourceWindowHandle.ToInt64():X} " +
            $"count={session.Entries.Length} moved={committedMove} " +
            $"delta={session.LastAppliedDelta.X},{session.LastAppliedDelta.Y}");
        return true;
    }

    private static bool TryCommitCoordinatedMoveBatch(
        IReadOnlyList<CoordinatedMoveTarget> targets)
    {
        IntPtr deferred = Win32Helper.BeginDeferWindowPos(targets.Count);
        if (deferred == IntPtr.Zero)
        {
            return false;
        }

        const uint flags =
            Win32Helper.SWP_NOZORDER |
            Win32Helper.SWP_NOACTIVATE |
            Win32Helper.SWP_NOOWNERZORDER;
        foreach (CoordinatedMoveTarget target in targets)
        {
            RectInt32 bounds = target.Bounds;
            IntPtr next = Win32Helper.DeferWindowPos(
                deferred,
                target.Entry.Window.WindowHandle,
                IntPtr.Zero,
                bounds.X,
                bounds.Y,
                bounds.Width,
                bounds.Height,
                flags);
            if (next == IntPtr.Zero)
            {
                // DeferWindowPos destroys the transaction on failure.
                // Submitting the previous, now-invalid handle to End is a
                // use-after-free; a small leak is preferred, and the
                // caller's per-window fallback still moves every window.
                App.LogVerbose(
                    "[WidgetManager] DeferWindowPos rejected a staged move; " +
                    "abandoning the batch for the per-window fallback.");
                return false;
            }

            deferred = next;
        }

        return Win32Helper.EndDeferWindowPos(deferred);
    }

    private sealed class CoordinatedMoveSession(
        IntPtr sourceWindowHandle,
        CoordinatedMoveEntry[] entries,
        RectInt32 groupStartBounds,
        RectInt32 workArea)
    {
        public IntPtr SourceWindowHandle { get; } = sourceWindowHandle;
        public CoordinatedMoveEntry[] Entries { get; } = entries;
        public RectInt32 GroupStartBounds { get; } = groupStartBounds;
        public RectInt32 WorkArea { get; } = workArea;
        public CoordinatedMoveTarget[] Targets { get; } =
            new CoordinatedMoveTarget[entries.Length];
        public PointInt32 LastAppliedDelta { get; set; }
    }

    private readonly record struct CoordinatedMoveEntry(
        IDesktopWidgetWindow Window,
        RectInt32 StartBounds,
        bool IsSource);

    private readonly record struct CoordinatedMoveTarget(
        CoordinatedMoveEntry Entry,
        RectInt32 Bounds);
}
