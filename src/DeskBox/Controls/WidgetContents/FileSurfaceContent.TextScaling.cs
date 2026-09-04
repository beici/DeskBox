using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class FileSurfaceContent
{
    private const double LayoutSlackPixels = 1;

    private void FileSurfaceContent_TextScaleLoaded(
        object sender,
        RoutedEventArgs e)
    {
        WindowsCompatibilityService.TextScaleFactorChanged -=
            FileSurfaceContent_TextScaleFactorChanged;
        WindowsCompatibilityService.TextScaleFactorChanged +=
            FileSurfaceContent_TextScaleFactorChanged;
        ViewModel.PropertyChanged -= FileSurfaceContent_LayoutPropertyChanged;
        ViewModel.PropertyChanged += FileSurfaceContent_LayoutPropertyChanged;
        ItemsGrid.SizeChanged -= ItemsGrid_SizeChanged;
        ItemsGrid.SizeChanged += ItemsGrid_SizeChanged;
        RefreshFileTextScaleFactor();
        ApplyUniformIconCellSize();
    }

    private void FileSurfaceContent_TextScaleUnloaded(
        object sender,
        RoutedEventArgs e)
    {
        WindowsCompatibilityService.TextScaleFactorChanged -=
            FileSurfaceContent_TextScaleFactorChanged;
        ViewModel.PropertyChanged -= FileSurfaceContent_LayoutPropertyChanged;
        ItemsGrid.SizeChanged -= ItemsGrid_SizeChanged;
    }

    private void ItemsGrid_SizeChanged(object sender, SizeChangedEventArgs e) =>
        ApplyUniformIconCellSize();

    private void ItemsGrid_UniformPanelLoaded(
        object sender,
        RoutedEventArgs e) =>
        ApplyUniformIconCellSize();

    private void FileSurfaceContent_LayoutPropertyChanged(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WidgetViewModel.IconCellWidth) or
            nameof(WidgetViewModel.IconCellHeight))
        {
            if (DispatcherQueue.HasThreadAccess)
            {
                ApplyUniformIconCellSize();
            }
            else
            {
                _ = DispatcherQueue.TryEnqueue(ApplyUniformIconCellSize);
            }
        }
    }

    private void FileSurfaceContent_TextScaleFactorChanged()
    {
        if (_isDisposed)
        {
            return;
        }

        if (!DispatcherQueue.HasThreadAccess)
        {
            _ = DispatcherQueue.TryEnqueue(RefreshFileTextScaleFactor);
            return;
        }

        RefreshFileTextScaleFactor();
    }

    private void RefreshFileTextScaleFactor()
    {
        if (_isDisposed)
        {
            return;
        }

        ViewModel.UpdateSystemTextScaleFactor(
            WindowsCompatibilityService.ResolveSystemTextScaleFactor());
    }

    /// <summary>
    /// Sizes the uniform icon slots. The upstream uniform-slot mechanism is
    /// kept (a first-realized item must never dictate the cell), but the
    /// column count is derived from the panel's own arrangement width and the
    /// uncEiled content width (tile + margins), with the leftover fraction
    /// spread across every slot. Two measured behaviors force this shape:
    /// the ceiled cell width dropped a column at 294.4 / 74 = 3.98, and
    /// ItemsWrapGrid wraps the last item when the row is exactly
    /// columns * ItemWidth wide (verified: itemWidth 73.6 in a 294.4 panel
    /// arranged items at x = 0, 73.6, 147.2, then wrapped), so a one-pixel
    /// slack is reserved before dividing. Slot width never falls below the
    /// content width, so nothing clips.
    /// </summary>
    private void ApplyUniformIconCellSize()
    {
        if (_isDisposed ||
            ItemsGrid.ItemsPanelRoot is not ItemsWrapGrid panel)
        {
            return;
        }

        panel.Orientation = Orientation.Horizontal;
        panel.ItemHeight = ViewModel.IconCellHeight;

        double contentWidth = ViewModel.IconTileWidth +
            ViewModel.IconTileMargin.Left + ViewModel.IconTileMargin.Right;
        // The panel's own layout width is the authoritative arrangement
        // width: ItemsGrid.ActualWidth still includes the ScrollViewer's
        // padding and vertical scrollbar gutter, so dividing that instead
        // pushes the last column behind the scrollbar where it is clipped.
        double viewportWidth = panel.ActualWidth > 0
            ? panel.ActualWidth
            : ItemsGrid.ActualWidth -
              ItemsGrid.Padding.Left - ItemsGrid.Padding.Right -
              ItemsGrid.BorderThickness.Left - ItemsGrid.BorderThickness.Right;
        if (contentWidth <= 0 ||
            !double.IsFinite(viewportWidth) ||
            viewportWidth < contentWidth)
        {
            // First layout pass or a viewport narrower than one tile: fall
            // back to the measured cell width (matches the pre-viewport
            // behavior, including its horizontal overflow handling).
            panel.ItemWidth = ViewModel.IconCellWidth;
            return;
        }

        // ItemsWrapGrid only accepts a column when the row is strictly wider
        // than columns * ItemWidth; an exact division (294.4 / 4 x 73.6) makes
        // it wrap the last item to the next row and leaves a full slot of
        // blank gutter. Reserve one logical pixel before dividing so the
        // arrangement is always strictly inside the viewport.
        double usableWidth = viewportWidth - LayoutSlackPixels;
        int columns = Math.Max(1, (int)(usableWidth / contentWidth));
        panel.ItemWidth = Math.Max(contentWidth, usableWidth / columns);
    }
}
