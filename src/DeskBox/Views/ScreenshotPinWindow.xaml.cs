using DeskBox.Helpers;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Streams;
using Windows.System;
using Windows.UI.Core;
using WinRT.Interop;

namespace DeskBox.Views;

/// <summary>
/// Snipaste F3-style pinned screenshot: a borderless, topmost window whose
/// entire surface is one already-captured frame. The image is the window, so
/// at 100% one frame pixel is one screen pixel; zoom keeps the aspect ratio and
/// window alpha is applied to the layered HWND, which is what lets the desktop
/// show through instead of a dimmed copy of the content.
/// <para>
/// Interaction is pointer-first and keyboard-assisted, mirroring the source
/// tool: drag to move, wheel to zoom around the cursor, Ctrl+wheel for
/// transparency, 1/2 rotate a quarter turn, 3/4 mirror, double-click to get the
/// pin out of the way, Esc or the context menu to close. The window is created
/// hidden by the constructor and only appears once
/// <see cref="InitializeAsync"/> has a frame to show.
/// </para>
/// </summary>
public sealed partial class ScreenshotPinWindow : Window
{
    private const double MinZoom = 0.1;
    private const double MaxZoom = 8.0;
    // One wheel notch. Chosen so 0.1x <-> 8x stays a comfortable number of
    // notches rather than a scroll marathon.
    private const double ZoomStep = 1.1;
    private const double MinOpacity = 0.2;
    private const double MaxOpacity = 1.0;
    private const double OpacityStep = 0.05;
    // Double-click "hide": alpha instead of Hide(), because a hidden window can
    // never receive the double-click that is supposed to bring it back.
    private const double HiddenOpacity = 0.02;
    private const byte HiddenAlpha = 5;

    // Menu copy comes from the shared resources (Common.Copy, Common.Close and
    // the screenshot-specific SaveAs), so the flyout needs no local literals.

    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;

    private CapturedFrame _frame;
    private bool _initialized;
    private bool _closed;
    private bool _busy;
    private double _dpiScale = 1.0;
    private double _zoom = 1.0;
    private double _opacity = MaxOpacity;
    private int _quarterTurns;
    private bool _mirrorHorizontal;
    private bool _mirrorVertical;
    private bool _hidden;
    private bool _layeredApplied;
    private string _closeReason = "system";

    // Drag state in physical screen pixels: the cursor is read from Win32 and
    // never converted, so a drag stays exact across monitors with different DPI.
    private bool _dragging;
    private Win32Helper.POINT _dragStartCursor;
    private int _dragStartLeft;
    private int _dragStartTop;

    public ScreenshotPinWindow()
    {
        InitializeComponent();

        _hwnd = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        // Both helpers degrade with a log line instead of failing the window,
        // so a pin that still appears in the switcher is an acceptable state.
        WindowShellState.TryApplyBorderlessOverlappedPresenter(_appWindow);
        WindowShellState.TryHideFromSwitchers(_appWindow);

        Closed += ScreenshotPinWindow_Closed;
        ApplyMenuText();
        // Layered while the window is still hidden, which is the order the other
        // layered windows here use; 255 is simply "opaque", and Ctrl+wheel only
        // ever changes the alpha afterwards.
        ApplyWindowAlpha(255);
    }

    /// <summary>
    /// Raised after the window has closed, whatever closed it (Esc, the context
    /// menu, or the shell). Owners drop their reference here.
    /// </summary>
    public event EventHandler? PinClosed;

