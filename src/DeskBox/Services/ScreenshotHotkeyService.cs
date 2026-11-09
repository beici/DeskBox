using DeskBox.Models;
using DeskBox.Platform;

namespace DeskBox.Services;

/// <summary>
/// Registers the optional screenshot hotkey on the tray window without a
/// keyboard hook (ported from the bbkien fork's ScreenshotHotkeyService,
/// commit 414c7719; gestures live in the per-feature ScreenshotSettingsStore
/// instead of AppSettings). The store's <see cref="ScreenshotSettingsStore.Changed"/>
/// event drives RefreshRegistration when the user edits the gesture.
/// </summary>
public sealed class ScreenshotHotkeyService : IDisposable
{
    private const int ScreenshotHotkeyId = 0x4450;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModShift = 0x0004;
    private const uint ModWindows = 0x0008;
    private const uint ModNoRepeat = 0x4000;
    private static readonly UIntPtr SubclassId = new(0x4450);

    private readonly ScreenshotSettingsStore _settingsStore;
    private readonly Func<Task> _invokeAsync;
    private readonly Win32Helper.SubclassProc _subclassProc;
    private IntPtr _windowHandle;
    // Volatile: written on the RegisterHotkey/subclass callback threads,
    // read on the UI thread.
    private volatile bool _isSubclassInstalled;
    private volatile bool _isRegistered;
    private volatile bool _isInvoking;
    private long _dispatchFailureSequence;

    public long DispatchFailureCount => Interlocked.Read(ref _dispatchFailureSequence);

    public ScreenshotHotkeyService(ScreenshotSettingsStore settingsStore, Func<Task> invokeAsync)
    {
        _settingsStore = settingsStore;
        _invokeAsync = invokeAsync;
        _subclassProc = CallbackDepth.WrapSubclassProc(WindowSubclassProc);
    }

    public bool IsRegistered => _isRegistered;

    public GlobalHotkeyGesture CurrentGesture
    {
        get
        {
            ScreenshotSettings settings = _settingsStore.Load();
            return GlobalHotkeyService.NormalizeGesture(
                settings.HotkeyModifiers,
                settings.HotkeyKey);
        }
    }

    public void Attach(IntPtr windowHandle)
    {
        if (windowHandle == IntPtr.Zero)
        {
            return;
        }

        Detach();
        _windowHandle = windowHandle;
        _isSubclassInstalled = Win32Helper.SetWindowSubclass(
            _windowHandle, _subclassProc, SubclassId, UIntPtr.Zero);
        RefreshRegistration();
    }

    public void RefreshRegistration()
    {
        Unregister();
        ScreenshotSettings settings = _settingsStore.Load();
        // Without a subclass on the host window WM_HOTKEY arrives unhandled;
        // registering the chord anyway would show "Active" in settings while
        // the keypress does nothing (GlobalHotkeyService refuses the same way).
        if (_windowHandle == IntPtr.Zero || !_isSubclassInstalled || !settings.HotkeyEnabled)
        {
            return;
        }

        GlobalHotkeyGesture gesture = GlobalHotkeyService.NormalizeGesture(
            settings.HotkeyModifiers,
            settings.HotkeyKey);
        if (!GlobalHotkeyService.IsValidGesture(gesture) ||
            !Win32Helper.RegisterHotKey(
                _windowHandle,
                ScreenshotHotkeyId,
                ToWin32Modifiers(gesture.Modifiers) | ModNoRepeat,
                (uint)gesture.VirtualKey))
        {
            App.Log($"[ScreenshotHotkey] Failed to register gesture={FormatGesture(gesture)}");
            return;
        }

        _isRegistered = true;
        App.Log($"[ScreenshotHotkey] Registered gesture={FormatGesture(gesture)}");
    }

    public void Detach()
    {
        Unregister();
        if (_isSubclassInstalled && _windowHandle != IntPtr.Zero)
        {
            Win32Helper.RemoveWindowSubclass(_windowHandle, _subclassProc, SubclassId);
        }

        _isSubclassInstalled = false;
        _windowHandle = IntPtr.Zero;
    }

    public void Dispose() => Detach();

    private IntPtr WindowSubclassProc(
        IntPtr hWnd,
        uint message,
        UIntPtr wParam,
        IntPtr lParam,
        UIntPtr uIdSubclass,
        UIntPtr dwRefData)
    {
        if (message == GlobalHotkeyService.WmHotkey &&
            wParam.ToUInt32() == ScreenshotHotkeyId)
        {
            Win32Helper.ReleaseAllModifiers();
            // Mirror the Search/Global hotkey services: a rejected enqueue is
            // observable (counter + log) instead of silently dropping the
            // screenshot hotkey press.
            if (!App.UiDispatcherQueue.TryEnqueue(() => _ = InvokeAsync()))
            {
                Interlocked.Increment(ref _dispatchFailureSequence);
                App.Log("[ScreenshotHotkey] UI dispatch rejected");
            }
            return IntPtr.Zero;
        }

        return Win32Helper.DefSubclassProc(hWnd, message, wParam, lParam);
    }

    private async Task InvokeAsync()
    {
        if (_isInvoking)
        {
            return;
        }

        _isInvoking = true;
        try
        {
            await _invokeAsync();
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotHotkey] Invocation failed: {ex}");
        }
        finally
        {
            _isInvoking = false;
        }
    }

    private void Unregister()
    {
        if (_isRegistered && _windowHandle != IntPtr.Zero)
        {
            Win32Helper.UnregisterHotKey(_windowHandle, ScreenshotHotkeyId);
        }

        _isRegistered = false;
    }

    private static uint ToWin32Modifiers(HotkeyModifierKeys modifiers)
    {
        uint value = 0;
        if (modifiers.HasFlag(HotkeyModifierKeys.Alt)) value |= ModAlt;
        if (modifiers.HasFlag(HotkeyModifierKeys.Control)) value |= ModControl;
        if (modifiers.HasFlag(HotkeyModifierKeys.Shift)) value |= ModShift;
        if (modifiers.HasFlag(HotkeyModifierKeys.Windows)) value |= ModWindows;
        return value;
    }

    private static string FormatGesture(GlobalHotkeyGesture gesture) =>
        $"{gesture.Modifiers}+VK:{gesture.VirtualKey:X2}";
}
