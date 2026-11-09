using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using DeskBox.Helpers;
using DeskBox.Services;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.System;
using Windows.UI;
using ShapePath = Microsoft.UI.Xaml.Shapes.Path;

namespace DeskBox.Controls;

/// <summary>
/// Snipaste-style annotation layer for the screenshot capture flow.
/// <para>
/// The committed annotation list is the single source of truth: the canvas on
/// screen is rebuilt from it, and <see cref="BuildExportVisual"/> rebuilds the
/// very same model through the very same visual factory, so the exported
/// pixels and the on-screen pixels cannot diverge. Nothing here is drawn
/// straight from pointer input - an in-progress drag only ever owns a
/// throwaway preview element.
/// </para>
/// <para>
/// The layer carries no user-visible text of its own (the host owns the action
/// bar and every label), so it needs no resource keys.
/// </para>
/// <para>
/// Mosaic and blur are done in software over the BGRA frame handed in by
/// <see cref="SetSourceFrame(byte[], int, int)"/>: the repository has no Win2D
/// dependency and none may be added, so block averaging and a separable box
/// blur produce a <see cref="WriteableBitmap"/> that is rebuilt identically on
/// export.
/// </para>
/// </summary>
public sealed partial class ScreenshotAnnotationLayer : UserControl
{
    private const double DefaultStrokeThickness = 3;
    private const double DefaultMarkerThickness = 14;
    private const double MarkerOpacity = 0.35;
    private const double MarkerThicknessScale = 4.5;
    private const double MinimumDragSize = 2;
    private const double EraserTolerance = 6;
    private const double FreehandPointSpacing = 1.2;
    private const double MosaicBlockSizeDip = 8;
    private const double BlurRadiusDip = 5;
    private const int BlurIterations = 2;
    private const double TextFontSize = 18;
    private const double TextEditorMinWidth = 24;
    private const double TextWidthEstimateRatio = 0.6;
    private const double TextHeightEstimateRatio = 1.35;
    private const double BoundsChangeEpsilon = 0.5;

    // Cursors are cached per shape: every uncached assignment would create
    // another native cursor object, and the tool can be switched repeatedly.
    private static readonly Dictionary<InputSystemCursorShape, InputSystemCursor> CursorCache = [];