    /// <summary>
    /// Pins <paramref name="frame"/> at the given physical screen position and
    /// shows the window. Must be called on the UI thread.
    /// <para>
    /// Internal rather than public because <c>CapturedFrame</c> is an internal
    /// type: a public member cannot expose a less accessible parameter type
    /// (CS0051). Every caller lives in this assembly (or in DeskBox.Tests,
    /// which the assembly already exposes its internals to).
    /// </para>
    /// </summary>
    /// <param name="frame">The captured BGRA frame to pin.</param>
    /// <param name="screenLeftPx">Physical left edge of the window.</param>
    /// <param name="screenTopPx">Physical top edge of the window.</param>
    /// <param name="dpiScale">DPI scale of the monitor the frame came from.</param>
    internal async Task InitializeAsync(
        CapturedFrame frame,
        int screenLeftPx,
        int screenTopPx,
        double dpiScale)
    {
        if (_initialized || _closed)
        {
            return;
        }

        try
        {
            if (frame.Width <= 0 || frame.Height <= 0 || frame.Pixels.Length == 0)
            {
                App.Log($"[Pin] Open rejected empty frame {frame.Width}x{frame.Height}");
                CloseQuietly("empty");
                return;
            }

            _initialized = true;
            _frame = frame;
            // Only a seed: the real scale is re-probed from the monitor the
            // window actually lands on, right after it has been positioned.
            _dpiScale = IsUsableScale(dpiScale) ? dpiScale : 1.0;

            await LoadImageAsync(frame);
            if (_closed)
            {
                return;
            }

            // A pin with no picture would be an invisible window with no way to
            // close it, so an undecodable frame aborts the pin instead.
            if (PinImage.Source is null)
            {
                App.Log("[Pin] Open aborted: no decodable image.");
                CloseQuietly("nodecode");
                return;
            }

            SizeInt32 size = ComputeWindowSizePx();
            MoveAndResize(screenLeftPx, screenTopPx, size.Width, size.Height);
            RefreshDpiScale();
            ApplyContentSize();

            _appWindow.Show();
            // Focus is what makes the Esc / 1-4 accelerators fire at all; a
            // pin that never activates would look correct and swallow keys.
            Activate();

            App.Log($"[Pin] Open size={frame.Width}x{frame.Height} at=({screenLeftPx},{screenTopPx})");
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Open failed: {ex}");
            CloseQuietly("failed");
        }
    }

    private async Task LoadImageAsync(CapturedFrame frame)
    {
        try
        {
            // Owned by this block, released when it exits: SetSourceAsync is
            // awaited, so the decode has finished reading the stream by then.
            // TryEncodePngToStreamAsync never disposes the stream it is given —
            // an adapter disposed in there used to kill the caller's stream.
            using var stream = new InMemoryRandomAccessStream();
            if (!await ScreenCaptureService.TryEncodePngToStreamAsync(frame, stream))
            {
                App.Log("[Pin] Image encode failed.");
                return;
            }

            stream.Seek(0);
            var image = new BitmapImage();
            await image.SetSourceAsync(stream);
            PinImage.Source = image;
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Image load failed: {ex}");
        }
    }

    private void ApplyMenuText()
    {
        // The flyout and its items are built by InitializeComponent, so these
        // are live here; the null-conditionals only guard a future move of the
        // menu into a template, where the fields would be assigned later.
        LocalizationService localization = App.Current.LocalizationService;
        CopyMenuItem?.Text = localization.T("Common.Copy");
        SaveAsMenuItem?.Text = localization.T("Screenshot.SaveAs");
        CloseMenuItem?.Text = localization.T("Common.Close");
    }

    // ────────────────────────────────────────────────────────────────
    //  Geometry
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The window box in physical pixels: the frame's pixel size scaled by the
    /// zoom, with its sides swapped on an odd quarter turn. Physical pixels are
    /// used on purpose — a pin is "one frame pixel is one screen pixel" at
    /// 100%, which is DPI independent, so only the XAML content size needs the
    /// DPI scale.
    /// </summary>
    private SizeInt32 ComputeWindowSizePx()
    {
        bool swapped = _quarterTurns % 2 != 0;
        double pixelWidth = swapped ? _frame.Height : _frame.Width;
        double pixelHeight = swapped ? _frame.Width : _frame.Height;
        return new SizeInt32(
            Math.Max(1, (int)Math.Round(pixelWidth * _zoom)),
            Math.Max(1, (int)Math.Round(pixelHeight * _zoom)));
    }

    /// <summary>
    /// Sizes the image in DIP so that, after the rotate/mirror transform, it
    /// exactly fills the window box computed by
    /// <see cref="ComputeWindowSizePx"/>.
    /// </summary>
    private void ApplyContentSize()
    {
        double scale = IsUsableScale(_dpiScale) ? _dpiScale : 1.0;
        PinImage.Width = Math.Max(1, _frame.Width * _zoom / scale);
        PinImage.Height = Math.Max(1, _frame.Height * _zoom / scale);
        PinRotateTransform.Angle = 90 * _quarterTurns;
        PinMirrorTransform.ScaleX = _mirrorHorizontal ? -1 : 1;
        PinMirrorTransform.ScaleY = _mirrorVertical ? -1 : 1;
    }

