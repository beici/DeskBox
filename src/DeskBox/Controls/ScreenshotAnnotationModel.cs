using Windows.Foundation;

namespace DeskBox.Controls;

/// <summary>
/// One committed annotation. This list is the only source of truth for what
/// the layer shows and for what
/// <see cref="ScreenshotAnnotationLayer.BuildExportVisual"/> rebuilds, so the
/// on-screen canvas and the exported visual can never drift apart.
/// <para>
/// Coordinates are DIP relative to the selection's top-left corner. An
/// instance is treated as immutable once it is committed: the undo history
/// snapshots the annotation list by reference instead of deep-copying every
/// recorded point set.
/// </para>
/// </summary>
internal sealed class ScreenshotAnnotation
{
    private readonly List<Point> _points = [];

    public ScreenshotAnnotation(ScreenshotAnnotationTool kind)
    {
        Kind = kind;
    }

    public ScreenshotAnnotationTool Kind { get; }

    public Windows.UI.Color Color { get; init; }

    public double Thickness { get; init; }

    public string Text { get; init; } = string.Empty;

    public double FontSize { get; init; }

    /// <summary>Measured text extent in DIP; only meaningful for text annotations.</summary>
    public Size TextSize { get; init; }

    public IReadOnlyList<Point> Points => _points;

    public int PointCount => _points.Count;

    public Point FirstPoint => _points.Count > 0 ? _points[0] : default;

    public void AddPoint(Point point) => _points.Add(point);

    public void AddPoints(IReadOnlyList<Point> points)
    {
        for (int i = 0; i < points.Count; i++)
        {
            _points.Add(points[i]);
        }
    }

    /// <summary>Axis-aligned extent of the recorded points (empty for text).</summary>
    public Rect GetPointBounds()
    {
        if (_points.Count == 0)
        {
            return default;
        }

        double left = _points[0].X;
        double top = _points[0].Y;
        double right = left;
        double bottom = top;
        for (int i = 1; i < _points.Count; i++)
        {
            Point point = _points[i];
            if (point.X < left)
            {
                left = point.X;
            }

            if (point.Y < top)
            {
                top = point.Y;
            }

            if (point.X > right)
            {
                right = point.X;
            }

            if (point.Y > bottom)
            {
                bottom = point.Y;
            }
        }

        return new Rect(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// The area the annotation covers, in DIP: the point extent for shapes and
    /// freehand strokes, the measured text box for text, and the dragged
    /// rectangle for the pixel-effect tools.
    /// </summary>
    public Rect GetCoveredBounds()
    {
        if (Kind != ScreenshotAnnotationTool.Text)
        {
            return GetPointBounds();
        }

        Point origin = FirstPoint;
        double width = TextSize.Width > 0 ? TextSize.Width : 0;
        double height = TextSize.Height > 0 ? TextSize.Height : 0;
        return new Rect(origin.X, origin.Y, width, height);
    }
}
