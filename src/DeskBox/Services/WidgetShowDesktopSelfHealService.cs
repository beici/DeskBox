using DeskBox.Helpers;
using Microsoft.UI.Dispatching;

namespace DeskBox.Services;

/// <summary>
/// Watches for shell-level Show Desktop / Win+D activity and asks the widget
/// manager to verify resting widget visibility once the activity settles.
///
/// Two event sources cover both shell implementations: classic minimize
/// storms (EVENT_SYSTEM_MINIMIZESTART/END) and cloak-based toggles, which at
/// minimum change the foreground window (EVENT_SYSTEM_FOREGROUND).
/// The hook is registered with WINEVENT_OUTOFCONTEXT from the dispatcher
/// thread, so callbacks arrive on that thread's message loop without
/// marshalling. Events are only debounced here; all verification work (and
/// its logging) lives in
/// <see cref="WidgetManager.VerifyRestingWidgetsAfterShellMinimize"/>, which
/// stays idempotent when every widget is already visible.
/// </summary>
internal sealed class WidgetShowDesktopSelfHealService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

    private const int ObjectIdWindow = 0;

    private readonly DispatcherQueue _dispatcherQueue;
    private readonly DispatcherQueueTimer _debounceTimer;
    private readonly Action<string> _verifyAction;
    private readonly Win32Helper.WinEventProc _winEventProc;
    private IntPtr _minimizeHook;
    private IntPtr _foregroundHook;
    private bool _disposed;

    public WidgetShowDesktopSelfHealService(
        DispatcherQueue dispatcherQueue,
        Action<string> verifyAction)
    {
        _dispatcherQueue = dispatcherQueue;
        _verifyAction = verifyAction;
        _winEventProc = WinEventCallback;
        _debounceTimer = dispatcherQueue.CreateTimer();
        _debounceTimer.Interval = DebounceDelay;
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    public void Start()
    {
        if (_disposed || _minimizeHook != IntPtr.Zero)
        {
            return;
        }

        // Out-of-context WinEvents are delivered to the registering thread's
        // message loop. The async startup continuation that constructs this
        // service can resume on a thread-pool thread, where the hook would
        // register successfully yet never receive a single event. Register
        // from the dispatcher thread so its message loop owns the callback.
        if (_dispatcherQueue.HasThreadAccess)
        {
            StartCore();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(StartCore);
        }
    }

    private void StartCore()
    {
        if (_disposed || _minimizeHook != IntPtr.Zero)
        {
            return;
        }

        uint flags = Win32Helper.WINEVENT_OUTOFCONTEXT | Win32Helper.WINEVENT_SKIPOWNPROCESS;
        _minimizeHook = Win32Helper.SetWinEventHook(
            Win32Helper.EVENT_SYSTEM_MINIMIZESTART,
            Win32Helper.EVENT_SYSTEM_MINIMIZEEND,
            IntPtr.Zero,
            _winEventProc,
            idProcess: 0,
            idThread: 0,
            flags);
        _foregroundHook = Win32Helper.SetWinEventHook(
            Win32Helper.EVENT_SYSTEM_FOREGROUND,
            Win32Helper.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero,
            _winEventProc,
            idProcess: 0,
            idThread: 0,
            flags);
        App.Log(
            "[ShowDesktop] Self-heal watcher started " +
            $"minimizeHook=0x{_minimizeHook.ToInt64():X} " +
            $"foregroundHook=0x{_foregroundHook.ToInt64():X}");
    }

    private void WinEventCallback(
        IntPtr hWinEventHook,
        uint eventId,
        IntPtr hWnd,
        int idObject,
        int idChild,
        uint idThread,
        uint dwmsEventTime)
    {
        if (_disposed || idObject != ObjectIdWindow)
        {
            return;
        }

        // Out-of-context events are delivered on the registering (UI) thread;
        // only restart the debounce here, never touch widget state.
        _debounceTimer.Stop();
        _debounceTimer.Start();
    }

    private void DebounceTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        _debounceTimer.Stop();
        if (_disposed)
        {
            return;
        }

        try
        {
            _verifyAction("minimize-storm");
        }
        catch (Exception ex)
        {
            App.Log($"[ShowDesktop] Self-heal verification failed: {ex.Message}");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _debounceTimer.Stop();
        _debounceTimer.Tick -= DebounceTimer_Tick;

        IntPtr minimizeHook = _minimizeHook;
        _minimizeHook = IntPtr.Zero;
        IntPtr foregroundHook = _foregroundHook;
        _foregroundHook = IntPtr.Zero;
        if (minimizeHook != IntPtr.Zero)
        {
            _ = Win32Helper.UnhookWinEvent(minimizeHook);
        }

        if (foregroundHook != IntPtr.Zero)
        {
            _ = Win32Helper.UnhookWinEvent(foregroundHook);
        }
    }
}