    private void MoveAndResize(int leftPx, int topPx, int widthPx, int heightPx)
    {
        try
        {
            // SWP_NOACTIVATE: repositioning must never hand focus to (or take it
            // from) the pin, otherwise the keyboard shortcuts stop working
            // mid-gesture.
            Win32Helper.SetWindowPos(
                _hwnd,
                Win32Helper.HWND_TOPMOST,
                leftPx,
                topPx,
                Math.Max(1, widthPx),
                Math.Max(1, heightPx),
                Win32Helper.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] SetWindowPos failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Current window box in physical pixels, with the AppWindow size as the
    /// fallback when the window rect is momentarily unavailable.
    /// </summary>
    private Win32Helper.RECT CurrentWindowRectPx()
    {
        if (Win32Helper.GetWindowRect(_hwnd, out Win32Helper.RECT rect) &&
            rect.Right > rect.Left && rect.Bottom > rect.Top)
        {
            return rect;
        }

        SizeInt32 size = _appWindow.Size;
        return new Win32Helper.RECT
        {
            Left = 0,
            Top = 0,
            Right = Math.Max(1, size.Width),
            Bottom = Math.Max(1, size.Height),
        };
    }

    /// <summary>
    /// Re-reads the DPI scale of the monitor the window currently sits on. The
    /// pin survives being dragged across monitors, so a scale captured once at
    /// open time would silently stretch the image after such a move.
    /// </summary>
    private void RefreshDpiScale()
    {
        try
        {
            Win32Helper.RECT rect = CurrentWindowRectPx();
            double scale = Win32Helper.GetDpiScaleForPoint(
                rect.Left + Math.Max(1, rect.Right - rect.Left) / 2,
                rect.Top + Math.Max(1, rect.Bottom - rect.Top) / 2);
            if (IsUsableScale(scale))
            {
                _dpiScale = scale;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] DPI probe failed: {ex.Message}");
        }
    }

    private static bool IsUsableScale(double scale) =>
        scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale);

    // ────────────────────────────────────────────────────────────────
    //  Zoom, rotation, mirroring, opacity
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Scales the pin by <paramref name="factor"/> keeping the point under the
    /// cursor fixed, which is what makes wheel zoom feel like it acts on the
    /// pixel you are pointing at.
    /// </summary>
    private void ZoomAtCursor(double factor)
    {
        if (_closed)
        {
            return;
        }

        double nextZoom = Math.Clamp(_zoom * factor, MinZoom, MaxZoom);
        if (Math.Abs(nextZoom - _zoom) < 0.001)
        {
            return;
        }

        Win32Helper.RECT current = CurrentWindowRectPx();
        int widthPx = Math.Max(1, current.Right - current.Left);
        int heightPx = Math.Max(1, current.Bottom - current.Top);

        // Anchor as a fraction of the window, so the maths stays valid whatever
        // the new size turns out to be.
        double anchorX = 0.5;
        double anchorY = 0.5;
        Win32Helper.POINT cursor = default;
        bool hasCursor = Win32Helper.GetCursorPos(out cursor);
        if (hasCursor)
        {
            anchorX = Math.Clamp((cursor.X - current.Left) / (double)widthPx, 0, 1);
            anchorY = Math.Clamp((cursor.Y - current.Top) / (double)heightPx, 0, 1);
        }

        _zoom = nextZoom;
        RefreshDpiScale();
        ApplyContentSize();
        SizeInt32 size = ComputeWindowSizePx();
        int left = hasCursor
            ? (int)Math.Round(cursor.X - anchorX * size.Width)
            : current.Left;
        int top = hasCursor
            ? (int)Math.Round(cursor.Y - anchorY * size.Height)
            : current.Top;
        MoveAndResize(left, top, size.Width, size.Height);
        App.Log($"[Pin] Zoom scale={_zoom:0.###}");
    }

    private void RotateQuarterTurn(int delta)
    {
        if (_closed)
        {
            return;
        }

        _quarterTurns = ((_quarterTurns + delta) % 4 + 4) % 4;
        ApplyTransformKeepingCenter();
        App.Log($"[Pin] Rotate turns={_quarterTurns}");
    }

    private void ToggleHorizontalMirror()
    {
        if (_closed)
        {
            return;
        }

        _mirrorHorizontal = !_mirrorHorizontal;
        ApplyTransformKeepingCenter();
        App.Log($"[Pin] Mirror horizontal={_mirrorHorizontal}");
    }

    private void ToggleVerticalMirror()
    {
        if (_closed)
        {
            return;
        }

        _mirrorVertical = !_mirrorVertical;
        ApplyTransformKeepingCenter();
        App.Log($"[Pin] Mirror vertical={_mirrorVertical}");
    }

    /// <summary>
    /// Applies the rotate/mirror state and, when a quarter turn swaps the
    /// window's sides, re-centres the box on the old centre so the pin appears
    /// to turn in place instead of jumping.
    /// </summary>
    private void ApplyTransformKeepingCenter()
    {
        Win32Helper.RECT current = CurrentWindowRectPx();
        double centerX = (current.Left + current.Right) / 2.0;
        double centerY = (current.Top + current.Bottom) / 2.0;

        RefreshDpiScale();
        ApplyContentSize();
        SizeInt32 size = ComputeWindowSizePx();
        MoveAndResize(
            (int)Math.Round(centerX - size.Width / 2.0),
            (int)Math.Round(centerY - size.Height / 2.0),
            size.Width,
            size.Height);
    }

    private void ApplyOpacity(double opacity)
    {
        if (_closed)
        {
            return;
        }

        double clamped = Math.Clamp(opacity, MinOpacity, MaxOpacity);
        if (Math.Abs(clamped - _opacity) < 0.001)
        {
            return;
        }

        _opacity = clamped;
        _hidden = false;
        ApplyWindowAlpha((byte)Math.Round(_opacity * 255));
        App.Log($"[Pin] Opacity value={_opacity:0.##}");
    }

    /// <summary>
    /// Whole-window alpha through the layered HWND, the same mechanism the
    /// detach preview and the display overlay use. The XAML content opacity is
    /// only a fallback: it dims the image towards the window's own background
    /// instead of letting the desktop through, so it is the degraded result.
    /// </summary>
    private void ApplyWindowAlpha(byte alpha)
    {
        try
        {
            if (!_layeredApplied)
            {
                int extendedStyle = Win32Helper.GetWindowLong(_hwnd, Win32Helper.GWL_EXSTYLE);
                if ((extendedStyle & Win32Helper.WS_EX_LAYERED) == 0)
                {
                    _ = Win32Helper.SetWindowLongPtr(
                        _hwnd,
                        Win32Helper.GWL_EXSTYLE,
                        new IntPtr(extendedStyle | Win32Helper.WS_EX_LAYERED));
                }

                _layeredApplied = true;
            }

            if (!Win32Helper.SetLayeredWindowAttributes(
                    _hwnd,
                    0,
                    alpha,
                    Win32Helper.LWA_ALPHA))
            {
                RootGrid.Opacity = alpha / 255.0;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Alpha apply failed: {ex.Message}");
            try
            {
                RootGrid.Opacity = alpha / 255.0;
            }
            catch
            {
                // Nothing left to degrade to.
            }
        }
    }

    private void ToggleHidden()
    {
        if (_closed)
        {
            return;
        }

        _hidden = !_hidden;
        ApplyWindowAlpha(_hidden ? HiddenAlpha : (byte)Math.Round(_opacity * 255));
        App.Log($"[Pin] Hidden value={_hidden}");
    }

    private static bool IsControlDown() =>
        InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)
            .HasFlag(CoreVirtualKeyStates.Down);

    // ────────────────────────────────────────────────────────────────
    //  Pointer and keyboard input
    // ────────────────────────────────────────────────────────────────

    private void RootGrid_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_closed)
        {
            return;
        }

