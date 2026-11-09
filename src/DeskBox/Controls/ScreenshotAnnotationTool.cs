namespace DeskBox.Controls;

/// <summary>
/// The drawing tool the annotation layer is currently armed with.
/// <see cref="None"/> leaves the layer pointer-transparent so the capture
/// overlay keeps receiving its own selection gestures.
/// </summary>
public enum ScreenshotAnnotationTool
{
    None,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pencil,
    Marker,
    Text,
    Mosaic,
    Blur,
    Eraser,
}
