using DeskBox.Services;
using DeskBox.Views;
using Microsoft.UI.Dispatching;

namespace DeskBox;

/// <summary>
/// Screenshot capture entry point. The overlay is a singleton while open:
/// a second trigger (hotkey retry, tray double activation) focuses nothing
/// and is dropped instead of stacking two frozen-screen overlays.
/// </summary>
public sealed partial class App
{
    private int _screenshotWindowOpen;
    private ScreenshotSettingsStore? _screenshotSettingsStore;
    private ScreenshotHotkeyService? _screenshotHotkeyService;
    private readonly List<ScreenshotPinWindow> _screenshotPins = [];

    internal ScreenshotHotkeyService? ScreenshotHotkeyService => _screenshotHotkeyService;

    /// <summary>
    /// Pinned screenshots outlive the capture overlay, so the app has to hold
    /// the reference: a WinUI <see cref="Microsoft.UI.Xaml.Window"/> nothing
    /// references can be collected while its native window is still on screen.
    /// </summary>
    internal List<ScreenshotPinWindow> ScreenshotPins => _screenshotPins;

    internal async Task ShowScreenshotCaptureAsync()
    {
        if (Interlocked.CompareExchange(ref _screenshotWindowOpen, 1, 0) != 0)
        {
            return;
        }

        // The ctor performs Win32 plumbing and InitializeAsync captures the
        // frozen frame before anything can fire Closed, so a throw anywhere
        // in this path must release the gate or the hotkey stays dead until
        // app restart.
        ScreenshotCaptureWindow? window = null;
        try
        {
            window = new ScreenshotCaptureWindow();
            window.Closed += (_, _) => Volatile.Write(ref _screenshotWindowOpen, 0);
            await window.InitializeAsync();
            window.Activate();
        }
        catch (Exception ex)
        {
            try
            {
                window?.Close();
            }
            catch (Exception closeError)
            {
                Log($"[Screenshot] Failed capture window cleanup: {closeError}");
            }
            Volatile.Write(ref _screenshotWindowOpen, 0);
            Log($"[Screenshot] Capture window failed to open: {ex}");
        }
    }

    /// <summary>
    /// Attaches the screenshot hotkey to the tray host window (the same
    /// silent host the search hotkey uses). Safe to call repeatedly: the
    /// service refreshes its registration whenever the settings store
    /// changes, so gesture edits apply without reattaching.
    /// </summary>
    private void EnsureScreenshotHotkeyService(IntPtr trayHwnd)
    {
        if (_screenshotHotkeyService is not null)
        {
            return;
        }

        try
        {
            _screenshotSettingsStore ??= new ScreenshotSettingsStore();
            ScreenshotHotkeyService service = new(
                _screenshotSettingsStore,
                () =>
                {
                    // The subclass proc already marshals to the UI thread.
                    return ShowScreenshotCaptureAsync();
                });
            ScreenshotSettingsStore.Changed += RefreshScreenshotHotkeyRegistration;
            service.Attach(trayHwnd);
            _screenshotHotkeyService = service;
            Log("[Screenshot] Hotkey service attached");
        }
        catch (Exception ex)
        {
            Log($"[Screenshot] Hotkey service initialization failed: {ex}");
            ScreenshotSettingsStore.Changed -= RefreshScreenshotHotkeyRegistration;
            _screenshotHotkeyService?.Dispose();
            _screenshotHotkeyService = null;
        }
    }

    private void RefreshScreenshotHotkeyRegistration()
    {
        DispatcherQueue? dispatcherQueue = UiDispatcherQueue;
        if (dispatcherQueue is null)
        {
            return;
        }

        dispatcherQueue.TryEnqueue(() => _screenshotHotkeyService?.RefreshRegistration());
    }

    private void ShutdownScreenshotServices()
    {
        ScreenshotSettingsStore.Changed -= RefreshScreenshotHotkeyRegistration;
        _screenshotHotkeyService?.Dispose();
        _screenshotHotkeyService = null;
        foreach (ScreenshotPinWindow pin in _screenshotPins.ToArray())
        {
            try
            {
                pin.Close();
            }
            catch (Exception ex)
            {
                Log($"[Screenshot] Pin close during shutdown failed: {ex}");
            }
        }

        _screenshotPins.Clear();
    }
}
