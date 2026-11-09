using DeskBox.Platform;
using DeskBox.Services;
using Windows.Foundation;
using Windows.UI;

namespace DeskBox.Controls;

/// <summary>
/// Software rasterizer for the screenshot annotation model: every committed
/// <see cref="ScreenshotAnnotation"/> is drawn straight into a BGRA pixel
/// buffer, so an annotated export never has to travel through
/// <c>RenderTargetBitmap</c>.
/// </summary>
/// <remarks>
/// The XAML visuals in <see cref="ScreenshotAnnotationLayer"/> are only the
/// interactive feedback; this type is the second consumer of the very same
/// model and owns the exported pixels. It repeats that layer's geometry (same
/// stroke widths, same arrow head, same mosaic grid, same blur) at the pixel
/// level, so the two cannot drift the way a separate drawing code path would.
/// <para>
/// Every length in the model is DIP; every length here is a physical pixel.
/// Points convert once with <c>round(dip * dpiScale)</c>, and stroke widths,
/// mosaic blocks and blur radii are derived from the DIP constants through the
/// same scale, so a capture taken at 125% DPI looks identical - only larger.
/// Nothing is ever written outside the destination buffer.
/// </para>
/// <para>
/// Mosaic and blur sample the pristine base frame (not the partially composed
/// destination), exactly like the on-screen layer, which rebuilds each pixel
/// effect from the untouched source frame. That keeps "what the user sees" and
/// "what gets exported" the same even when a pixel effect overlaps a shape.
/// </para>
/// </remarks>
internal static class ScreenshotAnnotationRasterizer
{
    private const double DefaultStrokeThickness = 3;
    private const double DefaultFontSize = 18;
    private const double MarkerOpacity = 0.35;
    private const double MosaicBlockSizeDip = 8;
    private const double BlurRadiusDip = 5;
    private const int BlurIterations = 2;

    // Arrow head geometry, identical to ScreenshotAnnotationLayer.BuildArrowGeometry
    // except that thickness is already in physical pixels here.
    private const double ArrowHeadThicknessScale = 3.5;
    private const double ArrowHeadMinimumLength = 9;
    private const double ArrowHeadMaximumLength = 28;
    private const double ArrowHeadWidthRatio = 0.62;
    private const double ArrowHeadLengthRatio = 0.6;

    private const int MinimumEllipseSegments = 32;
    private const int MaximumEllipseSegments = 512;


