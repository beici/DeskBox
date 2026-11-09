using Windows.Foundation;

namespace DeskBox.Services;

internal readonly record struct ScreenshotSelectionTarget(nint Window, Rect Bounds);

/// <summary>Hit tests the window order captured with the frozen screen.</summary>
internal static class ScreenshotSelectionSnapshot
{
    internal static ScreenshotSelectionTarget FindAt(
        IReadOnlyList<ScreenshotSelectionTarget> targets, Point point, Rect monitor)
    {
        foreach (ScreenshotSelectionTarget target in targets)
        {
            Rect bounds = target.Bounds;
            double left = Math.Max(bounds.Left, monitor.Left);
            double top = Math.Max(bounds.Top, monitor.Top);
            double right = Math.Min(bounds.Right, monitor.Right);
            double bottom = Math.Min(bounds.Bottom, monitor.Bottom);
            if (right > left && bottom > top &&
                point.X >= left && point.X < right && point.Y >= top && point.Y < bottom)
            {
                return new(target.Window, new Rect(left, top, right - left, bottom - top));
            }
        }

        return new(0, monitor);
    }
}