    private static readonly Color DefaultStrokeColor = Color.FromArgb(0xFF, 0xFF, 0x3B, 0x30);
    private static readonly Color TransparentColor = Color.FromArgb(0x00, 0x00, 0x00, 0x00);
    private static readonly Color PreviewOutlineColor = Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF);
    private static readonly Color PreviewFillColor = Color.FromArgb(0x22, 0x00, 0x00, 0x00);
    private static readonly Color TextEditorBackdropColor = Color.FromArgb(0x40, 0x00, 0x00, 0x00);

    private readonly List<ScreenshotAnnotation> _annotations = [];
    private readonly Stack<IReadOnlyList<ScreenshotAnnotation>> _undoStack = new();
    private readonly Stack<IReadOnlyList<ScreenshotAnnotation>> _redoStack = new();
    private readonly List<Point> _dragPoints = [];

    private ScreenshotAnnotationTool _activeTool = ScreenshotAnnotationTool.None;
    private Color _strokeColor = DefaultStrokeColor;
    private double _strokeThickness = DefaultStrokeThickness;

    private double _boundsLeft;
    private double _boundsTop;
    private double _boundsWidth;
    private double _boundsHeight;
    private bool _hasBounds;

    private byte[]? _sourcePixels;
    private int _sourceWidth;
    private int _sourceHeight;

    private bool _isPointerDrawing;
    private bool _isErasing;
    private ScreenshotAnnotationTool _dragTool = ScreenshotAnnotationTool.None;
    private Point _dragStart;
    private Point _dragCurrent;
    private UIElement? _previewVisual;

    private TextBox? _textEditor;
    private Point _textEditorOrigin;
    private bool _isCommittingText;
    private bool _isRebuilding;

    public ScreenshotAnnotationLayer()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Raised after every change to the committed annotation list, including
    /// undo, redo, clear and eraser removals. Hosts use it to refresh their
    /// own enabled state for undo/redo/save.
    /// </summary>
    public event EventHandler? AnnotationsChanged;

    /// <summary>
    /// The armed tool. <see cref="ScreenshotAnnotationTool.None"/> makes the
    /// layer pointer-transparent, which is what lets the capture overlay keep
    /// its own selection gestures.
    /// </summary>
    public ScreenshotAnnotationTool ActiveTool
    {
        get => _activeTool;
        set
        {
            try
            {
                ScreenshotAnnotationTool tool = Enum.IsDefined(value)
                    ? value
                    : ScreenshotAnnotationTool.None;
                if (_activeTool == tool)
                {
                    return;
                }

                // Whatever the previous tool was in the middle of is settled
                // before the new tool arms, so a half-dragged shape can never
                // leak into the next one.
                CommitTextEditor(commit: true);
                CancelPreview();
                _isErasing = false;
                _activeTool = tool;
                IsHitTestVisible = tool != ScreenshotAnnotationTool.None;
                UpdateCursor(tool);
            }
            catch (Exception ex)
            {
                App.Log($"[ScreenshotAnnotation] ActiveTool set failed: {ex.Message}");
            }
        }
    }

    /// <summary>Stroke colour for every tool. Default is the Snipaste red.</summary>
    public Color StrokeColor
    {
        get => _strokeColor;
        set
        {
            try
            {
                _strokeColor = value;
            }
            catch (Exception ex)
            {
                App.Log($"[ScreenshotAnnotation] StrokeColor set failed: {ex.Message}");
            }
        }
    }

    /// <summary>Stroke width in DIP for every tool. Default is 3.</summary>
    public double StrokeThickness
    {
        get => _strokeThickness;
        set
        {
            try
            {
                if (double.IsFinite(value) && value > 0)
                {
                    _strokeThickness = value;
                }
            }
            catch (Exception ex)
            {
                App.Log($"[ScreenshotAnnotation] StrokeThickness set failed: {ex.Message}");
            }
        }
    }

    internal void CommitPendingEdit() => CommitTextEditor(commit: true);

    public bool HasAnnotations => _annotations.Count > 0;

    public bool CanUndo => _undoStack.Count > 0;

    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Log-only tool name, e.g. "arrow".</summary>
    public string ActiveToolName => _activeTool switch
    {
        ScreenshotAnnotationTool.Rectangle => "rectangle",
        ScreenshotAnnotationTool.Ellipse => "ellipse",
        ScreenshotAnnotationTool.Line => "line",
        ScreenshotAnnotationTool.Arrow => "arrow",
        ScreenshotAnnotationTool.Pencil => "pencil",
        ScreenshotAnnotationTool.Marker => "marker",
        ScreenshotAnnotationTool.Text => "text",
        ScreenshotAnnotationTool.Mosaic => "mosaic",
        ScreenshotAnnotationTool.Blur => "blur",
        ScreenshotAnnotationTool.Eraser => "eraser",
        _ => "none",
    };

    public void Undo()
    {
        try
        {
            CommitTextEditor(commit: true);
            CancelPreview();
            if (_undoStack.Count == 0)
            {
                return;
            }

            _redoStack.Push(SnapshotAnnotations());
            RestoreAnnotations(_undoStack.Pop());
            RebuildCanvas();
            RaiseAnnotationsChanged();
            App.Log($"[ScreenshotAnnotation] Undo depth={_undoStack.Count} redo={_redoStack.Count}");
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Undo failed: {ex.Message}");
        }
    }

    public void Redo()
    {
        try
        {
            CommitTextEditor(commit: true);
            CancelPreview();
            if (_redoStack.Count == 0)
            {
                return;
            }

            _undoStack.Push(SnapshotAnnotations());
            RestoreAnnotations(_redoStack.Pop());
            RebuildCanvas();
            RaiseAnnotationsChanged();
            App.Log($"[ScreenshotAnnotation] Redo depth={_undoStack.Count} redo={_redoStack.Count}");
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Redo failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Drops every annotation and the whole undo/redo history. This is the
    /// "new selection" reset: the host calls it when the annotations no longer
    /// belong to what is on screen, so a stale undo can never resurrect marks
    /// drawn over a previous selection.
    /// </summary>
    public void Clear()
    {
        try
        {
            CancelTextEditor();
            CancelPreview();
            _isErasing = false;
            _annotations.Clear();
            _undoStack.Clear();
            _redoStack.Clear();
            RebuildCanvas();
            RaiseAnnotationsChanged();
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Clear failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Positions the layer over the host window's selection rectangle, in DIP
    /// relative to the host's top-left corner. Changing the rectangle starts a
    /// new annotation session: the recorded DIPs belong to the old selection
    /// and are dropped, exactly like <see cref="Clear"/>.
    /// </summary>
    public void SetBounds(double leftDip, double topDip, double widthDip, double heightDip)
    {
        try
        {
            double width = double.IsFinite(widthDip) ? Math.Max(0, widthDip) : 0;
            double height = double.IsFinite(heightDip) ? Math.Max(0, heightDip) : 0;
            double left = double.IsFinite(leftDip) ? leftDip : 0;
            double top = double.IsFinite(topDip) ? topDip : 0;

            bool changed = !_hasBounds ||
                Math.Abs(left - _boundsLeft) > BoundsChangeEpsilon ||
                Math.Abs(top - _boundsTop) > BoundsChangeEpsilon ||
                Math.Abs(width - _boundsWidth) > BoundsChangeEpsilon ||
                Math.Abs(height - _boundsHeight) > BoundsChangeEpsilon;

            _boundsLeft = left;
            _boundsTop = top;
            _boundsWidth = width;
            _boundsHeight = height;
            _hasBounds = true;

            ApplyBoundsToLayout();

            if (changed)
            {
                CancelTextEditor();
                CancelPreview();
                _isErasing = false;
                _annotations.Clear();
                _undoStack.Clear();
                _redoStack.Clear();
                RebuildCanvas();
                RaiseAnnotationsChanged();
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] SetBounds failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Hands the layer the already-cropped selection frame in physical BGRA
    /// pixels; mosaic and blur sample it. Passing an unusable buffer simply
    /// disables those two tools.
    /// </summary>
    public void SetSourceFrame(byte[] pixels, int width, int height)
    {
        try
        {
            if (pixels is null || width <= 0 || height <= 0 ||
                pixels.LongLength < (long)width * height * 4)
            {
                _sourcePixels = null;
                _sourceWidth = 0;
                _sourceHeight = 0;
            }
            else
            {
                _sourcePixels = pixels;
                _sourceWidth = width;
                _sourceHeight = height;
            }

            RequestRebuild();
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] SetSourceFrame failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Convenience overload for the capture pipeline's own frame type. Kept
    /// internal because <see cref="CapturedFrame"/> is internal to the app
    /// assembly; the public surface takes the raw buffer instead.
    /// </summary>
    internal void SetSourceFrame(CapturedFrame frame) =>
        SetSourceFrame(frame.Pixels, frame.Width, frame.Height);

    /// <summary>
    /// Builds a brand-new canvas from the annotation model for the host to
    /// render with <c>RenderTargetBitmap</c>. It is sized to the selection
    /// bounds in DIP with the origin at the selection's top-left corner, and
    /// carries a transparent background so the host can composite it over the
    /// cropped screenshot. The layer currently on screen is never touched.
    /// </summary>
    public Canvas BuildExportVisual()
    {
        var canvas = new Canvas
        {
            Background = new SolidColorBrush(TransparentColor),
        };

        try
        {
            double width = _boundsWidth > 0 ? _boundsWidth : ActualWidth;
            double height = _boundsHeight > 0 ? _boundsHeight : ActualHeight;
            if (double.IsFinite(width) && width > 0)
            {
                canvas.Width = width;
            }

            if (double.IsFinite(height) && height > 0)
            {
                canvas.Height = height;
            }

            for (int i = 0; i < _annotations.Count; i++)
            {
                UIElement? visual = CreateAnnotationVisual(_annotations[i]);
                if (visual is not null)
                {
                    canvas.Children.Add(visual);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Export visual build failed: {ex.Message}");
        }

        return canvas;
    }

    private void AnnotationLayer_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            ApplyBoundsToLayout();
            RebuildCanvas();
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Loaded handling failed: {ex.Message}");
        }
    }

    // ---- layout ---------------------------------------------------------

    private void ApplyBoundsToLayout()
    {
        if (!_hasBounds)
        {
            return;
        }

        HorizontalAlignment = HorizontalAlignment.Left;
        VerticalAlignment = VerticalAlignment.Top;

        if (_boundsWidth > 0)
        {
            Width = _boundsWidth;
            AnnotationCanvas.Width = _boundsWidth;
        }

        if (_boundsHeight > 0)
        {
            Height = _boundsHeight;
            AnnotationCanvas.Height = _boundsHeight;
        }

        // A Canvas parent positions by attached properties and ignores Margin,
        // so the two placement idioms must not be mixed.
        if (Parent is Canvas hostCanvas)
        {
            Margin = new Thickness(0);
            Canvas.SetLeft(this, _boundsLeft);
            Canvas.SetTop(this, _boundsTop);
        }
        else
        {
            Margin = new Thickness(_boundsLeft, _boundsTop, 0, 0);
        }
    }

    private void UpdateCursor(ScreenshotAnnotationTool tool)
    {
        try
        {
            InputSystemCursorShape shape = tool switch
            {
                ScreenshotAnnotationTool.None => InputSystemCursorShape.Arrow,
                ScreenshotAnnotationTool.Text => InputSystemCursorShape.IBeam,
                ScreenshotAnnotationTool.Eraser => InputSystemCursorShape.Hand,
                _ => InputSystemCursorShape.Cross,
            };
            ProtectedCursor = GetCursor(shape);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Cursor update failed: {ex.Message}");
        }
    }

    private static InputSystemCursor GetCursor(InputSystemCursorShape shape)
    {
        lock (CursorCache)
        {
            if (!CursorCache.TryGetValue(shape, out InputSystemCursor? cursor) || cursor is null)
            {
                cursor = InputSystemCursor.Create(shape);
                CursorCache[shape] = cursor;
            }

            return cursor;
        }
    }

    private void RequestRebuild()
    {
        if (DispatcherQueue.HasThreadAccess)
        {
            RebuildCanvas();
            return;
        }

        if (!DispatcherQueue.TryEnqueue(RebuildCanvas))
        {
            App.Log("[ScreenshotAnnotation] Rebuild dispatch rejected.");
        }
    }

    // ---- pointer input --------------------------------------------------

    private void AnnotationCanvas_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_activeTool == ScreenshotAnnotationTool.None)
            {
                return;
            }

            // Clicks that land inside the inline editor belong to its caret,
            // even though they bubble through this canvas.
            if (IsInsideTextEditor(e.OriginalSource as DependencyObject))
            {
                return;
            }

            if (!e.GetCurrentPoint(AnnotationCanvas).Properties.IsLeftButtonPressed)
            {
                return;
            }

            // A press anywhere else settles the open editor before the new
            // gesture starts, matching how a click commits text in Snipaste.
            if (_textEditor is not null)
            {
                CommitTextEditor(commit: true);
            }

            Point position = ClampToBounds(e.GetCurrentPoint(AnnotationCanvas).Position);

            if (_activeTool == ScreenshotAnnotationTool.Text)
            {
                BeginTextEditor(position);
                e.Handled = true;
                return;
            }

            if (_activeTool == ScreenshotAnnotationTool.Eraser)
            {
                _isErasing = true;
                AnnotationCanvas.CapturePointer(e.Pointer);
                EraseAt(position);
                e.Handled = true;
                return;
            }

            if ((_activeTool == ScreenshotAnnotationTool.Mosaic ||
                 _activeTool == ScreenshotAnnotationTool.Blur) &&
                _sourcePixels is null)
            {
                App.Log($"[ScreenshotAnnotation] {ActiveToolName} ignored: no source frame.");
                return;
            }

            BeginPreview(position, e.Pointer);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Pointer pressed failed: {ex.Message}");
        }
    }

    private void AnnotationCanvas_PointerMoved(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (!_isPointerDrawing && !_isErasing)
            {
                return;
            }

            Point position = ClampToBounds(e.GetCurrentPoint(AnnotationCanvas).Position);
            if (_isErasing)
            {
                EraseAt(position);
                e.Handled = true;
                return;
            }

            UpdatePreview(position);
            e.Handled = true;
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Pointer moved failed: {ex.Message}");
        }
    }

    private void AnnotationCanvas_PointerReleased(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            if (_isErasing)
            {
                _isErasing = false;
                AnnotationCanvas.ReleasePointerCapture(e.Pointer);
                e.Handled = true;
                return;
            }

            if (!_isPointerDrawing)
            {
                return;
            }

            Point position = ClampToBounds(e.GetCurrentPoint(AnnotationCanvas).Position);
            // The in-progress state is dropped before capture is released so
            // the CaptureLost that follows cannot mistake this for an
            // interrupted drag.
            ScreenshotAnnotation? annotation = BuildDraggedAnnotation(position);
            CancelPreview();
            AnnotationCanvas.ReleasePointerCapture(e.Pointer);
            e.Handled = true;

            if (annotation is null)
            {
                return;
            }

            PushUndoSnapshot();
            _annotations.Add(annotation);
            RebuildCanvas();
            RaiseAnnotationsChanged();
            App.Log(
                $"[ScreenshotAnnotation] Committed tool={ActiveToolName} " +
                $"points={annotation.PointCount} total={_annotations.Count}");
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Pointer released failed: {ex.Message}");
        }
    }

    private void AnnotationCanvas_PointerCaptureLost(object sender, PointerRoutedEventArgs e)
    {
        try
        {
            // Capture can be stolen mid-gesture (focus switch, Win combos)
            // without a Released ever arriving; the preview is abandoned so a
            // stale drag cannot keep painting on the next move.
            if (!_isPointerDrawing && !_isErasing)
            {
                return;
            }

            bool wasDrawing = _isPointerDrawing;
            CancelPreview();
            _isErasing = false;
            App.Log($"[ScreenshotAnnotation] Pointer capture lost wasDrawing={wasDrawing}");
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Capture lost handling failed: {ex.Message}");
        }
    }

    private Point ClampToBounds(Point point)
    {
        double width = _boundsWidth > 0 ? _boundsWidth : AnnotationCanvas.ActualWidth;
        double height = _boundsHeight > 0 ? _boundsHeight : AnnotationCanvas.ActualHeight;
        double x = double.IsFinite(point.X) ? point.X : 0;
        double y = double.IsFinite(point.Y) ? point.Y : 0;
        if (width <= 0 || height <= 0)
        {
            return new Point(Math.Max(0, x), Math.Max(0, y));
        }

        return new Point(Math.Clamp(x, 0, width), Math.Clamp(y, 0, height));
    }

    // ---- in-progress preview --------------------------------------------

    private void BeginPreview(Point start, Microsoft.UI.Xaml.Input.Pointer pointer)
    {
        _dragTool = _activeTool;
        _dragStart = start;
        _dragCurrent = start;
        _dragPoints.Clear();
        _dragPoints.Add(start);
        _isPointerDrawing = true;
        AnnotationCanvas.CapturePointer(pointer);

        _previewVisual = CreatePreviewElement(_dragTool);
        if (_previewVisual is not null)
        {
            AnnotationCanvas.Children.Add(_previewVisual);
        }

        UpdatePreview(start);
    }

    private void UpdatePreview(Point current)
    {
        _dragCurrent = current;
        if (_dragTool is ScreenshotAnnotationTool.Pencil or ScreenshotAnnotationTool.Marker)
        {
            if (_dragPoints.Count == 0)
            {
                _dragPoints.Add(current);
            }
            else
            {
                Point last = _dragPoints[^1];
                double dx = current.X - last.X;
                double dy = current.Y - last.Y;
                if ((dx * dx) + (dy * dy) >= FreehandPointSpacing * FreehandPointSpacing)
                {
                    _dragPoints.Add(current);
                }
            }
        }

        ApplyPreviewGeometry();
    }

    private void ApplyPreviewGeometry()
    {
        if (_previewVisual is null)
        {
            return;
        }

        switch (_dragTool)
        {
            case ScreenshotAnnotationTool.Rectangle:
            case ScreenshotAnnotationTool.Ellipse:
            case ScreenshotAnnotationTool.Mosaic:
            case ScreenshotAnnotationTool.Blur:
            {
                Rect rect = NormalizeRect(_dragStart, _dragCurrent);
                if (_previewVisual is FrameworkElement element)
                {
                    element.Width = Math.Max(0, rect.Width);
                    element.Height = Math.Max(0, rect.Height);
                }

                Canvas.SetLeft(_previewVisual, rect.X);
                Canvas.SetTop(_previewVisual, rect.Y);
                break;
            }

            case ScreenshotAnnotationTool.Line:
            {
                if (_previewVisual is Line line)
                {
                    line.X1 = _dragStart.X;
                    line.Y1 = _dragStart.Y;
                    line.X2 = _dragCurrent.X;
                    line.Y2 = _dragCurrent.Y;
                }

                break;
            }

            case ScreenshotAnnotationTool.Arrow:
            {
                if (_previewVisual is ShapePath path)
                {
                    path.Data = BuildArrowGeometry(
                        _dragStart,
                        _dragCurrent,
                        _strokeThickness);
                }

                break;
            }

            case ScreenshotAnnotationTool.Pencil:
            case ScreenshotAnnotationTool.Marker:
            {
                if (_previewVisual is Polyline polyline)
                {
                    polyline.Points.Clear();
                    for (int i = 0; i < _dragPoints.Count; i++)
                    {
                        polyline.Points.Add(_dragPoints[i]);
                    }
                }

                break;
            }
        }
    }

    private UIElement? CreatePreviewElement(ScreenshotAnnotationTool tool)
    {
        Brush stroke = new SolidColorBrush(_strokeColor);
        switch (tool)
        {
            case ScreenshotAnnotationTool.Rectangle:
                return new Rectangle
                {
                    Stroke = stroke,
                    StrokeThickness = _strokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    IsHitTestVisible = false,
                };

            case ScreenshotAnnotationTool.Ellipse:
                return new Ellipse
                {
                    Stroke = stroke,
                    StrokeThickness = _strokeThickness,
                    IsHitTestVisible = false,
                };

            // The pixel-effect tools cannot afford a bitmap per pointer move,
            // so the drag shows the marquee and the mosaic appears on release.
            case ScreenshotAnnotationTool.Mosaic:
            case ScreenshotAnnotationTool.Blur:
                return new Rectangle
                {
                    Stroke = new SolidColorBrush(PreviewOutlineColor),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 4, 3 },
                    Fill = new SolidColorBrush(PreviewFillColor),
                    IsHitTestVisible = false,
                };

            case ScreenshotAnnotationTool.Line:
                return new Line
                {
                    Stroke = stroke,
                    StrokeThickness = _strokeThickness,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };

            case ScreenshotAnnotationTool.Arrow:
                return new ShapePath
                {
                    Stroke = stroke,
                    StrokeThickness = _strokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    Fill = stroke,
                    IsHitTestVisible = false,
                };

            case ScreenshotAnnotationTool.Pencil:
                return new Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = _strokeThickness,
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    IsHitTestVisible = false,
                };

            case ScreenshotAnnotationTool.Marker:
                return new Polyline
                {
                    Stroke = stroke,
                    StrokeThickness = Math.Max(_strokeThickness * MarkerThicknessScale, DefaultMarkerThickness),
                    StrokeLineJoin = PenLineJoin.Round,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    Opacity = MarkerOpacity,
                    IsHitTestVisible = false,
                };

            default:
                return null;
        }
    }

    private void CancelPreview()
    {
        _isPointerDrawing = false;
        _dragTool = ScreenshotAnnotationTool.None;
        _dragPoints.Clear();
        if (_previewVisual is null)
        {
            return;
        }

        try
        {
            AnnotationCanvas.Children.Remove(_previewVisual);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Preview removal failed: {ex.Message}");
        }

        _previewVisual = null;
    }

    private ScreenshotAnnotation? BuildDraggedAnnotation(Point current)
    {
        _dragCurrent = current;
        if (_dragTool is ScreenshotAnnotationTool.Pencil or ScreenshotAnnotationTool.Marker)
        {
            if (_dragPoints.Count == 0 || _dragPoints[^1] != current)
            {
                _dragPoints.Add(current);
            }

            return BuildFreehandAnnotation(_dragTool, _dragPoints);
        }

        return BuildTwoPointAnnotation(_dragTool, _dragStart, current);
    }

    private ScreenshotAnnotation? BuildTwoPointAnnotation(
        ScreenshotAnnotationTool tool,
        Point start,
        Point end)
    {
        double width = Math.Abs(end.X - start.X);
        double height = Math.Abs(end.Y - start.Y);
        switch (tool)
        {
            case ScreenshotAnnotationTool.Rectangle:
            case ScreenshotAnnotationTool.Ellipse:
            case ScreenshotAnnotationTool.Mosaic:
            case ScreenshotAnnotationTool.Blur:
                if (width < MinimumDragSize || height < MinimumDragSize)
                {
                    return null;
                }

                break;

            case ScreenshotAnnotationTool.Line:
            case ScreenshotAnnotationTool.Arrow:
                if (Math.Sqrt((width * width) + (height * height)) < MinimumDragSize)
                {
                    return null;
                }

                break;

            default:
                return null;
        }

        var annotation = new ScreenshotAnnotation(tool)
        {
            Color = _strokeColor,
            Thickness = _strokeThickness,
        };
        annotation.AddPoint(start);
        annotation.AddPoint(end);
        return annotation;
    }

    private ScreenshotAnnotation? BuildFreehandAnnotation(
        ScreenshotAnnotationTool tool,
        IReadOnlyList<Point> points)
    {
        if (points.Count < 2)
        {
            return null;
        }

        double thickness = tool == ScreenshotAnnotationTool.Marker
            ? Math.Max(_strokeThickness * MarkerThicknessScale, DefaultMarkerThickness)
            : _strokeThickness;

        var annotation = new ScreenshotAnnotation(tool)
        {
            Color = _strokeColor,
            Thickness = thickness,
        };
        annotation.AddPoints(points);
        return annotation;
    }

    // ---- inline text editor ---------------------------------------------

    private void BeginTextEditor(Point position)
    {
        CancelTextEditor();

        var editor = new TextBox
        {
            Text = string.Empty,
            FontSize = TextFontSize,
            Foreground = new SolidColorBrush(_strokeColor),
            Background = new SolidColorBrush(TextEditorBackdropColor),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            MinWidth = TextEditorMinWidth,
            MinHeight = 0,
            AcceptsReturn = false,
            TextWrapping = TextWrapping.NoWrap,
            IsSpellCheckEnabled = false,
            VerticalContentAlignment = VerticalAlignment.Top,
        };

        Canvas.SetLeft(editor, position.X);
        Canvas.SetTop(editor, position.Y);
        _textEditorOrigin = position;
        _textEditor = editor;
        AnnotationCanvas.Children.Add(editor);
        editor.AddHandler(
            UIElement.PreviewKeyDownEvent,
            new KeyEventHandler(TextEditor_PreviewKeyDown),
            handledEventsToo: true);
        editor.LostFocus += TextEditor_LostFocus;
        editor.Focus(FocusState.Programmatic);
    }

    private void TextEditor_PreviewKeyDown(object sender, KeyRoutedEventArgs e)
    {
        try
        {
            if (e.Key == VirtualKey.Enter)
            {
                // Enter confirms an IME candidate before it reaches us; committing here
                // would store the raw composition buffer as the annotation text.
                if (TextCompositionTracker.IsComposing(sender as TextBox))
                {
                    return;
                }

                e.Handled = true;
                CommitTextEditor(commit: true);
                return;
            }

            if (e.Key == VirtualKey.Escape)
            {
                if (TextCompositionTracker.IsComposing(sender as TextBox))
                {
                    // Escape dismisses the IME candidate list; ending the annotation
                    // here would throw away the text the user is still composing.
                    return;
                }

                e.Handled = true;
                CommitTextEditor(commit: false);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Text key handling failed: {ex.Message}");
        }
    }

    private void TextEditor_LostFocus(object sender, RoutedEventArgs e)
    {
        try
        {
            // Rebuilding the canvas detaches the editor, which raises
            // LostFocus again; the commit path must not re-enter itself.
            if (_isRebuilding || _isCommittingText)
            {
                return;
            }

            CommitTextEditor(commit: true);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Text focus loss failed: {ex.Message}");
        }
    }

    private void CommitTextEditor(bool commit)
    {
        if (_textEditor is null || _isCommittingText)
        {
            return;
        }

        _isCommittingText = true;
        try
        {
            string text = _textEditor.Text;
            Point origin = _textEditorOrigin;
            DetachTextEditor();

            if (!commit || string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            Size textSize = MeasureText(text, TextFontSize);
            var annotation = new ScreenshotAnnotation(ScreenshotAnnotationTool.Text)
            {
                Color = _strokeColor,
                Thickness = _strokeThickness,
                Text = text,
                FontSize = TextFontSize,
                TextSize = textSize,
            };
            annotation.AddPoint(origin);

            PushUndoSnapshot();
            _annotations.Add(annotation);
            RebuildCanvas();
            RaiseAnnotationsChanged();
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Text commit failed: {ex.Message}");
        }
        finally
        {
            _isCommittingText = false;
        }
    }

    private void CancelTextEditor()
    {
        if (_textEditor is null)
        {
            return;
        }

        bool wasCommitting = _isCommittingText;
        _isCommittingText = true;
        try
        {
            DetachTextEditor();
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Text editor cancel failed: {ex.Message}");
        }
        finally
        {
            _isCommittingText = wasCommitting;
        }
    }

    private void DetachTextEditor()
    {
        TextBox? editor = _textEditor;
        if (editor is null)
        {
            return;
        }

        _textEditor = null;
        editor.LostFocus -= TextEditor_LostFocus;
        try
        {
            AnnotationCanvas.Children.Remove(editor);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Text editor removal failed: {ex.Message}");
        }
    }

    private bool IsInsideTextEditor(DependencyObject? source)
    {
        if (_textEditor is null || source is null)
        {
            return false;
        }

        DependencyObject? current = source;
        while (current is not null)
        {
            if (ReferenceEquals(current, _textEditor))
            {
                return true;
            }

            current = VisualTreeHelper.GetParent(current);
        }

        return false;
    }

    private static Size MeasureText(string text, double fontSize)
    {
        try
        {
            var block = new TextBlock
            {
                Text = text,
                FontSize = fontSize,
                TextWrapping = TextWrapping.NoWrap,
            };
            block.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Size desired = block.DesiredSize;
            if (double.IsFinite(desired.Width) && double.IsFinite(desired.Height) &&
                desired.Width > 0 && desired.Height > 0)
            {
                return desired;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Text measurement failed: {ex.Message}");
        }

        // Measurement of an unparented TextBlock can come back empty on a
        // cold layout; a rough estimate keeps hit testing usable rather than
        // collapsing the text box to a point.
        return new Size(
            Math.Max(0, text.Length * fontSize * TextWidthEstimateRatio),
            Math.Max(0, fontSize * TextHeightEstimateRatio));
    }

    // ---- eraser ---------------------------------------------------------

    private void EraseAt(Point point)
    {
        for (int i = _annotations.Count - 1; i >= 0; i--)
        {
            if (!HitTestAnnotation(_annotations[i], point, EraserTolerance))
            {
                continue;
            }

            PushUndoSnapshot();
            _annotations.RemoveAt(i);
            RebuildCanvas();
            RaiseAnnotationsChanged();
            App.Log($"[ScreenshotAnnotation] Erased index={i} remaining={_annotations.Count}");
            return;
        }
    }

    private static bool HitTestAnnotation(
        ScreenshotAnnotation annotation,
        Point point,
        double tolerance)
    {
        double slack = tolerance + (annotation.Thickness / 2);
        switch (annotation.Kind)
        {
            case ScreenshotAnnotationTool.Text:
            case ScreenshotAnnotationTool.Mosaic:
            case ScreenshotAnnotationTool.Blur:
            {
                Rect region = annotation.GetCoveredBounds();
                return Inflate(region, tolerance).Contains(point);
            }

            case ScreenshotAnnotationTool.Rectangle:
            {
                Rect region = annotation.GetPointBounds();
                if (region.Width <= 0 || region.Height <= 0)
                {
                    return false;
                }

                // Only the outline is "the rectangle"; the middle of a big
                // hollow box is not something the user is pointing at.
                bool insideOuter = Inflate(region, slack).Contains(point);
                bool insideInner = Inflate(region, -slack).Contains(point);
                return insideOuter && !insideInner;
            }

            case ScreenshotAnnotationTool.Ellipse:
            {
                Rect region = annotation.GetPointBounds();
                if (region.Width <= 0 || region.Height <= 0)
                {
                    return false;
                }

                double radiusX = region.Width / 2;
                double radiusY = region.Height / 2;
                double centerX = region.X + radiusX;
                double centerY = region.Y + radiusY;
                double normalizedX = (point.X - centerX) / radiusX;
                double normalizedY = (point.Y - centerY) / radiusY;
                double normalizedRadius = Math.Sqrt(
                    (normalizedX * normalizedX) + (normalizedY * normalizedY));
                return Math.Abs(normalizedRadius - 1) * Math.Min(radiusX, radiusY) <= slack;
            }

            case ScreenshotAnnotationTool.Line:
            case ScreenshotAnnotationTool.Arrow:
            case ScreenshotAnnotationTool.Pencil:
            case ScreenshotAnnotationTool.Marker:
                return DistanceToPolyline(annotation.Points, point) <= slack;

            default:
                return false;
        }
    }

    private static double DistanceToPolyline(IReadOnlyList<Point> points, Point point)
    {
        if (points.Count == 0)
        {
            return double.PositiveInfinity;
        }

        if (points.Count == 1)
        {
            return Distance(points[0], point);
        }

        double best = double.PositiveInfinity;
        for (int i = 1; i < points.Count; i++)
        {
            double distance = DistanceToSegment(points[i - 1], points[i], point);
            if (distance < best)
            {
                best = distance;
            }
        }

        return best;
    }

    private static double DistanceToSegment(Point start, Point end, Point point)
    {
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared <= double.Epsilon)
        {
            return Distance(start, point);
        }

        double t = (((point.X - start.X) * dx) + ((point.Y - start.Y) * dy)) / lengthSquared;
        t = Math.Clamp(t, 0, 1);
        return Distance(new Point(start.X + (t * dx), start.Y + (t * dy)), point);
    }

    private static double Distance(Point first, Point second)
    {
        double dx = first.X - second.X;
        double dy = first.Y - second.Y;
        return Math.Sqrt((dx * dx) + (dy * dy));
    }

    private static Rect Inflate(Rect rect, double amount)
    {
        double x = rect.X - amount;
        double y = rect.Y - amount;
        double width = Math.Max(0, rect.Width + (amount * 2));
        double height = Math.Max(0, rect.Height + (amount * 2));
        return new Rect(x, y, width, height);
    }

    // ---- shared visual factory (screen and export both use this) --------

    private UIElement? CreateAnnotationVisual(ScreenshotAnnotation annotation)
    {
        try
        {
            var stroke = new SolidColorBrush(annotation.Color);
            switch (annotation.Kind)
            {
                case ScreenshotAnnotationTool.Rectangle:
                {
                    Rect rect = annotation.GetPointBounds();
                    var rectangle = new Rectangle
                    {
                        Width = Math.Max(0, rect.Width),
                        Height = Math.Max(0, rect.Height),
                        Stroke = stroke,
                        StrokeThickness = annotation.Thickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(rectangle, rect.X);
                    Canvas.SetTop(rectangle, rect.Y);
                    return rectangle;
                }

                case ScreenshotAnnotationTool.Ellipse:
                {
                    Rect rect = annotation.GetPointBounds();
                    var ellipse = new Ellipse
                    {
                        Width = Math.Max(0, rect.Width),
                        Height = Math.Max(0, rect.Height),
                        Stroke = stroke,
                        StrokeThickness = annotation.Thickness,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(ellipse, rect.X);
                    Canvas.SetTop(ellipse, rect.Y);
                    return ellipse;
                }

                case ScreenshotAnnotationTool.Line:
                {
                    if (annotation.PointCount < 2)
                    {
                        return null;
                    }

                    return new Line
                    {
                        X1 = annotation.Points[0].X,
                        Y1 = annotation.Points[0].Y,
                        X2 = annotation.Points[1].X,
                        Y2 = annotation.Points[1].Y,
                        Stroke = stroke,
                        StrokeThickness = annotation.Thickness,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false,
                    };
                }

                case ScreenshotAnnotationTool.Arrow:
                {
                    if (annotation.PointCount < 2)
                    {
                        return null;
                    }

                    return new ShapePath
                    {
                        Data = BuildArrowGeometry(
                            annotation.Points[0],
                            annotation.Points[1],
                            annotation.Thickness),
                        Stroke = stroke,
                        StrokeThickness = annotation.Thickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        Fill = stroke,
                        IsHitTestVisible = false,
                    };
                }

                case ScreenshotAnnotationTool.Pencil:
                case ScreenshotAnnotationTool.Marker:
                {
                    var polyline = new Polyline
                    {
                        Stroke = stroke,
                        StrokeThickness = annotation.Thickness,
                        StrokeLineJoin = PenLineJoin.Round,
                        StrokeStartLineCap = PenLineCap.Round,
                        StrokeEndLineCap = PenLineCap.Round,
                        IsHitTestVisible = false,
                    };
                    for (int i = 0; i < annotation.Points.Count; i++)
                    {
                        polyline.Points.Add(annotation.Points[i]);
                    }

                    if (annotation.Kind == ScreenshotAnnotationTool.Marker)
                    {
                        polyline.Opacity = MarkerOpacity;
                    }

                    return polyline;
                }

                case ScreenshotAnnotationTool.Text:
                {
                    if (string.IsNullOrEmpty(annotation.Text))
                    {
                        return null;
                    }

                    var block = new TextBlock
                    {
                        Text = annotation.Text,
                        FontSize = annotation.FontSize > 0 ? annotation.FontSize : TextFontSize,
                        Foreground = stroke,
                        TextWrapping = TextWrapping.NoWrap,
                        IsHitTestVisible = false,
                    };
                    Canvas.SetLeft(block, annotation.FirstPoint.X);
                    Canvas.SetTop(block, annotation.FirstPoint.Y);
                    return block;
                }

                case ScreenshotAnnotationTool.Mosaic:
                case ScreenshotAnnotationTool.Blur:
                {
                    PixelEffect? effect = BuildPixelEffect(annotation);
                    if (effect is not { } built)
                    {
                        return null;
                    }

                    var image = new Image
                    {
                        Source = built.Bitmap,
                        Width = built.Width,
                        Height = built.Height,
                        Stretch = Stretch.Fill,
                        IsHitTestVisible = false,
                        Clip = new RectangleGeometry
                        {
                            Rect = new Rect(0, 0, built.Width, built.Height),
                        },
                    };
                    Canvas.SetLeft(image, built.Left);
                    Canvas.SetTop(image, built.Top);
                    return image;
                }

                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Visual build failed: {ex.Message}");
            return null;
        }
    }

    private static Geometry BuildArrowGeometry(Point start, Point end, double thickness)
    {
        var geometry = new PathGeometry();
        double dx = end.X - start.X;
        double dy = end.Y - start.Y;
        double length = Math.Sqrt((dx * dx) + (dy * dy));
        if (length < 0.001)
        {
            return geometry;
        }

        double unitX = dx / length;
        double unitY = dy / length;
        double headLength = Math.Clamp(thickness * 3.5, 9, 28);
        if (headLength > length * 0.6)
        {
            headLength = length * 0.6;
        }

        double headWidth = headLength * 0.62;
        var basePoint = new Point(end.X - (unitX * headLength), end.Y - (unitY * headLength));
        double perpX = -unitY;
        double perpY = unitX;
        var leftPoint = new Point(
            basePoint.X + (perpX * headWidth / 2),
            basePoint.Y + (perpY * headWidth / 2));
        var rightPoint = new Point(
            basePoint.X - (perpX * headWidth / 2),
            basePoint.Y - (perpY * headWidth / 2));

        var shaft = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            IsFilled = false,
        };
        shaft.Segments.Add(new LineSegment { Point = basePoint });
        geometry.Figures.Add(shaft);

        var head = new PathFigure
        {
            StartPoint = end,
            IsClosed = true,
            IsFilled = true,
        };
        head.Segments.Add(new LineSegment { Point = leftPoint });
        head.Segments.Add(new LineSegment { Point = rightPoint });
        geometry.Figures.Add(head);

        return geometry;
    }

    private static Rect NormalizeRect(Point start, Point end)
    {
        double left = Math.Min(start.X, end.X);
        double top = Math.Min(start.Y, end.Y);
        return new Rect(left, top, Math.Abs(end.X - start.X), Math.Abs(end.Y - start.Y));
    }

    // ---- software pixel effects (no Win2D in this repository) -----------

    private readonly record struct PixelEffect(
        WriteableBitmap Bitmap,
        double Left,
        double Top,
        double Width,
        double Height);

    private PixelEffect? BuildPixelEffect(ScreenshotAnnotation annotation)
    {
        if (_sourcePixels is null || _sourceWidth <= 0 || _sourceHeight <= 0)
        {
            return null;
        }

        double scaleX = _boundsWidth > 0 ? _sourceWidth / _boundsWidth : 1;
        double scaleY = _boundsHeight > 0 ? _sourceHeight / _boundsHeight : 1;
        if (!double.IsFinite(scaleX) || scaleX <= 0)
        {
            scaleX = 1;
        }

        if (!double.IsFinite(scaleY) || scaleY <= 0)
        {
            scaleY = 1;
        }

        Rect region = annotation.GetPointBounds();
        if (region.Width <= 0 || region.Height <= 0)
        {
            return null;
        }

        int pixelX = (int)Math.Round(region.X * scaleX);
        int pixelY = (int)Math.Round(region.Y * scaleY);
        int pixelWidth = (int)Math.Round(region.Width * scaleX);
        int pixelHeight = (int)Math.Round(region.Height * scaleY);
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return null;
        }

        (int clampedX, int clampedY, int clampedWidth, int clampedHeight) =
            ClampRegion(pixelX, pixelY, pixelWidth, pixelHeight, _sourceWidth, _sourceHeight);
        if (clampedWidth <= 0 || clampedHeight <= 0)
        {
            return null;
        }

        byte[] regionPixels = ExtractRegion(clampedX, clampedY, clampedWidth, clampedHeight);
        byte[] processed = annotation.Kind == ScreenshotAnnotationTool.Mosaic
            ? ApplyMosaic(regionPixels, clampedWidth, clampedHeight, MosaicBlockSize(scaleX))
            : ApplyBoxBlur(regionPixels, clampedWidth, clampedHeight, BlurRadius(scaleX));

        WriteableBitmap? bitmap = CreateBitmap(clampedWidth, clampedHeight, processed);
        if (bitmap is null)
        {
            return null;
        }

        return new PixelEffect(
            bitmap,
            clampedX / scaleX,
            clampedY / scaleY,
            clampedWidth / scaleX,
            clampedHeight / scaleY);
    }

    private byte[] ExtractRegion(int x, int y, int width, int height)
    {
        byte[] source = _sourcePixels ?? Array.Empty<byte>();
        int sourceStride = _sourceWidth * 4;
        int rowBytes = width * 4;
        byte[] destination = new byte[width * height * 4];
        for (int row = 0; row < height; row++)
        {
            int sourceOffset = ((y + row) * sourceStride) + (x * 4);
            Array.Copy(source, sourceOffset, destination, row * rowBytes, rowBytes);
        }

        return destination;
    }

    private static (int X, int Y, int Width, int Height) ClampRegion(
        int x,
        int y,
        int width,
        int height,
        int sourceWidth,
        int sourceHeight)
    {
        int left = Math.Clamp(x, 0, sourceWidth);
        int top = Math.Clamp(y, 0, sourceHeight);
        int right = Math.Clamp(x + width, 0, sourceWidth);
        int bottom = Math.Clamp(y + height, 0, sourceHeight);
        return (left, top, right - left, bottom - top);
    }

    private static int MosaicBlockSize(double scale)
    {
        double raw = MosaicBlockSizeDip * scale;
        int block = (int)Math.Round(double.IsFinite(raw) && raw > 0 ? raw : MosaicBlockSizeDip);
        return Math.Max(2, block);
    }

    private static int BlurRadius(double scale)
    {
        double raw = BlurRadiusDip * scale;
        int radius = (int)Math.Round(double.IsFinite(raw) && raw > 0 ? raw : BlurRadiusDip);
        return Math.Max(1, radius);
    }

    private static WriteableBitmap? CreateBitmap(int width, int height, byte[] bgra)
    {
        if (width <= 0 || height <= 0 || bgra.Length < width * height * 4)
        {
            return null;
        }

        var bitmap = new WriteableBitmap(width, height);
        using (Stream stream = bitmap.PixelBuffer.AsStream())
        {
            stream.Write(bgra, 0, width * height * 4);
        }

        bitmap.Invalidate();
        return bitmap;
    }

    /// <summary>
    /// Block averaging: every <paramref name="block"/>-sized cell of the
    /// region is replaced by the mean colour of its pixels, which is what makes
    /// the mosaic read as opaque pixelation rather than a smear. Cells are
    /// aligned to the region's own origin, so the same annotation always
    /// produces the same grid on screen and on export.
    /// </summary>
    private static byte[] ApplyMosaic(byte[] source, int width, int height, int block)
    {
        int effectiveBlock = Math.Clamp(block, 2, Math.Max(2, Math.Min(width, height)));
        byte[] destination = new byte[source.Length];
        for (int blockTop = 0; blockTop < height; blockTop += effectiveBlock)
        {
            int blockBottom = Math.Min(blockTop + effectiveBlock, height);
            for (int blockLeft = 0; blockLeft < width; blockLeft += effectiveBlock)
            {
                int blockRight = Math.Min(blockLeft + effectiveBlock, width);
                long sumB = 0;
                long sumG = 0;
                long sumR = 0;
                long sumA = 0;
                int count = 0;
                for (int y = blockTop; y < blockBottom; y++)
                {
                    int rowOffset = y * width * 4;
                    for (int x = blockLeft; x < blockRight; x++)
                    {
                        int index = rowOffset + (x * 4);
                        sumB += source[index];
                        sumG += source[index + 1];
                        sumR += source[index + 2];
                        sumA += source[index + 3];
                        count++;
                    }
                }

                if (count == 0)
                {
                    continue;
                }

                byte averageB = (byte)(sumB / count);
                byte averageG = (byte)(sumG / count);
                byte averageR = (byte)(sumR / count);
                byte averageA = (byte)(sumA / count);
                for (int y = blockTop; y < blockBottom; y++)
                {
                    int rowOffset = y * width * 4;
                    for (int x = blockLeft; x < blockRight; x++)
                    {
                        int index = rowOffset + (x * 4);
                        destination[index] = averageB;
                        destination[index + 1] = averageG;
                        destination[index + 2] = averageR;
                        destination[index + 3] = averageA;
                    }
                }
            }
        }

        return destination;
    }

    /// <summary>
    /// Separable box blur with edge clamping, run twice so the result does not
    /// keep the hard box edges of a single pass. Sliding window sums keep it
    /// O(width * height) per pass regardless of the radius.
    /// </summary>
    private static byte[] ApplyBoxBlur(byte[] source, int width, int height, int radius)
    {
        int effectiveRadius = Math.Clamp(radius, 1, Math.Max(1, Math.Min(width, height) / 2));
        byte[] horizontal = new byte[source.Length];
        byte[] vertical = new byte[source.Length];
        byte[] current = source;
        for (int iteration = 0; iteration < BlurIterations; iteration++)
        {
            BlurHorizontal(current, horizontal, width, height, effectiveRadius);
            BlurVertical(horizontal, vertical, width, height, effectiveRadius);
            current = vertical;
        }

        return current;
    }

    private static void BlurHorizontal(
        byte[] source,
        byte[] destination,
        int width,
        int height,
        int radius)
    {
        int window = (radius * 2) + 1;
        for (int y = 0; y < height; y++)
        {
            int rowOffset = y * width * 4;
            int sumB = 0;
            int sumG = 0;
            int sumR = 0;
            int sumA = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int sample = rowOffset + (Math.Clamp(offset, 0, width - 1) * 4);
                sumB += source[sample];
                sumG += source[sample + 1];
                sumR += source[sample + 2];
                sumA += source[sample + 3];
            }

            for (int x = 0; x < width; x++)
            {
                int index = rowOffset + (x * 4);
                destination[index] = (byte)(sumB / window);
                destination[index + 1] = (byte)(sumG / window);
                destination[index + 2] = (byte)(sumR / window);
                destination[index + 3] = (byte)(sumA / window);

                int outgoing = rowOffset + (Math.Clamp(x - radius, 0, width - 1) * 4);
                int incoming = rowOffset + (Math.Clamp(x + radius + 1, 0, width - 1) * 4);
                sumB += source[incoming] - source[outgoing];
                sumG += source[incoming + 1] - source[outgoing + 1];
                sumR += source[incoming + 2] - source[outgoing + 2];
                sumA += source[incoming + 3] - source[outgoing + 3];
            }
        }
    }

    private static void BlurVertical(
        byte[] source,
        byte[] destination,
        int width,
        int height,
        int radius)
    {
        int window = (radius * 2) + 1;
        int stride = width * 4;
        for (int x = 0; x < width; x++)
        {
            int columnOffset = x * 4;
            int sumB = 0;
            int sumG = 0;
            int sumR = 0;
            int sumA = 0;
            for (int offset = -radius; offset <= radius; offset++)
            {
                int sample = (Math.Clamp(offset, 0, height - 1) * stride) + columnOffset;
                sumB += source[sample];
                sumG += source[sample + 1];
                sumR += source[sample + 2];
                sumA += source[sample + 3];
            }

            for (int y = 0; y < height; y++)
            {
                int index = (y * stride) + columnOffset;
                destination[index] = (byte)(sumB / window);
                destination[index + 1] = (byte)(sumG / window);
                destination[index + 2] = (byte)(sumR / window);
                destination[index + 3] = (byte)(sumA / window);

                int outgoing = (Math.Clamp(y - radius, 0, height - 1) * stride) + columnOffset;
                int incoming = (Math.Clamp(y + radius + 1, 0, height - 1) * stride) + columnOffset;
                sumB += source[incoming] - source[outgoing];
                sumG += source[incoming + 1] - source[outgoing + 1];
                sumR += source[incoming + 2] - source[outgoing + 2];
                sumA += source[incoming + 3] - source[outgoing + 3];
            }
        }
    }

    // ---- canvas rebuild and history -------------------------------------

    private void RebuildCanvas()
    {
        if (_isRebuilding)
        {
            return;
        }

        _isRebuilding = true;
        try
        {
            TextBox? editor = _textEditor;
            AnnotationCanvas.Children.Clear();

            for (int i = 0; i < _annotations.Count; i++)
            {
                UIElement? visual = CreateAnnotationVisual(_annotations[i]);
                if (visual is not null)
                {
                    AnnotationCanvas.Children.Add(visual);
                }
            }

            if (_previewVisual is not null)
            {
                AnnotationCanvas.Children.Add(_previewVisual);
            }

            // The inline editor outlives a rebuild: it is a canvas child by
            // contract, and dropping it would eat the text being typed.
            if (editor is not null && _textEditor is not null)
            {
                AnnotationCanvas.Children.Add(editor);
            }
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] Canvas rebuild failed: {ex.Message}");
        }
        finally
        {
            _isRebuilding = false;
        }
    }

    private IReadOnlyList<ScreenshotAnnotation> SnapshotAnnotations() => _annotations.ToArray();

    private void RestoreAnnotations(IReadOnlyList<ScreenshotAnnotation> snapshot)
    {
        _annotations.Clear();
        for (int i = 0; i < snapshot.Count; i++)
        {
            _annotations.Add(snapshot[i]);
        }
    }

    private void PushUndoSnapshot()
    {
        _undoStack.Push(SnapshotAnnotations());
        _redoStack.Clear();
    }

    private void RaiseAnnotationsChanged()
    {
        try
        {
            AnnotationsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotation] AnnotationsChanged handler failed: {ex.Message}");
        }
    }

    /// <summary>Rasterizes the committed annotations straight into a pixel buffer.</summary>
    internal CapturedFrame RenderToFrame(CapturedFrame baseFrame, double dpiScale) =>
        ScreenshotAnnotationRasterizer.Render(baseFrame, _annotations, dpiScale);
}
