using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using DeskBox.Controls;
using DeskBox.Helpers;
using DeskBox.Platform;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using WinRT.Interop;

namespace DeskBox.Views;

/// <summary>
/// iTop-style capture overlay (ported from the bbkien fork, commit 414c7719):
/// a frozen monitor snapshot is taken before this window appears, so the
/// user sees a still screen while picking a window, dragging a free region, or
/// annotating what was picked. Desktop/taskbar clicks select the whole monitor.
/// Every System.Drawing usage of the source is replaced by the GDI/WIC pipeline
/// in <see cref="ScreenCaptureService"/>; nothing here touches the disk outside
/// the save picker destination.
/// <para>
/// Everything this window draws on top of the frozen frame (loupe, pixel
/// readout, thirds guides, selection grips) is chrome: the exported
/// image is always cropped from the frozen frame or read with PrintWindow, never
/// from a screen capture taken while this window is visible. Annotations are
/// composited from the annotation model, not scraped off the screen. The one
/// re-capture (delayed capture) hides this window first.
/// </para>
/// </summary>
public sealed partial class ScreenshotCaptureWindow : Window
{
    private const double DragActivationThreshold = 4;

    // Pointer chrome geometry, in DIP. Inside the loupe one frozen-frame pixel
    // becomes LoupeZoom DIP; a wider sample gives context around the crosshair.
    private const double LoupePanelSize = 158;
    private const double LoupeZoom = 8;
    private const double LoupeCursorOffset = 24;
    private const uint SwpHideWindow = 0x0080;
    // Action bar placement: while a selection is live the bar follows the
    // selection instead of sitting in the far corner, the way a region
    // capture tool is expected to behave.
    private const double ActionBarGap = 8;
    private const double ActionBarInset = 12;
    // Resize grips: drawn at HandleSizeDip, hit-tested at the larger radius so
    // a grip that straddles the monitor edge is still grabbable.
    private const double HandleSizeDip = 11;
    private const double HandleHitRadiusDip = 8;
    // Band around the selection outline that moves the whole region when
    // dragged; wide enough to hit with a mouse, narrow enough to leave the
    // interior free for drawing.
    private const double SelectionEdgeBandDip = 8;
    private const int MinimumSelectionPixels = 8;
    // Corner brackets: arm length and bar thickness, in DIP.
    private const double CornerBracketLengthDip = 16;
    private const double CornerBracketThicknessDip = 2;

    private readonly AppWindow _appWindow;
    private readonly IntPtr _hwnd;
    private readonly Win32Helper.RECT _monitorRect;
    private readonly double _dpiScale;
    private CapturedFrame? _frozenFrame;
    private readonly LocalizationService _localization = App.Current.LocalizationService;

    private IntPtr _selectedWindow;
    private Win32Helper.RECT _selectedRect;
    private Windows.Foundation.Point _dragOriginDip;
    private Win32Helper.RECT _dragOriginalRect;
    private DragMode _dragMode = DragMode.None;
    private ResizeHandle _resizeHandle = ResizeHandle.None;
    private bool _isDraggingRegion;
    private bool _isManualRegion;
    // A live selection no longer blocks the pointer: it only decides whether the
    // action bar, the grips and the annotation surface are armed. The previous
    // model set a permanent lock on the first click, so every later drag was a
    // silent no-op — the exact complaint that drove this rewrite.
    private bool _hasSelection;
    // The overlay opens with the window under the cursor outlined but not yet
    // accepted; a click inside the outline turns it into a real selection.
    private bool _preSelectMode;
    private ScreenshotAnnotationTool _activeTool = ScreenshotAnnotationTool.None;
    private bool _colorPickMode;
    private bool _isCapturing;
    private byte[]? _annotationSourcePixels;
    private Win32Helper.RECT _annotationSourceRect;
    private bool _isClosed;
    // Forensics only: the overlay's happy path leaves no other trace, so a
    // "screen went black" report needs the exit reason beside the open line.
    private string _closeReason = "system";

    private readonly List<Rectangle> _handles = [];
    private readonly List<Rectangle> _brackets = [];
    private readonly List<Button> _toolButtons = [];
    private readonly SolidColorBrush _toolIdleBrush = new(Windows.UI.Color.FromArgb(0x00, 0xFF, 0xFF, 0xFF));
    private readonly SolidColorBrush _toolActiveBrush = new(Windows.UI.Color.FromArgb(0xFF, 0xED, 0xED, 0xED));

    // The loupe log is rate limited: the overlay follows every pointer move, so
    // an unthrottled line would bury the rest of the capture session.
    private int _loupeLogs;

    // The loupe samples the frozen frame in software, so the magnified picture
    // is exact at any DPI and never depends on a render transform.
    private WriteableBitmap? _loupeBitmap;
    private byte[]? _loupePixels;
    private int _lastLoupeX = int.MinValue;
    private int _lastLoupeY = int.MinValue;
    private Windows.Foundation.Point? _pendingPointerOverlay;
    private bool _pointerOverlayQueued;
    private readonly SolidColorBrush _pixelSwatchBrush = new(Colors.White);
    private IReadOnlyList<ScreenshotSelectionTarget> _selectionTargets = [];
    private long _colorCopiedUntil;

