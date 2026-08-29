using DeskBox.Helpers;
using Microsoft.UI.Dispatching;

namespace DeskBox.Services;

/// <summary>
/// Watches for shell-level minimize storms — the signature of the taskbar
/// "Show Desktop" button or Win+D — and asks the widget manager to verify
/// resting widget visibility once the storm settles.
///
/// The hook is registered with WINEVENT_OUTOFCONTEXT from the UI thread, so
/// the callback arrives on that thread's message loop without marshalling.
/// Events are only debounced here; all verification work (and its logging)
/// lives in <see cref="WidgetManager.VerifyRestingWidgetsAfterShellMinimize"/>,
/// which stays idempotent when every widget is already visible.
/// </summary>
internal sealed class WidgetShowDesktopSelfHealService : IDisposable
{
    private static readonly TimeSpan DebounceDelay = TimeSpan.FromMilliseconds(700);

    private const int ObjectIdWindow = 0;

    private readonly DispatcherQueueTimer _debounceTimer;
    private readonly Action<string> _verifyAction;
    private readonly Win32Helper.WinEventProc _winEventProc;
    private IntPtr _hook;
    private bool _disposed;

    public WidgetShowDesktopSelfHealService(
        DispatcherQueue dispatcherQueue,
        Action<string> verifyAction)
    {
        _verifyAction = verifyAction;
        _winEventProc = WinEventCallback;
        _debounceTimer = dispatcherQueue.CreateTimer();
        _debounceTimer.Interval = DebounceDelay;
        _debounceTimer.IsRepeating = false;
        _debounceTimer.Tick += DebounceTimer_Tick;
    }

    public void Start()
    {
        if (_disposed || _hook != IntPtr.Zero)
        {
            return;
        }

        _hook = Win32Helper.SetWinEventHook(
            Win32Helper.EVENT_SYSTEM_MINIMIZESTART,
            Win32Helper.EVENT_SYSTEM_MINIMIZESTART,
            IntPtr.Zero,
            _winEventProc,
            idProcess: 0,
            idThread: 0,
            Win32Helper.WINEVENT_OUTOFCONTEXT | Win32Helper.WINEVENT_SKIPOWNPROCESS);
        App.Log($"[ShowDesktop] Self-heal watcher started hook=0x{_hook.ToInt64():X}");
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

        IntPtr hook = _hook;
        _hook = IntPtr.Zero;
        if (hook != IntPtr.Zero)
        {
            _ = Win32Helper.UnhookWinEvent(hook);
        }
    }
}