        try
        {
            var point = e.GetCurrentPoint(RootGrid);
            if (!point.Properties.IsLeftButtonPressed)
            {
                // Right button: the context flyout owns this gesture.
                return;
            }

            if (!Win32Helper.GetCursorPos(out Win32Helper.POINT cursor))
            {
                return;
            }

            Win32Helper.RECT current = CurrentWindowRectPx();
            _dragStartCursor = cursor;
            _dragStartLeft = current.Left;
            _dragStartTop = current.Top;
            _dragging = RootGrid.CapturePointer(e.Pointer);
            // A click is how the user gives the pin focus, and focus is what the
            // accelerators need.
            Activate();
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Pointer press failed: {ex.Message}");
        }
    }

    private void RootGrid_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (!_dragging || _closed)
        {
            return;
        }

        try
        {
            if (!Win32Helper.GetCursorPos(out Win32Helper.POINT cursor))
            {
                return;
            }

            Win32Helper.SetWindowPos(
                _hwnd,
                Win32Helper.HWND_TOPMOST,
                _dragStartLeft + (cursor.X - _dragStartCursor.X),
                _dragStartTop + (cursor.Y - _dragStartCursor.Y),
                0,
                0,
                Win32Helper.SWP_NOSIZE | Win32Helper.SWP_NOACTIVATE);
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Drag failed: {ex.Message}");
        }
    }

    private void RootGrid_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        EndDrag();
    }

    private void RootGrid_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        // The drag may have ended on a monitor with a different scale.
        RefreshDpiScale();
    }

    private void EndDrag()
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        try
        {
            RootGrid.ReleasePointerCaptures();
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Pointer release failed: {ex.Message}");
        }

        RefreshDpiScale();
        ApplyContentSize();
    }

    private void RootGrid_PointerWheelChanged(object sender, PointerRoutedEventArgs e)
    {
        if (_closed || !_initialized)
        {
            return;
        }

        try
        {
            var point = e.GetCurrentPoint(RootGrid);
            int delta = point.Properties.MouseWheelDelta;
            if (delta == 0)
            {
                return;
            }

            e.Handled = true;
            int notches = delta > 0 ? 1 : -1;
            if (IsControlDown())
            {
                ApplyOpacity(_opacity + notches * OpacityStep);
            }
            else
            {
                ZoomAtCursor(Math.Pow(ZoomStep, notches));
            }
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Wheel failed: {ex.Message}");
        }
    }

    private void RootGrid_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        e.Handled = true;
        ToggleHidden();
    }

    private void EscapeAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        CloseQuietly("esc");
    }

    private void RotateClockwiseAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RotateQuarterTurn(1);
    }

    private void RotateCounterClockwiseAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        RotateQuarterTurn(-1);
    }

    private void FlipHorizontalAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleHorizontalMirror();
    }

    private void FlipVerticalAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        ToggleVerticalMirror();
    }

    private void CopyAccelerator_Invoked(
        KeyboardAccelerator sender,
        KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_busy || _closed || !_initialized)
        {
            return;
        }

        args.Handled = true;
        _ = CopyToClipboardAsync();
    }

    // ────────────────────────────────────────────────────────────────
    //  Context menu actions
    // ────────────────────────────────────────────────────────────────

    private void CopyMenuItem_Click(object sender, RoutedEventArgs e) =>
        _ = CopyToClipboardAsync();

    private void SaveAsMenuItem_Click(object sender, RoutedEventArgs e) =>
        _ = SaveAsAsync();

    private void CloseMenuItem_Click(object sender, RoutedEventArgs e) =>
        CloseQuietly("menu");

    private async Task CopyToClipboardAsync()
    {
        if (_busy || _closed || !_initialized)
        {
            return;
        }

        _busy = true;
        try
        {
            // Intentionally not disposed: the DataPackage carries a
            // RandomAccessStreamReference, not a copy, so the OS reads this
            // stream when it materializes the bitmap (Clipboard.Flush is what
            // forces that). Disposing here would hand the paste target a dead
            // stream; the COM reference held by the DataPackage keeps the
            // native stream alive.
            var stream = new InMemoryRandomAccessStream();
            if (!await ScreenCaptureService.TryEncodePngToStreamAsync(_frame, stream))
            {
                App.Log("[Pin] Copy failed: PNG encode returned false.");
                return;
            }

            stream.Seek(0);
            var dataPackage = new DataPackage();
            dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
            DeskBoxClipboardWriteScope.MarkWrite(hasImage: true);
            App.Log($"[Pin] Copied size={_frame.Width}x{_frame.Height}");
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Copy failed: {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    private async Task SaveAsAsync()
    {
        if (_busy || _closed || !_initialized)
        {
            return;
        }

        _busy = true;
        try
        {
            string? destinationPath = await FileSavePickerService.PickSaveFileAsync(
                _hwnd,
                $"DeskBox-Pin-{DateTime.Now:yyyyMMdd-HHmmss}",
                [".png"]);
            if (destinationPath is null)
            {
                return;
            }

            if (!await ScreenCaptureService.TryEncodePngAsync(_frame, destinationPath))
            {
                App.Log($"[Pin] Save failed for '{destinationPath}'.");
                return;
            }

            App.Log($"[Pin] Saved path={destinationPath}");
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Save failed: {ex}");
        }
        finally
        {
            _busy = false;
        }
    }

    // ────────────────────────────────────────────────────────────────
    //  Lifetime
    // ────────────────────────────────────────────────────────────────

    private void CloseQuietly(string reason)
    {
        if (_closed)
        {
            return;
        }

        _closeReason = reason;
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Close failed: {ex.Message}");
        }
    }

    private void ScreenshotPinWindow_Closed(object sender, WindowEventArgs args)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _dragging = false;
        try
        {
            Closed -= ScreenshotPinWindow_Closed;
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Closed unsubscribe failed: {ex.Message}");
        }

        // Drop the pixel buffer and the decoded bitmap: a pinned frame is tens
        // of MB on a HiDPI screen, and the window object itself may outlive the
        // close if an owner still holds it while PinClosed runs.
        try
        {
            PinImage.Source = null;
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] Image release failed: {ex.Message}");
        }

        _frame = default;
        App.Log($"[Pin] Close reason={_closeReason}");

        try
        {
            PinClosed?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.Log($"[Pin] PinClosed handler failed: {ex}");
        }
    }
}
