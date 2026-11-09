namespace DeskBox.Services;

[Flags]
internal enum WidgetPanoramaPlacementChanges
{
    None = 0,
    Width = 1,
    Height = 2,
    Left = 4,
    Top = 8,
    Size = Width | Height,
    Translation = Left | Top,
    All = Size | Translation
}

/// <summary>
/// Separates the panorama's fitted size from its render-only translation.
/// Moving a window must not invalidate the oversized image's layout.
/// </summary>
internal sealed class WidgetPanoramaPlacementState
{
    private double _width = double.NaN;
    private double _height = double.NaN;
    private double _left = double.NaN;
    private double _top = double.NaN;

    public WidgetPanoramaPlacementChanges Update(double width, double height, double left, double top)
    {
        // A monitor or source can disappear while a window is transitioning.
        // Keep the last valid placement until the next complete geometry sample.
        if (!double.IsFinite(width) || width <= 0 ||
            !double.IsFinite(height) || height <= 0 ||
            !double.IsFinite(left) || !double.IsFinite(top))
        {
            return WidgetPanoramaPlacementChanges.None;
        }

        WidgetPanoramaPlacementChanges changes = WidgetPanoramaPlacementChanges.None;
        if (_width != width) changes |= WidgetPanoramaPlacementChanges.Width;
        if (_height != height) changes |= WidgetPanoramaPlacementChanges.Height;
        if (_left != left) changes |= WidgetPanoramaPlacementChanges.Left;
        if (_top != top) changes |= WidgetPanoramaPlacementChanges.Top;
        _width = width;
        _height = height;
        _left = left;
        _top = top;
        return changes;
    }

    public void Reset()
    {
        _width = _height = _left = _top = double.NaN;
    }
}