    public ScreenshotCaptureWindow()
    {
        InitializeComponent();
        Closed += ScreenshotCaptureWindow_Closed;
        // Activation is the prime suspect for the drag dying mid-gesture: a
        // capture taken while the overlay is still not the foreground window is
        // cancelled the moment activation lands. Log both edges so the pairing
        // is visible against [Screenshot] Pointer capture lost. WinUI reports
        // deactivation through the same event, not a separate one.
        Activated += (_, args) => App.Log(
            $"[Screenshot] Window {(args.WindowActivationState == WindowActivationState.Deactivated ? "deactivated" : "activated")} " +
            $"state={args.WindowActivationState} foregroundMatches={Win32Helper.GetForegroundWindow() == _hwnd} " +
            $"dragMode={_dragMode}");

        _hwnd = WindowNative.GetWindowHandle(this);
        if (!Win32Helper.GetCursorPos(out Win32Helper.POINT cursor) ||
            !Win32Helper.TryGetMonitorWorkArea(cursor.X, cursor.Y, out Win32Helper.RECT monitor, out _))
        {
            monitor = new Win32Helper.RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }

        _monitorRect = monitor;
        // Resolved once: the physical-pixel readout and the loupe are sized
        // against it, and it must not be re-probed per pointer move.
        _dpiScale = Win32Helper.GetDpiScaleForPoint(
            monitor.Left + Math.Max(1, monitor.Right - monitor.Left) / 2,
            monitor.Top + Math.Max(1, monitor.Bottom - monitor.Top) / 2);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(_hwnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        WindowShellState.TryApplyBorderlessOverlappedPresenter(_appWindow);
        WindowShellState.TryHideFromSwitchers(_appWindow);
        _appWindow.MoveAndResize(new RectInt32(
            monitor.Left,
            monitor.Top,
            Math.Max(1, monitor.Right - monitor.Left),
            Math.Max(1, monitor.Bottom - monitor.Top)));

        CreateSelectionHandles();
        CreateCornerBrackets();
        CollectToolButtons();
        AnnotationLayer.AnnotationsChanged += AnnotationLayer_AnnotationsChanged;
        ApplyLocalizedText();
        InitializeCaptureShortcuts();
        UpdateToolButtonVisuals();
    }

    private void CreateSelectionHandles()
    {
        // Index i maps to ResizeHandle i+1, so hit testing can address a grip by
        // its enum value without a second lookup table.
        for (int index = 0; index < 8; index++)
        {
            var handle = new Rectangle
            {
                Width = HandleSizeDip,
                Height = HandleSizeDip,
                Fill = new SolidColorBrush(Colors.White),
                Stroke = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x29, 0xB6, 0xF6)),
                StrokeThickness = 1,
                // The grips sit above the annotation surface so a region stays
                // adjustable while a tool is armed; each grip routes its press
                // into the shared drag state machine below.
                Tag = index,
                IsHitTestVisible = true,
            };
            handle.PointerPressed += Handle_PointerPressed;
            _handles.Add(handle);
            HandleCanvas.Children.Add(handle);
        }
    }

    /// <summary>
    /// Creates the eight bars that make up the four corner brackets. They are
    /// built once and repositioned by <see cref="UpdateCornerBrackets"/>.
    /// </summary>
    private void CreateCornerBrackets()
    {
        for (int index = 0; index < 8; index++)
        {
            var bar = new Rectangle
            {
                Fill = new SolidColorBrush(Colors.White),
                IsHitTestVisible = false,
            };
            _brackets.Add(bar);
            BracketCanvas.Children.Add(bar);
        }
    }

    /// <summary>
    /// Starts a resize from a grip. The pointer is captured on the frozen image
    /// rather than on the grip so the existing move/release handlers drive the
    /// rest of the gesture, which is what keeps resize identical whether it was
    /// begun on the border or on a grip.
    /// </summary>
    private void Handle_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_hasSelection || sender is not Rectangle { Tag: int index } ||
            index < 0 || index >= _handles.Count)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(FrozenScreenImage).Position;
        _dragOriginDip = point;
        _dragOriginalRect = _selectedRect;
        _dragMode = DragMode.Resize;
        _resizeHandle = (ResizeHandle)(index + 1);
        FrozenScreenImage.CapturePointer(e.Pointer);
        e.Handled = true;
        App.LogVerbose($"[Screenshot] Grip down handle={_resizeHandle} x={point.X:F1} y={point.Y:F1}");
    }

    private void CollectToolButtons()
    {
        _toolButtons.Clear();
        _toolButtons.Add(RectangleToolButton);
        _toolButtons.Add(EllipseToolButton);
        _toolButtons.Add(LineToolButton);
        _toolButtons.Add(ArrowToolButton);
        _toolButtons.Add(PencilToolButton);
        _toolButtons.Add(MarkerToolButton);
        _toolButtons.Add(TextToolButton);
        _toolButtons.Add(MosaicToolButton);
        _toolButtons.Add(BlurToolButton);
        _toolButtons.Add(EraserToolButton);
    }

    /// <summary>
    /// Captures the frozen monitor snapshot on a background thread (a
    /// full-screen BitBlt + copy is tens of MB on HiDPI screens) and only
    /// then shows the overlay. Showing after the capture is what keeps the
    /// frozen frame free of this window; the constructor leaves the window
    /// hidden for exactly that reason.
    /// </summary>
    internal async Task InitializeAsync()
    {
        long captureStartedTicks = Environment.TickCount64;
        Win32Helper.RECT monitor = _monitorRect;
        var capture = await Task.Run(() =>
        {
            var targets = CaptureSelectionTargets();
            var pixels = ScreenCaptureService.TryCaptureScreenRegion(
                monitor.Left, monitor.Top,
                Math.Max(1, monitor.Right - monitor.Left),
                Math.Max(1, monitor.Bottom - monitor.Top));
            return (Targets: targets, Frame: pixels);
        });
        _selectionTargets = capture.Targets;
        CapturedFrame? frozenFrame = capture.Frame;
        long captureElapsedMs = Environment.TickCount64 - captureStartedTicks;
        string frameText = "none";
        if (frozenFrame is not { } frame)
        {
            throw new InvalidOperationException("Frozen monitor snapshot failed.");
        }
        else
        {
            _frozenFrame = frame;
            frameText = $"{frame.Width}x{frame.Height}";
            await LoadFrozenImageAsync(frame);
        }

        Win32Helper.SetWindowPos(
            _hwnd,
            Win32Helper.HWND_TOPMOST,
            monitor.Left,
            monitor.Top,
            monitor.Right - monitor.Left,
            monitor.Bottom - monitor.Top,
            Win32Helper.SWP_SHOWWINDOW);

        // Forensics: nothing else records that a capture session ever opened,
        // so a "screen went black" report cannot be triaged without this line.
        int dpiPercent = (int)Math.Round(_dpiScale * 100);
        App.Log(
            $"[Screenshot] Open monitor=({monitor.Left},{monitor.Top},{monitor.Right - monitor.Left},{monitor.Bottom - monitor.Top}) " +
            $"dpi={dpiPercent}% frame={frameText} elapsedMs={captureElapsedMs}");

        // QQ/WeChat behaviour: the overlay opens with the window under the
        // cursor already picked, so double-click or Enter captures it at once
        // while a drag still starts a fresh region. Laid out first so the
        // action bar can measure the viewport before it positions itself.
        RootGrid.UpdateLayout();
        AutoSelectWindowUnderCursor();
        ShowPointerOverlayAtCursor();
    }

    /// <summary>
    /// Draws the loupe once at open time. WinUI only raises pointer events once
    /// the pointer moves, so without this first call the loupe stays hidden
    /// until the user jiggles the mouse, which reads as "the loupe is broken".
    /// </summary>
    private void ShowPointerOverlayAtCursor()
    {
        if (_dpiScale <= 0 ||
            !Win32Helper.GetCursorPos(out Win32Helper.POINT cursor))
        {
            return;
        }

        var point = new Windows.Foundation.Point(
            (cursor.X - _monitorRect.Left) / _dpiScale,
            (cursor.Y - _monitorRect.Top) / _dpiScale);
        UpdatePointerOverlay(point);
    }

    /// <summary>
    /// Outlines the window under the cursor as a pre-selection, the way QQ and
    /// WeChat open their capture overlays. It is deliberately not a selection:
    /// a hairline and no grips, so a click inside accepts it and anything else
    /// starts a fresh region.
    /// </summary>
    private void AutoSelectWindowUnderCursor()
    {
        if (_frozenFrame is null)
        {
            return;
        }

        UpdateSelectionFromCursor();
        if (_selectedWindow == IntPtr.Zero)
        {
            _selectedRect = new Win32Helper.RECT
            {
                Left = _monitorRect.Left,
                Top = _monitorRect.Top,
                Right = _monitorRect.Right,
                Bottom = _monitorRect.Bottom,
            };
        }

        _hasSelection = false;
        _preSelectMode = true;
        _isManualRegion = false;
        ApplySelectionBorderStyle(preSelecting: true);
        ShowSelectionBorder(_selectedRect);
        SelectionHintText.Text = _localization.T("Screenshot.Instruction");
        App.LogVerbose(
            $"[Screenshot] Pre-select rect={_selectedRect.Right - _selectedRect.Left}x" +
            $"{_selectedRect.Bottom - _selectedRect.Top} window={GetCaptureMode()}");
    }

    /// <summary>
    /// A pre-selection is a hint, so it gets a hairline in the accent green; a
    /// real selection gets the full border that says the region is locked.
    /// </summary>
    private void ApplySelectionBorderStyle(bool preSelecting)
    {
        SelectionBorder.BorderThickness = new Thickness(preSelecting ? 1 : 3);
        SelectionBorder.BorderBrush = new SolidColorBrush(preSelecting
            ? Windows.UI.Color.FromArgb(0xFF, 0x1A, 0xAD, 0x19)
            : Windows.UI.Color.FromArgb(0xFF, 0x40, 0x9E, 0xFF));
    }

    /// <summary>
    /// Whether a pointer position falls inside the outlined region. Used to tell
    /// "accept this pre-selection" apart from "draw a new one".
    /// </summary>
    private bool IsInsideSelection(Windows.Foundation.Point point)
    {
        if (_selectedRect.Right <= _selectedRect.Left || _selectedRect.Bottom <= _selectedRect.Top)
        {
            return false;
        }

        int x = ToCaptureX(point.X);
        int y = ToCaptureY(point.Y);
        return x >= _selectedRect.Left && x < _selectedRect.Right &&
            y >= _selectedRect.Top && y < _selectedRect.Bottom;
    }

    /// <summary>
    /// Double click confirms the live region without reselecting beneath it.
    /// </summary>
    private async void FrozenScreenImage_DoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if (_isCapturing || _frozenFrame is null)
        {
            return;
        }

        e.Handled = true;
        if (!_hasSelection)
        {
            UpdateSelectionFromCursor();
            if (_selectedWindow == IntPtr.Zero)
            {
                _selectedRect = new Win32Helper.RECT
                {
                    Left = _monitorRect.Left,
                    Top = _monitorRect.Top,
                    Right = _monitorRect.Right,
                    Bottom = _monitorRect.Bottom,
                };
            }

            _hasSelection = true;
            CommitSelection();
        }

        App.LogVerbose($"[Screenshot] Double tap confirm mode={GetCaptureMode()}");
        await ExportSelectionAsync(copyToClipboard: true);
    }

    /// <summary>
    /// Right click leaves the overlay, matching the way QQ and WeChat screenshots
    /// can be dismissed without hunting for a button.
    /// </summary>
    private void RootGrid_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (_isClosed || _isCapturing)
        {
            return;
        }

        e.Handled = true;
        _closeReason = "right-click";
        App.LogVerbose("[Screenshot] Right click dismissed the overlay");
        Close();
    }

    private void ScreenshotCaptureWindow_Closed(object sender, WindowEventArgs args)
    {
        _isClosed = true;
        CancelPointerOverlayUpdate();
        App.Log($"[Screenshot] Close reason={_closeReason}");
    }

    private async Task LoadFrozenImageAsync(CapturedFrame frame)
    {
        // GDI already supplies BGRA pixels. Avoid a full-screen PNG encode and
        // decode, and finish uploading before showing the overlay.
        var image = new WriteableBitmap(frame.Width, frame.Height);
        using (Stream pixels = image.PixelBuffer.AsStream())
        {
            await pixels.WriteAsync(frame.Pixels);
        }
        image.Invalidate();
        FrozenScreenImage.Source = image;
        _annotationSourcePixels = null;
    }

    private void ApplyLocalizedText()
    {
        Title = _localization.T("Screenshot.Title");
        SelectionHintText.Text = _localization.T("Screenshot.Instruction");
        // The bar itself is glyph-only, so the captions become tooltips: the
        // translations still reach the user without widening the panel to fit
        // the longest locale.
        ToolTipService.SetToolTip(CopyButton, _localization.T("Screenshot.Copy"));
        ToolTipService.SetToolTip(SaveButton, _localization.T("Screenshot.Save"));
        ToolTipService.SetToolTip(ResetButton, _localization.T("Screenshot.ReSelect"));
        ToolTipService.SetToolTip(CancelButton, _localization.T("Screenshot.Cancel"));
        ToolTipService.SetToolTip(PinButton, _localization.T("Screenshot.Pin"));
        CoordLabelText.Text = _localization.T("Screenshot.CoordLabel");
        ColorLabelText.Text = _localization.T("Screenshot.ColorLabel");
        ColorHintText.Text = _localization.T("Screenshot.ColorHint");
    }

    // ---------------------------------------------------------------- pointer

    private void FrozenScreenImage_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        Windows.Foundation.Point point = e.GetCurrentPoint(FrozenScreenImage).Position;
        if (_frozenFrame is null)
        {
            return;
        }

        if (_dragMode != DragMode.None &&
            !e.GetCurrentPoint(FrozenScreenImage).Properties.IsLeftButtonPressed)
        {
            // The button came up while the pointer was still moving. Without
            // capture the Released event may never arrive, so finish here rather
            // than leave the gesture hanging on a stale origin.
            App.LogVerbose($"[Screenshot] Drag finished by move stream dragMode={_dragMode}");
            CompleteDrag(point);
            QueuePointerOverlayUpdate(point);
            return;
        }

        switch (_dragMode)
        {
            case DragMode.Create:
                if (!_isDraggingRegion &&
                    (Math.Abs(point.X - _dragOriginDip.X) >= DragActivationThreshold ||
                     Math.Abs(point.Y - _dragOriginDip.Y) >= DragActivationThreshold))
                {
                    BeginNewRegion();
                }

                if (_isDraggingRegion)
                {
                    UpdateManualRegion(_dragOriginDip, point);
                }

                break;

            case DragMode.Move:
                MoveSelection(point);
                break;

            case DragMode.Resize:
                ResizeSelection(point);
                break;

            default:
                // Idle hover: preview the window under the cursor, but never
                // while a region is live — that would fight the user's pick.
                if (!_hasSelection)
                {
                    UpdateSelectionFromPoint(point);
                }

                break;
        }

        QueuePointerOverlayUpdate(point);
    }

    private void FrozenScreenImage_PointerExited(object sender, PointerRoutedEventArgs e) =>
        HidePointerOverlay();

    private void FrozenScreenImage_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (_isCapturing || _frozenFrame is null || !e.GetCurrentPoint(FrozenScreenImage).Properties.IsLeftButtonPressed)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(FrozenScreenImage).Position;

        if (!_hasSelection && !_colorPickMode)
        {
            UpdateSelectionFromPoint(point);
        }
        _dragOriginDip = point;
        _dragOriginalRect = _selectedRect;
        _resizeHandle = ResizeHandle.None;

        if (_colorPickMode)
        {
            // Colour picking is a click, not a drag: swallow the press so the
            // release handler can copy the sampled hex without a region flash.
            _dragMode = DragMode.None;
        }
        else
        {
            ResizeHandle handle = HitTestHandle(point);
            if (handle != ResizeHandle.None)
            {
                _dragMode = DragMode.Resize;
                _resizeHandle = handle;
            }
            else if (_hasSelection && IsInsideSelection(point))
            {
                // With no drawing tool armed, the entire selected region is a
                // move target. Handles keep priority for resizing.
                _dragMode = DragMode.Move;
            }
            else
            {
                // Deferred: the old region and its annotations survive a stray
                // click and are only replaced once the drag proves itself.
                _dragMode = DragMode.Create;
                _isDraggingRegion = false;
            }
        }

        FrozenScreenImage.CapturePointer(e.Pointer);
        e.Handled = true;
        App.LogVerbose($"[Screenshot] Pointer down mode={_dragMode} x={point.X:F1} y={point.Y:F1}");
    }

    private void FrozenScreenImage_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        if (_frozenFrame is null)
        {
            return;
        }

        Windows.Foundation.Point point = e.GetCurrentPoint(FrozenScreenImage).Position;
        App.LogVerbose($"[Screenshot] Pointer up dragMode={_dragMode} isDragging={_isDraggingRegion}");
        CompleteDrag(point);
        FrozenScreenImage.ReleasePointerCapture(e.Pointer);
        e.Handled = true;
    }

    /// <summary>
    /// Turns a finished gesture into a live selection. Shared by the real
    /// release and by the move-stream fallback, because a stolen pointer
    /// capture can swallow the release entirely.
    /// </summary>
    private void CompleteDrag(Windows.Foundation.Point point)
    {
        if (_colorPickMode)
        {
            CopyColorAtCursor();
            _dragMode = DragMode.None;
            return;
        }

        DragMode completedMode = _dragMode;
        if (completedMode == DragMode.Move)
        {
            MoveSelection(point);
        }
        else if (completedMode == DragMode.Resize)
        {
            ResizeSelection(point);
        }
        else if (completedMode == DragMode.Create && _isDraggingRegion)
        {
            UpdateManualRegion(_dragOriginDip, point);
        }
        // Settle the gesture before updating the annotation source or releasing
        // capture, both of which can synchronously trigger further UI events.
        _dragMode = DragMode.None;
        _resizeHandle = ResizeHandle.None;
        switch (completedMode)
        {
            case DragMode.Create when _isDraggingRegion:
                _isDraggingRegion = false;
                _hasSelection = true;
                _isManualRegion = true;
                _selectedWindow = IntPtr.Zero;
                CommitSelection();
                break;

            case DragMode.Create when _hasSelection:
                // A stray click outside the region must not discard it.
                break;

            case DragMode.Create:
                // A click, not a drag. If the overlay opened with the window
                // under the cursor already outlined, a click inside that
                // outline accepts it as-is — re-resolving would silently swap
                // the suggestion for whatever sits under the click point.
                _isDraggingRegion = false;
                if (_preSelectMode && IsInsideSelection(point))
                {
                    _hasSelection = true;
                    _isManualRegion = false;
                    CommitSelection();
                    break;
                }

                UpdateSelectionFromCursor();
                _hasSelection = true;
                _isManualRegion = false;
                if (_selectedWindow == IntPtr.Zero)
                {
                    _selectedRect = new Win32Helper.RECT
                    {
                        Left = _monitorRect.Left,
                        Top = _monitorRect.Top,
                        Right = _monitorRect.Right,
                        Bottom = _monitorRect.Bottom,
                    };
                }

                CommitSelection();
                break;

            case DragMode.Move when _selectedRect.Equals(_dragOriginalRect):
                break;

            case DragMode.Move:
            case DragMode.Resize:
                // The rect no longer matches a window's bounds, so the pick is
                // a free region from here on and must be cropped, not
                // re-read with PrintWindow.
                _selectedWindow = IntPtr.Zero;
                _isManualRegion = true;
                CommitSelection();
                break;
        }

        UpdatePointerOverlay(point);
    }

    private void FrozenScreenImage_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        // The system can steal capture mid-drag (focus switch, Win combos)
        // without a Released ever firing; abandon the in-progress drag so a
        // stale origin cannot turn the next hover into a phantom region.
        if (_dragMode == DragMode.None)
        {
            return;
        }

        bool wasDragging = _isDraggingRegion;
        DragMode lostMode = _dragMode;
        bool foregroundMatches = Win32Helper.GetForegroundWindow() == _hwnd;

        if (wasDragging)
        {
            // Keep the gesture alive. The frozen image covers the whole screen,
            // so moves keep arriving without capture and the button-up is still
            // observable through them (see the fallback in PointerMoved).
            // Aborting here is exactly what made a real drag look like "nothing
            // happens", so capture loss must never eat a live region drag.
            App.Log(
                $"[Screenshot] Pointer capture lost mid-drag; tracking without capture " +
                $"dragMode={lostMode} foregroundMatches={foregroundMatches}");
            return;
        }

        _dragMode = DragMode.None;
        _resizeHandle = ResizeHandle.None;
        _isDraggingRegion = false;
        if (!_hasSelection)
        {
            _isManualRegion = false;
            _selectedRect = default;
            SelectionBorder.Visibility = Visibility.Collapsed;
            HandleCanvas.Visibility = Visibility.Collapsed;
            SelectionHintText.Text = _localization.T("Screenshot.Instruction");
            UpdateDimVeil();
        }

        App.Log(
            $"[Screenshot] Pointer capture lost before release wasDragging={wasDragging} hadSelection={_hasSelection} " +
            $"dragMode={lostMode} captures={FrozenScreenImage.PointerCaptures.Count} " +
            $"foregroundMatches={foregroundMatches}");
    }

    private void BeginNewRegion()
    {
        _isDraggingRegion = true;
        _isManualRegion = true;
        _selectedWindow = IntPtr.Zero;
        // A new region invalidates the old one, and with it every annotation
        // anchored to it. It also retires the opening pre-selection: the user
        // is drawing a region, not accepting the suggestion.
        _hasSelection = false;
        _preSelectMode = false;
        _activeTool = ScreenshotAnnotationTool.None;
        _colorPickMode = false;
        AnnotationLayer.ActiveTool = ScreenshotAnnotationTool.None;
        AnnotationLayer.Clear();
        _annotationSourcePixels = null;
        AnnotationLayer.Visibility = Visibility.Collapsed;
        ToolRow.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        UpdateToolButtonVisuals();
        App.LogVerbose($"[Screenshot] Drag activated x={_dragOriginDip.X:F1} y={_dragOriginDip.Y:F1}");
    }

    // -------------------------------------------------------------- selection

    private void CommitSelection()
    {
        _preSelectMode = false;
        ApplySelectionBorderStyle(preSelecting: false);
        CopyButton.Visibility = Visibility.Visible;
        SaveButton.Visibility = Visibility.Visible;
        PinButton.Visibility = Visibility.Visible;
        ResetButton.Visibility = Visibility.Visible;
        ToolRow.Visibility = Visibility.Visible;
        // The pick is done: the drag-only guides retire, the grips arrive.
        ThirdsCanvas.Visibility = Visibility.Collapsed;
        HidePointerOverlay();
        ShowSelectionBorder(_selectedRect);
        UpdateSelectionHint();
        UpdateAnnotationLayerBounds();
        UpdateToolButtonVisuals();
        PositionActionBarNearSelection();
        App.Log(
            $"[Screenshot] Selection live mode={GetCaptureMode()} " +
            $"rect={_selectedRect.Right - _selectedRect.Left}x{_selectedRect.Bottom - _selectedRect.Top}");
    }

    private void UpdateSelectionHint()
    {
        int width = Math.Max(0, _selectedRect.Right - _selectedRect.Left);
        int height = Math.Max(0, _selectedRect.Bottom - _selectedRect.Top);
        // Bare dimensions, the way QQ and WeChat label a region. The old
        // "selected region:" prefix pushed the bar wider than the tools above it
        // and told the user nothing they could not see.
        SelectionHintText.Text = _localization.Format("Screenshot.SizeFormat", width, height);
    }

    /// <summary>
    /// Parks the action bar next to the live selection: under it when there is
    /// room, above it otherwise, and never outside the screen. A region tool is
    /// used in one glance, so the actions must not sit in the far corner of a
    /// 2560-wide desktop.
    /// </summary>
    private void PositionActionBarNearSelection()
    {
        double viewportWidth = RootGrid.ActualWidth;
        double viewportHeight = RootGrid.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        HintPanel.Measure(new Windows.Foundation.Size(
            double.PositiveInfinity, double.PositiveInfinity));
        double barWidth = HintPanel.DesiredSize.Width;
        double barHeight = HintPanel.DesiredSize.Height;
        if (barWidth <= 0 || barHeight <= 0)
        {
            return;
        }

        double left = ToVisualX(_selectedRect.Left);
        double top = ToVisualY(_selectedRect.Bottom) + ActionBarGap;
        if (top + barHeight > viewportHeight - ActionBarInset)
        {
            top = ToVisualY(_selectedRect.Top) - barHeight - ActionBarGap;
        }

        top = Math.Clamp(top, ActionBarInset, Math.Max(ActionBarInset, viewportHeight - barHeight - ActionBarInset));
        left = Math.Clamp(left, ActionBarInset, Math.Max(ActionBarInset, viewportWidth - barWidth - ActionBarInset));

        HintPanel.HorizontalAlignment = HorizontalAlignment.Left;
        HintPanel.VerticalAlignment = VerticalAlignment.Top;
        HintPanel.Margin = new Thickness(left, top, 0, 0);
    }

    /// <summary>
    /// Restores the action bar to its default corner once there is no selection
    /// to sit next to.
    /// </summary>
    private void ResetActionBarPlacement()
    {
        HintPanel.HorizontalAlignment = HorizontalAlignment.Right;
        HintPanel.VerticalAlignment = VerticalAlignment.Top;
        HintPanel.Margin = new Thickness(0, 24, 24, 0);
    }

    private void ResetButton_Click(object sender, RoutedEventArgs e) => ResetSelectionState();

    private void ResetSelectionState()
    {
        _hasSelection = false;
        _preSelectMode = false;
        _dragMode = DragMode.None;
        _resizeHandle = ResizeHandle.None;
        _isDraggingRegion = false;
        _isManualRegion = false;
        _selectedWindow = IntPtr.Zero;
        _selectedRect = default;
        _activeTool = ScreenshotAnnotationTool.None;
        _colorPickMode = false;
        AnnotationLayer.ActiveTool = ScreenshotAnnotationTool.None;
        AnnotationLayer.Clear();
        _annotationSourcePixels = null;
        AnnotationLayer.Visibility = Visibility.Collapsed;
        CopyButton.Visibility = Visibility.Collapsed;
        SaveButton.Visibility = Visibility.Collapsed;
        PinButton.Visibility = Visibility.Collapsed;
        ResetButton.Visibility = Visibility.Collapsed;
        ToolRow.Visibility = Visibility.Collapsed;
        SelectionHintText.Text = _localization.T("Screenshot.Instruction");
        SelectionBorder.Visibility = Visibility.Collapsed;
        ThirdsCanvas.Visibility = Visibility.Collapsed;
        HandleCanvas.Visibility = Visibility.Collapsed;
        HidePointerOverlay();
        ResetActionBarPlacement();
        UpdateToolButtonVisuals();
        UpdateDimVeil();
    }

    private void MoveSelection(Windows.Foundation.Point point)
    {
        int deltaX = ToCaptureX(point.X) - ToCaptureX(_dragOriginDip.X);
        int deltaY = ToCaptureY(point.Y) - ToCaptureY(_dragOriginDip.Y);
        int width = _dragOriginalRect.Right - _dragOriginalRect.Left;
        int height = _dragOriginalRect.Bottom - _dragOriginalRect.Top;
        int left = Math.Clamp(
            _dragOriginalRect.Left + deltaX,
            _monitorRect.Left,
            Math.Max(_monitorRect.Left, _monitorRect.Right - width));
        int top = Math.Clamp(
            _dragOriginalRect.Top + deltaY,
            _monitorRect.Top,
            Math.Max(_monitorRect.Top, _monitorRect.Bottom - height));
        _selectedRect = new Win32Helper.RECT
        {
            Left = left,
            Top = top,
            Right = left + width,
            Bottom = top + height,
        };
        ShowSelectionBorder(_selectedRect);
        UpdateSelectionHint();
    }

    private void ResizeSelection(Windows.Foundation.Point point)
    {
        int deltaX = ToCaptureX(point.X) - ToCaptureX(_dragOriginDip.X);
        int deltaY = ToCaptureY(point.Y) - ToCaptureY(_dragOriginDip.Y);
        int left = _dragOriginalRect.Left;
        int top = _dragOriginalRect.Top;
        int right = _dragOriginalRect.Right;
        int bottom = _dragOriginalRect.Bottom;

        if (_resizeHandle is ResizeHandle.NorthWest or ResizeHandle.West or ResizeHandle.SouthWest)
        {
            left += deltaX;
        }

        if (_resizeHandle is ResizeHandle.NorthEast or ResizeHandle.East or ResizeHandle.SouthEast)
        {
            right += deltaX;
        }

        if (_resizeHandle is ResizeHandle.NorthWest or ResizeHandle.North or ResizeHandle.NorthEast)
        {
            top += deltaY;
        }

        if (_resizeHandle is ResizeHandle.SouthWest or ResizeHandle.South or ResizeHandle.SouthEast)
        {
            bottom += deltaY;
        }

        left = Math.Clamp(left, _monitorRect.Left, Math.Max(_monitorRect.Left, _monitorRect.Right - MinimumSelectionPixels));
        top = Math.Clamp(top, _monitorRect.Top, Math.Max(_monitorRect.Top, _monitorRect.Bottom - MinimumSelectionPixels));
        right = Math.Clamp(right, left + MinimumSelectionPixels, _monitorRect.Right);
        bottom = Math.Clamp(bottom, top + MinimumSelectionPixels, _monitorRect.Bottom);

        _selectedRect = new Win32Helper.RECT
        {
            Left = left,
            Top = top,
            Right = right,
            Bottom = bottom,
        };
        ShowSelectionBorder(_selectedRect);
        UpdateSelectionHint();
    }

    private ResizeHandle HitTestHandle(Windows.Foundation.Point point)
    {
        if (!_hasSelection)
        {
            return ResizeHandle.None;
        }

        double left = ToVisualX(_selectedRect.Left);
        double top = ToVisualY(_selectedRect.Top);
        double right = ToVisualX(_selectedRect.Right);
        double bottom = ToVisualY(_selectedRect.Bottom);
        double midX = (left + right) / 2;
        double midY = (top + bottom) / 2;
        double radius = HandleHitRadiusDip;

        if (IsNear(point, left, top, radius))
        {
            return ResizeHandle.NorthWest;
        }

        if (IsNear(point, right, top, radius))
        {
            return ResizeHandle.NorthEast;
        }

        if (IsNear(point, left, bottom, radius))
        {
            return ResizeHandle.SouthWest;
        }

        if (IsNear(point, right, bottom, radius))
        {
            return ResizeHandle.SouthEast;
        }

        if (IsNear(point, midX, top, radius))
        {
            return ResizeHandle.North;
        }

        if (IsNear(point, midX, bottom, radius))
        {
            return ResizeHandle.South;
        }

        if (IsNear(point, left, midY, radius))
        {
            return ResizeHandle.West;
        }

        return IsNear(point, right, midY, radius) ? ResizeHandle.East : ResizeHandle.None;
    }

    private static bool IsNear(Windows.Foundation.Point point, double x, double y, double radius) =>
        Math.Abs(point.X - x) <= radius && Math.Abs(point.Y - y) <= radius;

    /// <summary>
    /// True when the point sits inside the selection's edge band. That band is
    /// the handle for moving the whole region; everything further inside is
    /// reserved for drawing a new region or for annotation tools.
    /// </summary>
    private bool IsNearSelectionBorder(Windows.Foundation.Point point)
    {
        double left = ToVisualX(_selectedRect.Left);
        double top = ToVisualY(_selectedRect.Top);
        double right = ToVisualX(_selectedRect.Right);
        double bottom = ToVisualY(_selectedRect.Bottom);
        double band = SelectionEdgeBandDip;
        if (point.X < left - band || point.X > right + band ||
            point.Y < top - band || point.Y > bottom + band)
        {
            return false;
        }

        return Math.Abs(point.X - left) <= band ||
               Math.Abs(point.X - right) <= band ||
               Math.Abs(point.Y - top) <= band ||
               Math.Abs(point.Y - bottom) <= band;
    }

    private void UpdateSelectionFromCursor()
    {
        if (Win32Helper.GetCursorPos(out Win32Helper.POINT cursor))
        {
            UpdateSelectionAt(cursor);
        }
    }

    private void UpdateSelectionFromPoint(Windows.Foundation.Point point) =>
        UpdateSelectionAt(new Win32Helper.POINT { X = ToCaptureX(point.X), Y = ToCaptureY(point.Y) });

    private void UpdateSelectionAt(Win32Helper.POINT cursor)
    {
        IntPtr window = FindWindowAtPoint(cursor, out var bounds);
        if (_selectedWindow == window && _selectedRect.Equals(bounds) &&
            SelectionBorder.Visibility == Visibility.Visible)
        {
            return;
        }

        _selectedWindow = window;
        _selectedRect = bounds;
        _preSelectMode = true;
        ApplySelectionBorderStyle(preSelecting: true);
        SelectionHintText.Text = window == IntPtr.Zero
            ? _localization.T("Screenshot.FullscreenHint")
            : GetWindowTitle(window, _localization);
        ShowSelectionBorder(bounds);
    }

    private void UpdateManualRegion(Windows.Foundation.Point start, Windows.Foundation.Point end)
    {
        double left = Math.Min(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        double right = Math.Max(start.X, end.X);
        double bottom = Math.Max(start.Y, end.Y);
        _selectedRect = new Win32Helper.RECT
        {
            Left = ToCaptureX(left),
            Top = ToCaptureY(top),
            Right = ToCaptureX(right),
            Bottom = ToCaptureY(bottom),
        };
        if (_selectedRect.Right <= _selectedRect.Left)
        {
            _selectedRect.Right = Math.Min(_monitorRect.Right, _selectedRect.Left + 1);
        }
        if (_selectedRect.Bottom <= _selectedRect.Top)
        {
            _selectedRect.Bottom = Math.Min(_monitorRect.Bottom, _selectedRect.Top + 1);
        }

        ShowSelectionBorder(_selectedRect);
        SelectionHintText.Text = _localization.Format(
            "Screenshot.RegionHint",
            _selectedRect.Right - _selectedRect.Left,
            _selectedRect.Bottom - _selectedRect.Top);
    }

    private void ShowSelectionBorder(Win32Helper.RECT rect)
    {
        double left = ToVisualX(Math.Max(rect.Left, _monitorRect.Left));
        double top = ToVisualY(Math.Max(rect.Top, _monitorRect.Top));
        double right = ToVisualX(Math.Min(rect.Right, _monitorRect.Right));
        double bottom = ToVisualY(Math.Min(rect.Bottom, _monitorRect.Bottom));
        SelectionBorder.Width = Math.Max(1, right - left);
        SelectionBorder.Height = Math.Max(1, bottom - top);
        SelectionBorder.Margin = new Thickness(left, top, 0, 0);
        SelectionBorder.HorizontalAlignment = HorizontalAlignment.Left;
        SelectionBorder.VerticalAlignment = VerticalAlignment.Top;
        SelectionBorder.Visibility = Visibility.Visible;
        UpdateThirdsGuides();
        UpdateHandles();
        UpdateCornerBrackets(left, top, right, bottom);
        UpdateAnnotationLayerBounds();
        UpdateDimVeil();
    }

    /// <summary>
    /// Draws the eight hairline corner brackets around the region. Two bars per
    /// corner is enough to read as a bracket without the visual weight of a full
    /// second border.
    /// </summary>
    private void UpdateCornerBrackets(double left, double top, double right, double bottom)
    {
        if (_brackets.Count < 8)
        {
            return;
        }

        double arm = CornerBracketLengthDip;
        double thickness = CornerBracketThicknessDip;
        // Bars are placed so their outer edge sits on the selection border.
        Place(_brackets[0], left, top, arm, thickness);
        Place(_brackets[1], left, top, thickness, arm);
        Place(_brackets[2], right - arm, top, arm, thickness);
        Place(_brackets[3], right - thickness, top, thickness, arm);
        Place(_brackets[4], left, bottom - thickness, arm, thickness);
        Place(_brackets[5], left, bottom - arm, thickness, arm);
        Place(_brackets[6], right - arm, bottom - thickness, arm, thickness);
        Place(_brackets[7], right - thickness, bottom - arm, thickness, arm);
        BracketCanvas.Visibility = Visibility.Visible;

        static void Place(Rectangle bar, double x, double y, double width, double height)
        {
            bar.Width = Math.Max(1, width);
            bar.Height = Math.Max(1, height);
            Canvas.SetLeft(bar, x);
            Canvas.SetTop(bar, y);
        }
    }

    /// <summary>
    /// Paints the grey veil over everything but the live selection, so the
    /// picked region keeps its true colours while the rest of the screen reads
    /// as obviously excluded. With no region the veil covers the whole overlay.
    /// </summary>
    private void UpdateDimVeil()
    {
        double viewportWidth = RootGrid.ActualWidth;
        double viewportHeight = RootGrid.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        var geometry = new PathGeometry { FillRule = FillRule.EvenOdd };
        AddVeilRectangle(geometry, 0, 0, viewportWidth, viewportHeight);
        int width = _selectedRect.Right - _selectedRect.Left;
        int height = _selectedRect.Bottom - _selectedRect.Top;
        if (width > 0 && height > 0)
        {
            double left = ToVisualX(_selectedRect.Left);
            double top = ToVisualY(_selectedRect.Top);
            AddVeilRectangle(geometry, left, top, ToVisualX(_selectedRect.Right) - left, ToVisualY(_selectedRect.Bottom) - top);
        }

        DimVeil.Data = geometry;
    }

    private static void AddVeilRectangle(PathGeometry geometry, double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var figure = new PathFigure
        {
            StartPoint = new Windows.Foundation.Point(x, y),
            IsClosed = true,
            IsFilled = true,
        };
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x + width, y) });
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x + width, y + height) });
        figure.Segments.Add(new LineSegment { Point = new Windows.Foundation.Point(x, y + height) });
        geometry.Figures.Add(figure);
    }

    private void UpdateHandles()
    {
        // Grips belong to a finished pick; during a create-drag they would sit
        // under the cursor and read as noise.
        if (!_hasSelection || _isDraggingRegion)
        {
            HandleCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        double left = ToVisualX(_selectedRect.Left);
        double top = ToVisualY(_selectedRect.Top);
        double right = ToVisualX(_selectedRect.Right);
        double bottom = ToVisualY(_selectedRect.Bottom);
        double midX = (left + right) / 2;
        double midY = (top + bottom) / 2;

        PlaceHandle(ResizeHandle.NorthWest, left, top);
        PlaceHandle(ResizeHandle.North, midX, top);
        PlaceHandle(ResizeHandle.NorthEast, right, top);
        PlaceHandle(ResizeHandle.West, left, midY);
        PlaceHandle(ResizeHandle.East, right, midY);
        PlaceHandle(ResizeHandle.SouthWest, left, bottom);
        PlaceHandle(ResizeHandle.South, midX, bottom);
        PlaceHandle(ResizeHandle.SouthEast, right, bottom);
        HandleCanvas.Visibility = Visibility.Visible;
    }

    private void PlaceHandle(ResizeHandle handle, double centerX, double centerY)
    {
        int index = (int)handle - 1;
        if (index < 0 || index >= _handles.Count)
        {
            return;
        }

        Rectangle grip = _handles[index];
        Canvas.SetLeft(grip, Math.Round(centerX - (HandleSizeDip / 2)));
        Canvas.SetTop(grip, Math.Round(centerY - (HandleSizeDip / 2)));
    }

    // ------------------------------------------------------------ annotations

    private void UpdateAnnotationLayerBounds()
    {
        if (!_hasSelection)
        {
            AnnotationLayer.Visibility = Visibility.Collapsed;
            return;
        }

        double left = ToVisualX(_selectedRect.Left);
        double top = ToVisualY(_selectedRect.Top);
        double width = Math.Max(1, ToVisualX(_selectedRect.Right) - left);
        double height = Math.Max(1, ToVisualY(_selectedRect.Bottom) - top);
        AnnotationLayer.SetBounds(left, top, width, height);
        UpdateAnnotationSourceFrame();
        // Armed only while a tool is selected: an unarmed surface must let the
        // pointer reach the frozen image so a new region can still be dragged.
        AnnotationLayer.IsHitTestVisible = _activeTool != ScreenshotAnnotationTool.None;
        AnnotationLayer.Visibility = Visibility.Visible;
    }

    /// <summary>
    /// Hands the annotation surface the pixels of the current selection. Mosaic
    /// and blur sample this buffer, and the surface maps its own DIP space onto
    /// it, so it must be the cropped region rather than the whole monitor. Only
    /// refreshed once the geometry settles: cropping a few megapixels on every
    /// pointer move of a drag would be wasted work, and a mid-drag crop would be
    /// thrown away immediately.
    /// </summary>
    private void UpdateAnnotationSourceFrame()
    {
        if (_dragMode != DragMode.None || _frozenFrame is not { } frozen || !_hasSelection)
        {
            return;
        }

        if (ReferenceEquals(_annotationSourcePixels, frozen.Pixels) &&
            _annotationSourceRect.Equals(_selectedRect))
        {
            return;
        }

        int x = _selectedRect.Left - _monitorRect.Left;
        int y = _selectedRect.Top - _monitorRect.Top;
        int width = _selectedRect.Right - _selectedRect.Left;
        int height = _selectedRect.Bottom - _selectedRect.Top;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        try
        {
            AnnotationLayer.SetSourceFrame(ScreenCaptureService.Crop(frozen, x, y, width, height));
            _annotationSourcePixels = frozen.Pixels;
            _annotationSourceRect = _selectedRect;
        }
        catch (Exception ex)
        {
            App.Log($"[Screenshot] Annotation source frame failed: {ex}");
        }
    }

    private void AnnotationLayer_AnnotationsChanged(object? sender, EventArgs e)
    {
        UndoButton.IsEnabled = AnnotationLayer.CanUndo;
        RedoButton.IsEnabled = AnnotationLayer.CanRedo;
    }

    private void ToolButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string tag &&
            Enum.TryParse(tag, out ScreenshotAnnotationTool tool))
        {
            SetActiveTool(tool);
        }
    }

    private void SetActiveTool(ScreenshotAnnotationTool tool)
    {
        if (_isCapturing || !_hasSelection)
        {
            return;
        }

        _activeTool = _activeTool == tool ? ScreenshotAnnotationTool.None : tool;
        _colorPickMode = false;
        AnnotationLayer.ActiveTool = _activeTool;
        UpdateAnnotationLayerBounds();
        UpdateToolButtonVisuals();
        // The palette only means something while a shape is being drawn, and
        // hiding it otherwise keeps the bar to a single row like QQ and WeChat.
        PaletteRow.Visibility = _activeTool == ScreenshotAnnotationTool.None
            ? Visibility.Collapsed
            : Visibility.Visible;
        // The loupe would sit on top of the drawing surface and hide the very
        // stroke being placed.
        HidePointerOverlay();
        App.LogVerbose($"[Screenshot] Tool={_activeTool}");
    }

    private void UpdateToolButtonVisuals()
    {
        foreach (Button button in _toolButtons)
        {
            bool active = button.Tag is string tag &&
                Enum.TryParse(tag, out ScreenshotAnnotationTool tool) &&
                tool == _activeTool;
            button.Background = active ? _toolActiveBrush : _toolIdleBrush;
        }

        ColorPickButton.Background = _colorPickMode ? _toolActiveBrush : _toolIdleBrush;
        UndoButton.IsEnabled = AnnotationLayer.CanUndo;
        RedoButton.IsEnabled = AnnotationLayer.CanRedo;
    }

    private void ColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.Background is not SolidColorBrush brush)
        {
            return;
        }

        AnnotationLayer.StrokeColor = brush.Color;
        App.LogVerbose(
            $"[Screenshot] Stroke color #{brush.Color.R:X2}{brush.Color.G:X2}{brush.Color.B:X2}");
    }

    private void ThicknessSlider_ValueChanged(object sender, RangeBaseValueChangedEventArgs e)
    {
        if (AnnotationLayer is null)
        {
            return;
        }

        AnnotationLayer.StrokeThickness = e.NewValue;
    }

    private void UndoButton_Click(object sender, RoutedEventArgs e) => AnnotationLayer.Undo();

    private void RedoButton_Click(object sender, RoutedEventArgs e) => AnnotationLayer.Redo();

    private void UndoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isCapturing || IsTextInputFocused() || !AnnotationLayer.CanUndo)
        {
            return;
        }

        args.Handled = true;
        AnnotationLayer.Undo();
    }

    private void RedoAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isCapturing || IsTextInputFocused() || !AnnotationLayer.CanRedo)
        {
            return;
        }

        args.Handled = true;
        AnnotationLayer.Redo();
    }

    // ------------------------------------------------------------ colour pick

    private void ColorPickButton_Click(object sender, RoutedEventArgs e) => ToggleColorPickMode();

    private void ColorPickAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isCapturing || !_hasSelection || IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        ToggleColorPickMode();
    }

    private bool IsTextInputFocused() =>
        Content?.XamlRoot is { } root && FocusManager.GetFocusedElement(root) is TextBox;

    private void ToggleColorPickMode()
    {
        // A live selection is not required: the colour readout and the loupe
        // both work straight off the frozen frame, and demanding a selection
        // first made the shortcut look broken on the pre-selection the overlay
        // opens with.
        if (_frozenFrame is null)
        {
            return;
        }

        _colorPickMode = !_colorPickMode;
        if (_colorPickMode)
        {
            _activeTool = ScreenshotAnnotationTool.None;
            AnnotationLayer.ActiveTool = ScreenshotAnnotationTool.None;
            UpdateAnnotationLayerBounds();
            ColorHintText.Text = _localization.T("Screenshot.ColorHint");
        }
        else
        {
            HidePointerOverlay();
        }

        UpdateToolButtonVisuals();
        App.LogVerbose($"[Screenshot] Color pick mode={_colorPickMode}");
    }

    private string? ReadFrozenPixelHex(int frameX, int frameY)
    {
        if (_frozenFrame is not { } frame || frameX < 0 || frameY < 0 ||
            frameX >= frame.Width || frameY >= frame.Height)
        {
            return null;
        }

        int index = ((frameY * frame.Width) + frameX) * 4;
        if (index < 0 || index + 2 >= frame.Pixels.Length)
        {
            return null;
        }

        // CapturedFrame is BGRA (WIC/GDI 32bpp), so the red byte comes last.
        byte blue = frame.Pixels[index];
        byte green = frame.Pixels[index + 1];
        byte red = frame.Pixels[index + 2];
        return $"#{red:X2}{green:X2}{blue:X2}";
    }

    private void CopyColorAtCursor()
    {
        if (!Win32Helper.GetCursorPos(out Win32Helper.POINT cursor))
        {
            return;
        }

        string? hex = ReadFrozenPixelHex(cursor.X - _monitorRect.Left, cursor.Y - _monitorRect.Top);
        if (hex is null)
        {
            return;
        }

        var dataPackage = new DataPackage();
        dataPackage.SetText(hex);
        try
        {
            DeskBoxClipboardWriteScope.MarkWrite(hasImage: false);
            Clipboard.SetContent(dataPackage);
            Clipboard.Flush();
        }
        catch (Exception ex)
        {
            App.Log($"[Screenshot] Color copy failed: {ex.Message}");
            SelectionHintText.Text = _localization.T("Screenshot.SaveFailed");
            return;
        }
        PointerColorText.Text = hex;
        // Without this the shortcut is silent: the value was already on screen,
        // so a successful copy and a no-op look exactly alike.
        _colorCopiedUntil = Environment.TickCount64 + 1200;
        ColorHintText.Text = _localization.Format("Screenshot.ColorCopied", hex);
        ColorHintText.Visibility = Visibility.Visible;
        PointerInfoPanel.Visibility = Visibility.Visible;
        App.Log($"[Screenshot] Color copied {hex}");
    }

    // ----------------------------------------------------------------- pinning

    private void PinButton_Click(object sender, RoutedEventArgs e) => _ = PinSelectionAsync();

    private void PinAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!_hasSelection || _isCapturing || IsTextInputFocused())
        {
            return;
        }

        args.Handled = true;
        _ = PinSelectionAsync();
    }

    private async Task PinSelectionAsync()
    {
        if (_isCapturing || !_hasSelection || _frozenFrame is null)
        {
            return;
        }

        SetCaptureBusy(true);
        try
        {
            CapturedFrame? pinned = await BuildExportFrameAsync();
            if (pinned is not { } frame)
            {
                return;
            }

            var pin = new ScreenshotPinWindow();
            App.Current.ScreenshotPins.Add(pin);
            pin.PinClosed += (_, _) => App.Current.ScreenshotPins.Remove(pin);
            await pin.InitializeAsync(frame, _selectedRect.Left, _selectedRect.Top, _dpiScale);
            App.Log(
                $"[Screenshot] Pinned mode={GetCaptureMode()} size={frame.Width}x{frame.Height}");
            _closeReason = "pin";
            Close();
        }
        catch (Exception ex)
        {
            App.Log($"[Screenshot] Pin failed: {ex}");
            SelectionHintText.Text = _localization.T("Screenshot.SaveFailed");
        }
        finally
        {
            SetCaptureBusy(false);
        }
    }

    // ---------------------------------------------------------------- overlay

    /// <summary>
    /// Rasterises the loupe straight from the frozen frame. Sampling here keeps
    /// the magnification exact: the picture is one screen pixel times
    /// <see cref="LoupeZoom"/> at any DPI, with no render transform, no clip and
    /// no dependence on the frozen image's layout size.
    /// </summary>
    private void RenderLoupe(int frameX, int frameY)
    {
        if (_frozenFrame is not { } frame || frame.Width <= 0 || frame.Height <= 0 || _dpiScale <= 0)
        {
            return;
        }

        // The panel is sized in DIP; rasterising at the physical size keeps the
        // loupe sharp at fractional DPI scales such as 125%.
        int loupePixels = Math.Clamp(
            (int)Math.Round(LoupePanelSize * _dpiScale), 32, 512);
        if (_loupeBitmap is null ||
            _loupeBitmap.PixelWidth != loupePixels ||
            _loupeBitmap.PixelHeight != loupePixels)
        {
            _loupeBitmap = new WriteableBitmap(loupePixels, loupePixels);
            _loupePixels = new byte[loupePixels * loupePixels * 4];
            MagnifierImage.Source = _loupeBitmap;
        }

        if (_lastLoupeX == frameX && _lastLoupeY == frameY)
        {
            return;
        }
        _lastLoupeX = frameX;
        _lastLoupeY = frameY;
        byte[] source = frame.Pixels;
        byte[] target = _loupePixels!;
        int sourceStride = frame.Width * 4;
        // One screen pixel covers this many loupe pixels; keeping it fractional
        // means the zoom stays true even when DPI makes it non-integral.
        double pixelsPerFramePixel = LoupeZoom * _dpiScale;
        int center = loupePixels / 2;

        for (int y = 0; y < loupePixels; y++)
        {
            int sourceY = frameY + (int)Math.Round((y - center) / pixelsPerFramePixel);
            sourceY = Math.Clamp(sourceY, 0, frame.Height - 1);
            int sourceRow = sourceY * sourceStride;
            int targetRow = y * loupePixels * 4;
            for (int x = 0; x < loupePixels; x++)
            {
                int sourceX = frameX + (int)Math.Round((x - center) / pixelsPerFramePixel);
                sourceX = Math.Clamp(sourceX, 0, frame.Width - 1);
                int sourceIndex = sourceRow + (sourceX * 4);
                int targetIndex = targetRow + (x * 4);
                // CapturedFrame is BGRA; the loupe is opaque, so alpha is fixed.
                target[targetIndex] = source[sourceIndex];
                target[targetIndex + 1] = source[sourceIndex + 1];
                target[targetIndex + 2] = source[sourceIndex + 2];
                target[targetIndex + 3] = 0xFF;
            }
        }

        using (Stream loupeStream = _loupeBitmap.PixelBuffer.AsStream())
        {
            loupeStream.Write(target, 0, target.Length);
        }

        _loupeBitmap.Invalidate();
    }

    /// <summary>
    /// Positions the loupe and the pixel readout for the pointer. Everything
    /// here is chrome: it reads the frozen frame and never the live screen.
    /// </summary>
    private void UpdatePointerOverlay(Windows.Foundation.Point point)
    {
        double windowWidth = RootGrid.ActualWidth;
        double windowHeight = RootGrid.ActualHeight;
        // ActualWidth of the frozen image is the DIP<->pixel ruler; before the
        // first layout pass it is 0 and every coordinate below would collapse
        // onto the monitor origin.
        if (_isClosed || _isCapturing || _activeTool != ScreenshotAnnotationTool.None ||
            (_hasSelection && !_colorPickMode && _dragMode == DragMode.None) ||
            _frozenFrame is null || FrozenScreenImage.Source is null ||
            windowWidth <= 0 || windowHeight <= 0 ||
            FrozenScreenImage.ActualWidth <= 0 || FrozenScreenImage.ActualHeight <= 0)
        {
            HidePointerOverlay();
            return;
        }

        // Physical pixels, not DIP: the readout is compared against other
        // tools, and ToCaptureX/Y already return absolute screen pixels.
        int screenX = ToCaptureX(point.X);
        int screenY = ToCaptureY(point.Y);
        int frameX = screenX - _monitorRect.Left;
        int frameY = screenY - _monitorRect.Top;

        PointerCoordText.Text = _localization.Format("Screenshot.CoordinatesFormat", screenX, screenY);
        string? hex = ReadFrozenPixelHex(frameX, frameY);
        PointerColorText.Text = hex ?? "--";
        if (hex is not null)
        {
            _pixelSwatchBrush.Color = Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex.Substring(1, 2), 16),
                Convert.ToByte(hex.Substring(3, 2), 16),
                Convert.ToByte(hex.Substring(5, 2), 16));
            PixelColorSwatch.Background = _pixelSwatchBrush;
        }
        bool canCopyColor = _colorPickMode || (!_hasSelection && _dragMode == DragMode.None);
        ColorHintText.Visibility = canCopyColor ? Visibility.Visible : Visibility.Collapsed;
        if (Environment.TickCount64 >= _colorCopiedUntil)
        {
            ColorHintText.Text = _localization.T("Screenshot.ColorHint");
        }

        MagnifierPanel.Visibility = Visibility.Visible;
        MagnifierPanel.Measure(new Windows.Foundation.Size(160, double.PositiveInfinity));
        double panelSize = MagnifierPanel.DesiredSize.Width;
        double panelHeight = MagnifierPanel.DesiredSize.Height;
        double left = point.X + LoupeCursorOffset;
        double top = point.Y + LoupeCursorOffset;
        if (left + panelSize > windowWidth)
        {
            left = point.X - LoupeCursorOffset - panelSize;
        }
        if (top + panelHeight > windowHeight)
        {
            top = point.Y - LoupeCursorOffset - panelHeight;
        }
        left = Math.Clamp(left, 0, Math.Max(0, windowWidth - panelSize));
        top = Math.Clamp(top, 0, Math.Max(0, windowHeight - panelHeight));

        // The loupe image is sized to the on-screen size of the frozen frame, so
        // one DIP in the loupe is one DIP on screen and the pointer position can
        // be used directly. The previous version worked in frame pixels against
        // a DIP-sized image, which at 125% DPI centred the loupe on a point 25%
        // away from the cursor.
        RenderLoupe(frameX, frameY);
        Canvas.SetLeft(MagnifierPanel, left);
        Canvas.SetTop(MagnifierPanel, top);
        MagnifierPanel.Visibility = Visibility.Visible;

        // The loupe is chrome the user either sees or does not, and the log is
        // the only way to tell "never shown" from "shown somewhere useless".
        if (_loupeLogs < 3)
        {
            _loupeLogs++;
            App.Log(
                $"[Screenshot] Loupe left={left:F0} top={top:F0} " +
                $"src=({frameX},{frameY}) bitmap={_loupeBitmap?.PixelWidth ?? 0}x{_loupeBitmap?.PixelHeight ?? 0} " +
                $"panel={MagnifierPanel.ActualWidth:F0}x{MagnifierPanel.ActualHeight:F0} " +
                $"z={Canvas.GetZIndex(MagnifierPanel)}");
        }

    }

    private void QueuePointerOverlayUpdate(Windows.Foundation.Point point)
    {
        _pendingPointerOverlay = point;
        if (_pointerOverlayQueued) return;
        _pointerOverlayQueued = true;
        CompositionTarget.Rendering += RenderPendingPointerOverlay;
    }

    private void RenderPendingPointerOverlay(object? sender, object args)
    {
        var point = _pendingPointerOverlay;
        CancelPointerOverlayUpdate();
        if (!_isClosed && point is { } position) UpdatePointerOverlay(position);
    }

    private void CancelPointerOverlayUpdate()
    {
        if (_pointerOverlayQueued) CompositionTarget.Rendering -= RenderPendingPointerOverlay;
        _pointerOverlayQueued = false;
        _pendingPointerOverlay = null;
    }

    private void HidePointerOverlay()
    {
        CancelPointerOverlayUpdate();
        MagnifierPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateThirdsGuides()
    {
        // Rule-of-thirds guides belong to an in-progress free-region drag; a
        // hovered window preview and a finished selection both drop them.
        double width = Math.Max(0, SelectionBorder.Width - 6);
        double height = Math.Max(0, SelectionBorder.Height - 6);
        if (!_isDraggingRegion || width < 12 || height < 12)
        {
            ThirdsCanvas.Visibility = Visibility.Collapsed;
            return;
        }

        double thirdWidth = width / 3;
        double thirdHeight = height / 3;
        SetGuide(ThirdsVertical1, thirdWidth, 0, 1, height);
        SetGuide(ThirdsVertical2, thirdWidth * 2, 0, 1, height);
        SetGuide(ThirdsHorizontal1, 0, thirdHeight, width, 1);
        SetGuide(ThirdsHorizontal2, 0, thirdHeight * 2, width, 1);
        ThirdsCanvas.Visibility = Visibility.Visible;
    }

    private static void SetGuide(Rectangle guide, double left, double top, double width, double height)
    {
        Canvas.SetLeft(guide, Math.Round(left));
        Canvas.SetTop(guide, Math.Round(top));
        guide.Width = Math.Max(1, Math.Round(width));
        guide.Height = Math.Max(1, Math.Round(height));
    }

    // Visual (DIP) to physical mapping rides the FrozenScreenImage fill
    // ratio — an exact round-trip is unnecessary because the same transform
    // is applied in both directions.
    private int ToCaptureX(double x) => _monitorRect.Left + (int)Math.Round(
        Math.Clamp(x, 0, Math.Max(1, FrozenScreenImage.ActualWidth)) *
        Math.Max(1, _monitorRect.Right - _monitorRect.Left) / Math.Max(1, FrozenScreenImage.ActualWidth));

    private int ToCaptureY(double y) => _monitorRect.Top + (int)Math.Round(
        Math.Clamp(y, 0, Math.Max(1, FrozenScreenImage.ActualHeight)) *
        Math.Max(1, _monitorRect.Bottom - _monitorRect.Top) / Math.Max(1, FrozenScreenImage.ActualHeight));

    private double ToVisualX(int x) => (x - _monitorRect.Left) * FrozenScreenImage.ActualWidth /
        Math.Max(1, _monitorRect.Right - _monitorRect.Left);

    private double ToVisualY(int y) => (y - _monitorRect.Top) * FrozenScreenImage.ActualHeight /
        Math.Max(1, _monitorRect.Bottom - _monitorRect.Top);

    // ----------------------------------------------------------------- export

    private async Task<CapturedFrame?> BuildExportFrameAsync()
    {
        if (_frozenFrame is not { } frozen)
        {
            return null;
        }

        AnnotationLayer.CommitPendingEdit();
        Win32Helper.RECT selectedRect = _selectedRect;
        Win32Helper.RECT monitorRect = _monitorRect;
        int width = Math.Max(1, selectedRect.Right - selectedRect.Left);
        int height = Math.Max(1, selectedRect.Bottom - selectedRect.Top);

        // Always crop the frozen screen. The overlay's whole coordinate system —
        // selection, grips, annotations — is the frozen frame, so the exported
        // base has to come from that same bitmap. Re-reading the window with
        // PrintWindow used to look like a free win ("unobstructed content"), but
        // it returns the window's own size and DPI, which only line up with the
        // selection by accident; when they did not, the pasted image came out
        // sheared and unreadable.
        CapturedFrame frame = await Task.Run(() => ScreenCaptureService.Crop(
            frozen,
            selectedRect.Left - monitorRect.Left,
            selectedRect.Top - monitorRect.Top,
            width,
            height));
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            return null;
        }
        if (!AnnotationLayer.HasAnnotations)
        {
            return frame;
        }

        // Annotations are rasterised straight into the frame's own pixel buffer.
        // The XAML layer above is only the live preview: nothing about the export
        // goes through the compositor, so there is no DIP-versus-pixel rescaling
        // to get wrong, no question about whether RenderTargetBitmap keeps its
        // transparency, and no reliance on an occluded element still being
        // rendered. Every mature capture tool works this way for the same reason.
        CapturedFrame annotated = AnnotationLayer.RenderToFrame(frame, _dpiScale);
        App.Log(
            $"[Screenshot] Annotations rasterized size={annotated.Width}x{annotated.Height} " +
            $"dpi={_dpiScale:F2}");
        return annotated;
    }
    private async Task ExportSelectionAsync(bool copyToClipboard)
    {
        if (_isCapturing || !_hasSelection || _frozenFrame is null)
        {
            return;
        }

        SetCaptureBusy(true);
        try
        {
            CapturedFrame? exported = await BuildExportFrameAsync();
            if (exported is not { } frame)
            {
                throw new InvalidOperationException("Empty capture frame.");
            }

            if (copyToClipboard)
            {
                // Intentionally not disposed: the DataPackage carries a
                // RandomAccessStreamReference, not a copy, so the OS reads this
                // stream when it materializes the bitmap (Clipboard.Flush is
                // what forces that). Disposing here would hand the paste
                // target a dead stream. The COM reference held by the
                // DataPackage keeps the native stream alive; the managed
                // wrapper is collected with the package.
                var stream = new InMemoryRandomAccessStream();
                if (!await ScreenCaptureService.TryEncodePngToStreamAsync(frame, stream))
                {
                    throw new InvalidOperationException("PNG encode failed.");
                }

                stream.Seek(0);
                var dataPackage = new DataPackage();
                dataPackage.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));
                await CopyBitmapWithRetryAsync(dataPackage);
                App.Log($"[Screenshot] Copied mode={GetCaptureMode()}");
                _closeReason = "copy";
                Close();
                return;
            }

            string? destinationPath = await FileSavePickerService.PickSaveFileAsync(
                _hwnd,
                $"DeskBox-{DateTime.Now:yyyyMMdd-HHmmss}",
                [".png"]);
            if (destinationPath is null)
            {
                return;
            }

            if (!await ScreenCaptureService.TryEncodePngAsync(frame, destinationPath))
            {
                throw new InvalidOperationException($"PNG write failed for '{destinationPath}'.");
            }

            App.Log($"[Screenshot] Saved mode={GetCaptureMode()} path={destinationPath}");
            _closeReason = "save";
            Close();
        }
        catch (Exception ex)
        {
            App.Log($"[Screenshot] Export failed: {ex}");
            SelectionHintText.Text = _localization.T("Screenshot.SaveFailed");
        }
        finally
        {
            SetCaptureBusy(false);
        }
    }

    private string GetCaptureMode() => _isManualRegion
        ? "region"
        : _selectedWindow == IntPtr.Zero
            ? "monitor"
            : "window";

    private void CopyButton_Click(object sender, RoutedEventArgs e) =>
        _ = ExportSelectionAsync(copyToClipboard: true);

    private void SaveButton_Click(object sender, RoutedEventArgs e) =>
        _ = ExportSelectionAsync(copyToClipboard: false);

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _closeReason = "cancel";
        Close();
    }

    private void EscapeAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (_isCapturing || IsTextInputFocused())
        {
            return;
        }
        args.Handled = true;
        // Esc backs out one level at a time: the colour picker first, then the
        // armed tool, then the live selection, and only closes the overlay when
        // nothing is pending.
        if (_colorPickMode)
        {
            ToggleColorPickMode();
            return;
        }

        if (_activeTool != ScreenshotAnnotationTool.None)
        {
            SetActiveTool(ScreenshotAnnotationTool.None);
            return;
        }

        if (_hasSelection)
        {
            ResetSelectionState();
            return;
        }

        _closeReason = "esc";
        Close();
    }

    private void CopyAccelerator_Invoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        // Hovering copies the displayed pixel color. Once a region is accepted,
        // Ctrl+C shares the screenshot export path with the copy button.
        if (_isCapturing || IsTextInputFocused() || _dragMode != DragMode.None)
        {
            return;
        }

        if (_colorPickMode || (!_hasSelection && _dragMode == DragMode.None))
        {
            // Before selecting a region, Ctrl+C means "copy the sampled colour", which
            // is what the hint under the readout promises.
            args.Handled = true;
            CopyColorAtCursor();
            return;
        }

        if (_preSelectMode)
        {
            _hasSelection = true;
            CommitSelection();
        }

        if (!_hasSelection)
        {
            return;
        }

        args.Handled = true;
        _ = ExportSelectionAsync(copyToClipboard: true);
    }

    // ------------------------------------------------------- window discovery

    private IReadOnlyList<ScreenshotSelectionTarget> CaptureSelectionTargets()
    {
        var targets = new List<ScreenshotSelectionTarget>();
        Win32Helper.EnumWindows((window, _) =>
        {
            if (window == _hwnd || !Win32Helper.IsWindowVisible(window) ||
                Win32Helper.IsIconic(window) || Win32Helper.TryGetDwmCloakState(window) > 0 ||
                (Win32Helper.GetWindowLong(window, Win32Helper.GWL_EXSTYLE) & Win32Helper.WS_EX_TRANSPARENT) != 0 ||
                !TryGetCaptureBounds(window, out var candidate) ||
                candidate.Right <= candidate.Left || candidate.Bottom <= candidate.Top ||
                GetWindowClassName(window) is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd" or "DV2ControlHost")
            {
                return true;
            }

            targets.Add(new(window, new Windows.Foundation.Rect(
                candidate.Left, candidate.Top,
                candidate.Right - candidate.Left, candidate.Bottom - candidate.Top)));
            return true;
        }, IntPtr.Zero);
        return targets;
    }

    private IntPtr FindWindowAtPoint(Win32Helper.POINT point, out Win32Helper.RECT rect)
    {
        var target = ScreenshotSelectionSnapshot.FindAt(
            _selectionTargets, new Windows.Foundation.Point(point.X, point.Y),
            new Windows.Foundation.Rect(_monitorRect.Left, _monitorRect.Top,
                _monitorRect.Right - _monitorRect.Left, _monitorRect.Bottom - _monitorRect.Top));
        rect = new Win32Helper.RECT
        {
            Left = (int)target.Bounds.Left, Top = (int)target.Bounds.Top,
            Right = (int)target.Bounds.Right, Bottom = (int)target.Bounds.Bottom
        };
        return target.Window;
    }

    private static bool TryGetCaptureBounds(IntPtr window, out Win32Helper.RECT bounds)
    {
        if (Win32Helper.TryGetExtendedFrameBounds(window, out Win32Helper.NativeRect frame) &&
            frame.Right > frame.Left && frame.Bottom > frame.Top)
        {
            bounds = new Win32Helper.RECT
            {
                Left = frame.Left,
                Top = frame.Top,
                Right = frame.Right,
                Bottom = frame.Bottom,
            };
            return true;
        }

        return Win32Helper.GetWindowRect(window, out bounds);
    }

    private static string GetWindowTitle(IntPtr window, LocalizationService localization)
    {
        var text = new StringBuilder(256);
        Win32Helper.GetWindowText(window, text, text.Capacity);
        string title = text.ToString();
        return string.IsNullOrWhiteSpace(title)
            ? localization.T("Screenshot.WindowFallbackTitle")
            : title;
    }

    private static string GetWindowClassName(IntPtr window)
    {
        var text = new StringBuilder(128);
        Win32Helper.GetClassName(window, text, text.Capacity);
        return text.ToString();
    }

    private enum DragMode
    {
        None,
        Create,
        Move,
        Resize,
    }

    // Order matters: the numeric value addresses _handles[index - 1].
    private enum ResizeHandle
    {
        None = 0,
        NorthWest = 1,
        North = 2,
        NorthEast = 3,
        West = 4,
        East = 5,
        SouthWest = 6,
        South = 7,
        SouthEast = 8,
    }
}
