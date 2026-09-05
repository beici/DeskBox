// Copyright (c) DeskBox. All rights reserved.

using Windows.Graphics;

namespace DeskBox.Services;

/// <summary>The four sides a margin can be measured on.</summary>
public enum WidgetMarginSide
{
    Left,
    Top,
    Right,
    Bottom
}

/// <summary>What a resolved margin is measured against.</summary>
public enum WidgetMarginReferenceKind
{
    /// <summary>Nothing borders this side; the monitor work area is used.</summary>
    WorkArea,

    /// <summary>Another DeskBox widget.</summary>
    Widget,

    /// <summary>A desktop icon, shortcut or folder.</summary>
    DesktopIcon
}

/// <summary>One candidate neighbour in physical screen pixels.</summary>
public readonly record struct WidgetMarginNeighbour(
    RectInt32 Bounds,
    WidgetMarginReferenceKind Kind);

/// <summary>
/// The boundary coordinate a side is measured from, plus what produced it.
/// </summary>
public readonly record struct WidgetMarginReference(
    int Boundary,
    WidgetMarginReferenceKind Kind);

/// <summary>Resolved distance for one side.</summary>
public readonly record struct WidgetMarginEdge(
    int Distance,
    WidgetMarginReferenceKind Kind);

/// <summary>
/// Pure geometry for "how far is this widget from the closest thing next to
/// it on each side".
/// <para>
/// The reference is deliberately the nearest *object* - another widget, or a
/// desktop icon/folder - and only falls back to the monitor work area when a
/// side has nothing beside it. Measuring against the screen edge instead
/// produces numbers users cannot act on: moving an unrelated icon changes what
/// "20px on the left" looks like, while the gap the eye actually judges is the
/// one to the neighbour.
/// </para>
/// <para>
/// Everything here is physical screen pixels, matching Win32 window rects and
/// list-view item rects. The perpendicular-overlap tolerance is passed in so it
/// can be scaled for the widget's monitor instead of being a raw constant.
/// </para>
/// </summary>
public static class WidgetMarginReferenceCalculator
{
    /// <summary>
    /// How much perpendicular overlap two rectangles need before they count as
    /// "beside" each other, in device-independent pixels. Without it, a widget
    /// whose corner merely touches another one's row would treat it as a
    /// neighbour.
    /// </summary>
    public const double ParallelOverlapToleranceDips = 8;

    public static int ResolveParallelOverlapTolerance(double dpiScale)
    {
        double scale = dpiScale > 0 ? dpiScale : 1;
        return Math.Max(1, (int)Math.Round(ParallelOverlapToleranceDips * scale));
    }

    public static WidgetMarginReference ResolveSide(
        RectInt32 bounds,
        WidgetMarginSide side,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        RectInt32 workArea,
        int parallelOverlapTolerance)
    {
        ArgumentNullException.ThrowIfNull(neighbours);

        int tolerance = Math.Max(0, parallelOverlapTolerance);
        int boundary = side switch
        {
            WidgetMarginSide.Left => workArea.X,
            WidgetMarginSide.Top => workArea.Y,
            WidgetMarginSide.Right => workArea.X + workArea.Width,
            _ => workArea.Y + workArea.Height
        };
        WidgetMarginReferenceKind kind = WidgetMarginReferenceKind.WorkArea;

        foreach (WidgetMarginNeighbour neighbour in neighbours)
        {
            if (!TryResolveNeighbourBoundary(
                    bounds,
                    side,
                    neighbour.Bounds,
                    tolerance,
                    out int candidate))
            {
                continue;
            }

            bool closer = side is WidgetMarginSide.Left or WidgetMarginSide.Top
                ? candidate > boundary
                : candidate < boundary;
            if (!closer)
            {
                continue;
            }

            boundary = candidate;
            kind = neighbour.Kind;
        }

        return new WidgetMarginReference(boundary, kind);
    }

    public static WidgetMarginEdge ResolveMargin(
        RectInt32 bounds,
        WidgetMarginSide side,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        RectInt32 workArea,
        int parallelOverlapTolerance)
    {
        WidgetMarginReference reference = ResolveSide(
            bounds,
            side,
            neighbours,
            workArea,
            parallelOverlapTolerance);
        int distance = side switch
        {
            WidgetMarginSide.Left => bounds.X - reference.Boundary,
            WidgetMarginSide.Top => bounds.Y - reference.Boundary,
            WidgetMarginSide.Right => reference.Boundary - (bounds.X + bounds.Width),
            _ => reference.Boundary - (bounds.Y + bounds.Height)
        };
        return new WidgetMarginEdge(Math.Max(0, distance), reference.Kind);
    }

    /// <summary>
    /// The window origin that puts <paramref name="side"/> at
    /// <paramref name="margin"/> from its reference boundary. Returns null when
    /// the window already sits there.
    /// </summary>
    public static PointInt32? ResolveShiftedOrigin(
        RectInt32 bounds,
        WidgetMarginSide side,
        int margin,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        RectInt32 workArea,
        int parallelOverlapTolerance)
    {
        int boundary = ResolveSide(
            bounds,
            side,
            neighbours,
            workArea,
            parallelOverlapTolerance).Boundary;
        int x = bounds.X;
        int y = bounds.Y;
        switch (side)
        {
            case WidgetMarginSide.Left:
                x = boundary + margin;
                break;
            case WidgetMarginSide.Top:
                y = boundary + margin;
                break;
            case WidgetMarginSide.Right:
                x = boundary - margin - bounds.Width;
                break;
            default:
                y = boundary - margin - bounds.Height;
                break;
        }

        return x == bounds.X && y == bounds.Y
            ? null
            : new PointInt32(x, y);
    }

    private static bool TryResolveNeighbourBoundary(
        RectInt32 bounds,
        WidgetMarginSide side,
        RectInt32 other,
        int tolerance,
        out int boundary)
    {
        boundary = 0;
        int right = bounds.X + bounds.Width;
        int bottom = bounds.Y + bounds.Height;
        int otherRight = other.X + other.Width;
        int otherBottom = other.Y + other.Height;
        bool overlapsVertically =
            other.Y < bottom - tolerance && otherBottom > bounds.Y + tolerance;
        bool overlapsHorizontally =
            other.X < right - tolerance && otherRight > bounds.X + tolerance;

        switch (side)
        {
            case WidgetMarginSide.Left
                when overlapsVertically && otherRight <= bounds.X + tolerance:
                boundary = otherRight;
                return true;
            case WidgetMarginSide.Right
                when overlapsVertically && other.X >= right - tolerance:
                boundary = other.X;
                return true;
            case WidgetMarginSide.Top
                when overlapsHorizontally && otherBottom <= bounds.Y + tolerance:
                boundary = otherBottom;
                return true;
            case WidgetMarginSide.Bottom
                when overlapsHorizontally && other.Y >= bottom - tolerance:
                boundary = other.Y;
                return true;
            default:
                return false;
        }
    }
}
