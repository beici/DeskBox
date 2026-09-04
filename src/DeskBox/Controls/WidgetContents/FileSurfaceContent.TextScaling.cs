using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class FileSurfaceContent
{
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
    /// column count is decided from the UNCEILED content width
    /// (tile + margins) against the grid's actual viewport, and the leftover
    /// fraction is distributed across every slot. Deciding on the ceiled
    /// cell width instead discards up to a near-full column: measured
    /// cellW=74 against a 294.4px viewport is 3.98 columns, so the fourth
    /// column disappeared and left a blank gutter even though four
    /// 73.07px content widths fit with room to spare (4.03). With the
    /// content width as the divisor, slot width = viewport/columns is
    /// never smaller than the content by construction, so no tolerance
    /// constant is needed and nothing clips.
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
        double viewportWidth = ItemsGrid.ActualWidth -
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

        int columns = Math.Max(1, (int)(viewportWidth / contentWidth));
        panel.ItemWidth = viewportWidth / columns;
    }
}