    /// <summary>Draws every committed annotation into a copy of <paramref name="baseFrame"/>.</summary>
    internal static CapturedFrame Render(
        CapturedFrame baseFrame,
        IReadOnlyList<ScreenshotAnnotation> annotations,
        double dpiScale)
    {
        // The copy is the contract: callers keep owning the frame they passed
        // in, and a failure below still hands back pixels, never a null or the
        // caller's own array.
        byte[] working = CopyPixels(baseFrame);
        int width = baseFrame.Width;
        int height = baseFrame.Height;
        try
        {
            if (width <= 0 || height <= 0 || working.Length < (long)width * height * 4)
            {
                return new CapturedFrame(working, width, height);
            }

            if (annotations is null || annotations.Count == 0)
            {
                return new CapturedFrame(working, width, height);
            }

            double scale = double.IsFinite(dpiScale) && dpiScale > 0 ? dpiScale : 1;
            var surface = new Surface(working, baseFrame.Pixels, width, height, scale);
            for (int i = 0; i < annotations.Count; i++)
            {
                ScreenshotAnnotation? annotation = annotations[i];
                if (annotation is null)
                {
                    continue;
                }

                // One malformed annotation must not cost the others: the loop
                // logs and moves on, and the outer catch only covers a failure
                // of the buffer machinery itself.
                try
                {
                    surface.Draw(annotation);
                }
                catch (Exception ex)
                {
                    App.Log(
                        $"[ScreenshotAnnotationRasterizer] {annotation.Kind} annotation {i} failed: {ex.Message}");
                }
            }

            return new CapturedFrame(working, width, height);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotAnnotationRasterizer] Render failed: {ex.Message}");
            // A partially composed buffer is not a usable fallback: return the
            // untouched frame so a failed export is still the screenshot.
            return new CapturedFrame(CopyPixels(baseFrame), width, height);
        }
    }

    private static byte[] CopyPixels(CapturedFrame frame)
    {
        if (frame.Pixels is not { Length: > 0 } pixels)
        {
            return Array.Empty<byte>();
        }

        byte[] copy = new byte[pixels.Length];
        Array.Copy(pixels, copy, pixels.Length);
        return copy;
    }

    /// <summary>
    /// One frame being composed: the destination buffer grows annotation by
    /// annotation while <c>_source</c> stays the pristine capture that the
    /// pixel-effect tools sample.
    /// </summary>
    private sealed class Surface
    {
        private readonly byte[] _destination;
        private readonly byte[] _source;
        private readonly double _scale;

        internal Surface(byte[] destination, byte[] source, int width, int height, double scale)
        {
            _destination = destination;
            _source = source;
            Width = width;
            Height = height;
            _scale = scale;
        }

        internal int Width { get; }

        internal int Height { get; }

        internal void Draw(ScreenshotAnnotation annotation)
        {
            switch (annotation.Kind)
            {
                case ScreenshotAnnotationTool.Rectangle:
                    DrawRectangle(annotation);
                    break;

                case ScreenshotAnnotationTool.Ellipse:
                    DrawEllipse(annotation);
                    break;

                case ScreenshotAnnotationTool.Line:
                    DrawStraightLine(annotation);
                    break;

                case ScreenshotAnnotationTool.Arrow:
                    DrawArrow(annotation);
                    break;

                case ScreenshotAnnotationTool.Pencil:
                    DrawFreehand(annotation, opacity: 1);
                    break;

                case ScreenshotAnnotationTool.Marker:
                    DrawFreehand(annotation, MarkerOpacity);
                    break;

                case ScreenshotAnnotationTool.Mosaic:
                    DrawMosaic(annotation);
                    break;

                case ScreenshotAnnotationTool.Blur:
                    DrawBlur(annotation);
                    break;

                case ScreenshotAnnotationTool.Text:
                    DrawText(annotation);
                    break;

                // Eraser never reaches this model (it removes annotations) and
                // None is not an annotation; both are deliberate no-ops.
                default:
                    break;
            }
        }

        // ---- geometry shared by every shape ------------------------------

        private int ToPixel(double dip) => (int)Math.Round(dip * _scale);

        /// <summary>Half the stroke width in pixels, never thinner than one pixel.</summary>
        private double StrokeHalfWidth(ScreenshotAnnotation annotation)
        {
            double thicknessDip = annotation.Thickness > 0 ? annotation.Thickness : DefaultStrokeThickness;
            double thicknessPixels = Math.Round(thicknessDip * _scale);
            if (!double.IsFinite(thicknessPixels) || thicknessPixels < 1)
            {
                thicknessPixels = 1;
            }

            return thicknessPixels / 2;
        }

        private Point[] ToPixels(IReadOnlyList<Point> points)
        {
            Point[] result = new Point[points.Count];
            for (int i = 0; i < points.Count; i++)
            {
                result[i] = new Point(ToPixel(points[i].X), ToPixel(points[i].Y));
            }

            return result;
        }

        // ---- primitives --------------------------------------------------

        /// <summary>
        /// A thick segment with round caps and a one-pixel soft edge: coverage
        /// falls off linearly over the last pixel of the stroke, which is the
        /// whole antialiasing budget this renderer needs.
        /// </summary>
        private void BlendSegment(
            PixelBox box,
            double x1,
            double y1,
            double x2,
            double y2,
            double halfWidth,
            Color color,
            double opacity)
        {
            if (box.IsEmpty)
            {
                return;
            }

            for (int y = box.Top; y <= box.Bottom; y++)
            {
                double centerY = y + 0.5;
                for (int x = box.Left; x <= box.Right; x++)
                {
                    double coverage = SegmentCoverage(x + 0.5, centerY, x1, y1, x2, y2, halfWidth);
                    if (coverage <= 0)
                    {
                        continue;
                    }

                    BlendPixel(((y * Width) + x) * 4, color, coverage, opacity);
                }
            }
        }

        private void AccumulateSegment(
            byte[] mask,
            PixelBox maskBox,
            int maskWidth,
            double x1,
            double y1,
            double x2,
            double y2,
            double halfWidth)
        {
            PixelBox box = GetBox(x1, y1, x2, y2, halfWidth);
            if (box.IsEmpty)
            {
                return;
            }

            for (int y = box.Top; y <= box.Bottom; y++)
            {
                double centerY = y + 0.5;
                int row = (y - maskBox.Top) * maskWidth;
                for (int x = box.Left; x <= box.Right; x++)
                {
                    double coverage = SegmentCoverage(x + 0.5, centerY, x1, y1, x2, y2, halfWidth);
                    if (coverage <= 0)
                    {
                        continue;
                    }

                    int index = row + (x - maskBox.Left);
                    byte value = (byte)Math.Round(coverage * 255);
                    if (value > mask[index])
                    {
                        mask[index] = value;
                    }
                }
            }
        }

        private static double SegmentCoverage(
            double pointX,
            double pointY,
            double x1,
            double y1,
            double x2,
            double y2,
            double halfWidth)
        {
            double dx = x2 - x1;
            double dy = y2 - y1;
            double lengthSquared = (dx * dx) + (dy * dy);
            double t = 0;
            if (lengthSquared > double.Epsilon)
            {
                t = (((pointX - x1) * dx) + ((pointY - y1) * dy)) / lengthSquared;
                t = t < 0 ? 0 : t > 1 ? 1 : t;
            }

            double offsetX = pointX - (x1 + (t * dx));
            double offsetY = pointY - (y1 + (t * dy));
            double distance = Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
            double coverage = halfWidth + 0.5 - distance;
            return coverage <= 0 ? 0 : coverage >= 1 ? 1 : coverage;
        }

        /// <summary>
        /// Pixel box (inclusive) that a segment of the given half width can
        /// touch, already clamped into the frame - every drawing loop below
        /// iterates a box like this, which is what keeps writes in bounds.
        /// </summary>
        private PixelBox GetBox(double x1, double y1, double x2, double y2, double halfWidth)
        {
            double reach = halfWidth + 1;
            return ClampBox(
                Math.Min(x1, x2) - reach,
                Math.Min(y1, y2) - reach,
                Math.Max(x1, x2) + reach,
                Math.Max(y1, y2) + reach);
        }

        private PixelBox ClampBox(double left, double top, double right, double bottom)
        {
            int clampedLeft = (int)Math.Floor(left);
            int clampedTop = (int)Math.Floor(top);
            int clampedRight = (int)Math.Ceiling(right);
            int clampedBottom = (int)Math.Ceiling(bottom);
            return new PixelBox(
                Math.Max(0, clampedLeft),
                Math.Max(0, clampedTop),
                Math.Min(Width - 1, clampedRight),
                Math.Min(Height - 1, clampedBottom));
        }

        /// <summary>Source-over blend of one pixel, honouring the destination alpha.</summary>
        private void BlendPixel(int offset, Color color, double coverage, double opacity)
        {
            double sourceAlpha = (color.A / 255.0) * coverage * opacity;
            if (sourceAlpha <= 0)
            {
                return;
            }

            if (sourceAlpha >= 1)
            {
                _destination[offset] = color.B;
                _destination[offset + 1] = color.G;
                _destination[offset + 2] = color.R;
                _destination[offset + 3] = 0xFF;
                return;
            }

            double destinationAlpha = _destination[offset + 3] / 255.0;
            double inverse = 1 - sourceAlpha;
            double resultAlpha = sourceAlpha + (destinationAlpha * inverse);
            if (resultAlpha <= 0)
            {
                _destination[offset] = 0;
                _destination[offset + 1] = 0;
                _destination[offset + 2] = 0;
                _destination[offset + 3] = 0;
                return;
            }

            double sourceWeight = sourceAlpha / resultAlpha;
            double destinationWeight = destinationAlpha * inverse / resultAlpha;
            _destination[offset] = ToByte((_destination[offset] * destinationWeight) + (color.B * sourceWeight));
            _destination[offset + 1] = ToByte((_destination[offset + 1] * destinationWeight) + (color.G * sourceWeight));
            _destination[offset + 2] = ToByte((_destination[offset + 2] * destinationWeight) + (color.R * sourceWeight));
            _destination[offset + 3] = ToByte(resultAlpha * 255);
        }

        private static byte ToByte(double value) =>
            value <= 0 ? (byte)0 : value >= 255 ? (byte)255 : (byte)(value + 0.5);

        // ---- shapes ------------------------------------------------------

        /// <summary>
        /// Four thick segments along the rectangle's own edge path, which is
        /// where the XAML stroke is centred; the round caps reproduce its
        /// rounded corners.
        /// </summary>
        private void DrawRectangle(ScreenshotAnnotation annotation)
        {
            Rect bounds = annotation.GetPointBounds();
            int left = ToPixel(bounds.X);
            int top = ToPixel(bounds.Y);
            int right = ToPixel(bounds.X + bounds.Width);
            int bottom = ToPixel(bounds.Y + bounds.Height);
            if (right < left)
            {
                (left, right) = (right, left);
            }

            if (bottom < top)
            {
                (top, bottom) = (bottom, top);
            }

            if (right == left && bottom == top)
            {
                return;
            }

            double half = StrokeHalfWidth(annotation);
            Color color = annotation.Color;
            PixelBox topEdge = GetBox(left, top, right, top, half);
            BlendSegment(topEdge, left, top, right, top, half, color, 1);
            PixelBox rightEdge = GetBox(right, top, right, bottom, half);
            BlendSegment(rightEdge, right, top, right, bottom, half, color, 1);
            PixelBox bottomEdge = GetBox(right, bottom, left, bottom, half);
            BlendSegment(bottomEdge, right, bottom, left, bottom, half, color, 1);
            PixelBox leftEdge = GetBox(left, bottom, left, top, half);
            BlendSegment(leftEdge, left, bottom, left, top, half, color, 1);
        }

        /// <summary>
        /// The ellipse outline is sampled into a closed polyline and stroked
        /// segment by segment: with a segment every pixel or so the joins are
        /// invisible and no conic rasterization is needed.
        /// </summary>
        private void DrawEllipse(ScreenshotAnnotation annotation)
        {
            Rect bounds = annotation.GetPointBounds();
            int left = ToPixel(bounds.X);
            int top = ToPixel(bounds.Y);
            int right = ToPixel(bounds.X + bounds.Width);
            int bottom = ToPixel(bounds.Y + bounds.Height);
            if (right < left)
            {
                (left, right) = (right, left);
            }

            if (bottom < top)
            {
                (top, bottom) = (bottom, top);
            }

            double centerX = (left + right) / 2.0;
            double centerY = (top + bottom) / 2.0;
            double radiusX = (right - left) / 2.0;
            double radiusY = (bottom - top) / 2.0;
            if (radiusX <= 0 && radiusY <= 0)
            {
                return;
            }

            double perimeter = Math.PI * (radiusX + radiusY);
            int segments = (int)Math.Clamp(
                Math.Ceiling(perimeter),
                MinimumEllipseSegments,
                MaximumEllipseSegments);
            Point[] points = new Point[segments + 1];
            for (int i = 0; i <= segments; i++)
            {
                double angle = 2 * Math.PI * i / segments;
                points[i] = new Point(
                    centerX + (radiusX * Math.Cos(angle)),
                    centerY + (radiusY * Math.Sin(angle)));
            }

            DrawPolyline(points, StrokeHalfWidth(annotation), annotation.Color, 1);
        }

        private void DrawStraightLine(ScreenshotAnnotation annotation)
        {
            if (annotation.PointCount < 2)
            {
                return;
            }

            Point start = ToPixelPoint(annotation.Points[0]);
            Point end = ToPixelPoint(annotation.Points[1]);
            double half = StrokeHalfWidth(annotation);
            BlendSegment(
                GetBox(start.X, start.Y, end.X, end.Y, half),
                start.X,
                start.Y,
                end.X,
                end.Y,
                half,
                annotation.Color,
                1);
        }

        /// <summary>
        /// Shaft plus a filled head, using the same head length/width rules as
        /// the XAML geometry builder. The head is filled (point-in-triangle)
        /// instead of stroked: at the sizes the clamp allows, the filled head
        /// covers the stroked one.
        /// </summary>
        private void DrawArrow(ScreenshotAnnotation annotation)
        {
            if (annotation.PointCount < 2)
            {
                return;
            }

            Point start = ToPixelPoint(annotation.Points[0]);
            Point end = ToPixelPoint(annotation.Points[1]);
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                return;
            }

            double half = StrokeHalfWidth(annotation);
            double thickness = half * 2;
            double unitX = dx / length;
            double unitY = dy / length;
            double headLength = Math.Clamp(
                thickness * ArrowHeadThicknessScale,
                ArrowHeadMinimumLength,
                ArrowHeadMaximumLength);
            if (headLength > length * ArrowHeadLengthRatio)
            {
                headLength = length * ArrowHeadLengthRatio;
            }

            double headWidth = headLength * ArrowHeadWidthRatio;
            var basePoint = new Point(end.X - (unitX * headLength), end.Y - (unitY * headLength));
            double perpendicularX = -unitY;
            double perpendicularY = unitX;
            var leftPoint = new Point(
                basePoint.X + (perpendicularX * headWidth / 2),
                basePoint.Y + (perpendicularY * headWidth / 2));
            var rightPoint = new Point(
                basePoint.X - (perpendicularX * headWidth / 2),
                basePoint.Y - (perpendicularY * headWidth / 2));

            Color color = annotation.Color;
            BlendSegment(
                GetBox(start.X, start.Y, basePoint.X, basePoint.Y, half),
                start.X,
                start.Y,
                basePoint.X,
                basePoint.Y,
                half,
                color,
                1);
            FillTriangle(end, leftPoint, rightPoint, color);
        }

        /// <summary>
        /// Convex triangle fill with a one-pixel soft edge: the coverage is the
        /// smallest signed distance to the three edges, so the result is a
        /// filled head rather than a stroked outline.
        /// </summary>
        private void FillTriangle(Point a, Point b, Point c, Color color)
        {
            double orientation = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
            if (Math.Abs(orientation) < 0.001)
            {
                return;
            }

            // SignedEdgeDistance is positive on the side the cross product
            // points away from, so the triangle's own winding has to be
            // negated for "inside" to come out positive on every edge.
            double sign = orientation > 0 ? -1 : 1;
            PixelBox box = ClampBox(
                Math.Min(a.X, Math.Min(b.X, c.X)) - 1,
                Math.Min(a.Y, Math.Min(b.Y, c.Y)) - 1,
                Math.Max(a.X, Math.Max(b.X, c.X)) + 1,
                Math.Max(a.Y, Math.Max(b.Y, c.Y)) + 1);
            if (box.IsEmpty)
            {
                return;
            }

            for (int y = box.Top; y <= box.Bottom; y++)
            {
                double centerY = y + 0.5;
                for (int x = box.Left; x <= box.Right; x++)
                {
                    double centerX = x + 0.5;
                    double edgeA = sign * SignedEdgeDistance(centerX, centerY, a, b);
                    double edgeB = sign * SignedEdgeDistance(centerX, centerY, b, c);
                    double edgeC = sign * SignedEdgeDistance(centerX, centerY, c, a);
                    double inside = Math.Min(edgeA, Math.Min(edgeB, edgeC)) + 0.5;
                    if (inside <= 0)
                    {
                        continue;
                    }

                    BlendPixel(
                        ((y * Width) + x) * 4,
                        color,
                        inside >= 1 ? 1 : inside,
                        1);
                }
            }
        }

        private static double SignedEdgeDistance(double pointX, double pointY, Point from, Point to)
        {
            double dx = to.X - from.X;
            double dy = to.Y - from.Y;
            double length = Math.Sqrt((dx * dx) + (dy * dy));
            if (length < 0.001)
            {
                return 0;
            }

            return (((pointX - from.X) * dy) - ((pointY - from.Y) * dx)) / length;
        }

        /// <summary>
        /// Pencil and marker. An opaque stroke blends segment by segment, which
        /// is idempotent where segments overlap; a translucent one first builds
        /// a coverage mask so the overlaps of one stroke cannot stack into a
        /// darker joint than the single translucent layer the XAML polyline
        /// paints.
        /// </summary>
        private void DrawFreehand(ScreenshotAnnotation annotation, double opacity)
        {
            Point[] points = ToPixels(annotation.Points);
            if (points.Length == 0)
            {
                return;
            }

            double half = StrokeHalfWidth(annotation);
            Color color = annotation.Color;
            if (points.Length == 1)
            {
                Point dot = points[0];
                BlendSegment(
                    GetBox(dot.X, dot.Y, dot.X, dot.Y, half),
                    dot.X,
                    dot.Y,
                    dot.X,
                    dot.Y,
                    half,
                    color,
                    opacity);
                return;
            }

            if (opacity >= 1)
            {
                for (int i = 1; i < points.Length; i++)
                {
                    Point from = points[i - 1];
                    Point to = points[i];
                    BlendSegment(
                        GetBox(from.X, from.Y, to.X, to.Y, half),
                        from.X,
                        from.Y,
                        to.X,
                        to.Y,
                        half,
                        color,
                        1);
                }

                return;
            }

            double minX = points[0].X;
            double minY = points[0].Y;
            double maxX = minX;
            double maxY = minY;
            for (int i = 1; i < points.Length; i++)
            {
                Point point = points[i];
                if (point.X < minX)
                {
                    minX = point.X;
                }

                if (point.Y < minY)
                {
                    minY = point.Y;
                }

                if (point.X > maxX)
                {
                    maxX = point.X;
                }

                if (point.Y > maxY)
                {
                    maxY = point.Y;
                }
            }

            double reach = half + 1;
            PixelBox maskBox = ClampBox(minX - reach, minY - reach, maxX + reach, maxY + reach);
            if (maskBox.IsEmpty)
            {
                return;
            }

            int maskWidth = maskBox.Right - maskBox.Left + 1;
            int maskHeight = maskBox.Bottom - maskBox.Top + 1;
            // One byte per pixel: a full-frame marker stroke on a 4K capture
            // must not need a 66 MB double buffer for its coverage mask.
            byte[] mask = new byte[maskWidth * maskHeight];
            for (int i = 1; i < points.Length; i++)
            {
                Point from = points[i - 1];
                Point to = points[i];
                AccumulateSegment(mask, maskBox, maskWidth, from.X, from.Y, to.X, to.Y, half);
            }

            for (int y = 0; y < maskHeight; y++)
            {
                int row = y * maskWidth;
                int targetRow = ((maskBox.Top + y) * Width) + maskBox.Left;
                for (int x = 0; x < maskWidth; x++)
                {
                    byte maskValue = mask[row + x];
                    if (maskValue == 0)
                    {
                        continue;
                    }

                    BlendPixel((targetRow + x) * 4, color, maskValue / 255.0, opacity);
                }
            }
        }

        private void DrawPolyline(
            IReadOnlyList<Point> points,
            double halfWidth,
            Color color,
            double opacity)
        {
            if (points.Count == 0)
            {
                return;
            }

            if (points.Count == 1)
            {
                Point dot = points[0];
                BlendSegment(
                    GetBox(dot.X, dot.Y, dot.X, dot.Y, halfWidth),
                    dot.X,
                    dot.Y,
                    dot.X,
                    dot.Y,
                    halfWidth,
                    color,
                    opacity);
                return;
            }

            for (int i = 1; i < points.Count; i++)
            {
                Point from = points[i - 1];
                Point to = points[i];
                BlendSegment(
                    GetBox(from.X, from.Y, to.X, to.Y, halfWidth),
                    from.X,
                    from.Y,
                    to.X,
                    to.Y,
                    halfWidth,
                    color,
                    opacity);
            }
        }

        private Point ToPixelPoint(Point point) => new(ToPixel(point.X), ToPixel(point.Y));

        // ---- pixel effects ----------------------------------------------

        /// <summary>
        /// Block averaging over the dragged region, with the block grid aligned
        /// to the region origin so the same annotation always produces the same
        /// cells on screen and on export.
        /// </summary>
        private void DrawMosaic(ScreenshotAnnotation annotation)
        {
            if (!TryGetPixelRegion(annotation, out PixelBox region))
            {
                return;
            }

            int width = region.Right - region.Left + 1;
            int height = region.Bottom - region.Top + 1;
            int block = Math.Max(2, (int)Math.Round(MosaicBlockSizeDip * _scale));
            block = Math.Clamp(block, 2, Math.Max(2, Math.Min(width, height)));
            for (int blockTop = 0; blockTop < height; blockTop += block)
            {
                int blockBottom = Math.Min(blockTop + block, height);
                for (int blockLeft = 0; blockLeft < width; blockLeft += block)
                {
                    int blockRight = Math.Min(blockLeft + block, width);
                    long sumB = 0;
                    long sumG = 0;
                    long sumR = 0;
                    long sumA = 0;
                    int count = 0;
                    for (int y = blockTop; y < blockBottom; y++)
                    {
                        int sourceRow = (((region.Top + y) * Width) + region.Left) * 4;
                        for (int x = blockLeft; x < blockRight; x++)
                        {
                            int index = sourceRow + (x * 4);
                            sumB += _source[index];
                            sumG += _source[index + 1];
                            sumR += _source[index + 2];
                            sumA += _source[index + 3];
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
                        int destinationRow = (((region.Top + y) * Width) + region.Left) * 4;
                        for (int x = blockLeft; x < blockRight; x++)
                        {
                            int index = destinationRow + (x * 4);
                            _destination[index] = averageB;
                            _destination[index + 1] = averageG;
                            _destination[index + 2] = averageR;
                            _destination[index + 3] = averageA;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Two-pass separable box blur over the dragged region, radius in
        /// physical pixels and clamped to the region so a thin selection still
        /// works instead of throwing out of <see cref="Math.Clamp(int, int, int)"/>.
        /// </summary>
        private void DrawBlur(ScreenshotAnnotation annotation)
        {
            if (!TryGetPixelRegion(annotation, out PixelBox region))
            {
                return;
            }

            int width = region.Right - region.Left + 1;
            int height = region.Bottom - region.Top + 1;
            int radius = Math.Max(1, (int)Math.Round(BlurRadiusDip * _scale));
            radius = Math.Clamp(radius, 1, Math.Max(1, Math.Min(width, height) / 2));
            byte[] buffer = ExtractRegion(region, width, height);
            BoxBlur(buffer, width, height, radius);
            WriteRegion(region, width, height, buffer);
        }

        private bool TryGetPixelRegion(ScreenshotAnnotation annotation, out PixelBox region)
        {
            Rect bounds = annotation.GetPointBounds();
            int left = ToPixel(bounds.X);
            int top = ToPixel(bounds.Y);
            int right = ToPixel(bounds.X + bounds.Width) - 1;
            int bottom = ToPixel(bounds.Y + bounds.Height) - 1;
            if (right < left)
            {
                (left, right) = (right, left);
            }

            if (bottom < top)
            {
                (top, bottom) = (bottom, top);
            }

            region = new PixelBox(
                Math.Max(0, left),
                Math.Max(0, top),
                Math.Min(Width - 1, right),
                Math.Min(Height - 1, bottom));
            return !region.IsEmpty;
        }

        private byte[] ExtractRegion(PixelBox region, int width, int height)
        {
            byte[] buffer = new byte[width * height * 4];
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                int sourceOffset = (((region.Top + y) * Width) + region.Left) * 4;
                Array.Copy(_source, sourceOffset, buffer, y * rowBytes, rowBytes);
            }

            return buffer;
        }

        private void WriteRegion(PixelBox region, int width, int height, byte[] buffer)
        {
            int rowBytes = width * 4;
            for (int y = 0; y < height; y++)
            {
                int destinationOffset = (((region.Top + y) * Width) + region.Left) * 4;
                Array.Copy(buffer, y * rowBytes, _destination, destinationOffset, rowBytes);
            }
        }

        // ---- text --------------------------------------------------------

        /// <summary>
        /// Text is the one primitive GDI still draws better than anything
        /// hand-rolled here, so it renders into a DIB that covers only the text
        /// box and the resulting coverage is blended back with the annotation
        /// colour (see <see cref="ScreenshotTextRasterizer"/> for the alpha trick).
        /// </summary>
        private void DrawText(ScreenshotAnnotation annotation)
        {
            string text = annotation.Text;
            if (string.IsNullOrEmpty(text))
            {
                return;
            }

            Color color = annotation.Color;
            if (color.A == 0)
            {
                return;
            }

            double fontSizeDip = annotation.FontSize > 0 ? annotation.FontSize : DefaultFontSize;
            int fontPixels = Math.Max(1, (int)Math.Round(fontSizeDip * _scale));
            if (!ScreenshotTextRasterizer.TryRender(text, fontPixels, out byte[] coverage, out int textWidth, out int textHeight))
            {
                return;
            }

            int originX = ToPixel(annotation.FirstPoint.X) - ScreenshotTextRasterizer.Padding;
            int originY = ToPixel(annotation.FirstPoint.Y) - ScreenshotTextRasterizer.Padding;
            for (int y = 0; y < textHeight; y++)
            {
                int targetY = originY + y;
                if (targetY < 0 || targetY >= Height)
                {
                    continue;
                }

                int row = y * textWidth;
                int targetRow = targetY * Width;
                for (int x = 0; x < textWidth; x++)
                {
                    byte alpha = coverage[row + x];
                    if (alpha == 0)
                    {
                        continue;
                    }

                    int targetX = originX + x;
                    if (targetX < 0 || targetX >= Width)
                    {
                        continue;
                    }

                    BlendPixel((targetRow + targetX) * 4, color, alpha / 255.0, 1);
                }
            }
        }
    }

    // ---- pixel effects maths (no Win2D anywhere in this repository) ------

    /// <summary>
    /// Separable box blur with edge clamping, run twice so the result does not
    /// keep the hard edges of a single pass. Sliding window sums keep it
    /// O(width * height) per pass regardless of the radius.
    /// </summary>
    private static void BoxBlur(byte[] pixels, int width, int height, int radius)
    {
        if (width <= 0 || height <= 0 || radius <= 0)
        {
            return;
        }

        byte[] horizontal = new byte[pixels.Length];
        byte[] vertical = new byte[pixels.Length];
        byte[] current = pixels;
        for (int iteration = 0; iteration < BlurIterations; iteration++)
        {
            BlurHorizontal(current, horizontal, width, height, radius);
            BlurVertical(horizontal, vertical, width, height, radius);
            current = vertical;
        }

        if (!ReferenceEquals(current, pixels))
        {
            Array.Copy(current, pixels, pixels.Length);
        }
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

    /// <summary>Inclusive pixel box; <see cref="IsEmpty"/> means it was clipped away.</summary>
    private readonly record struct PixelBox(int Left, int Top, int Right, int Bottom)
    {
        internal bool IsEmpty => Right < Left || Bottom < Top;
    }
}
