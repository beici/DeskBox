using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class FileSurfaceContent
{
    private bool _renderWindowCoverageQueued;
    private ScrollViewer? _gridRenderWindowScrollViewer;
    private ScrollViewer? _listRenderWindowScrollViewer;

    /// <summary>
    /// Grows the view model's render window as the user approaches the end of
    /// the rendered prefix, so a folder with thousands of items scrolls
    /// continuously without ever laying out its full contents at once.
    /// </summary>
    private void RegisterRenderWindowScrollTracking()
    {
        ItemsGrid.Loaded += ItemsView_LoadedForRenderWindow;
        ItemsList.Loaded += ItemsView_LoadedForRenderWindow;
        FileItemsViewport.LayoutUpdated += ItemsView_LayoutUpdatedForRenderWindow;
        ItemsGrid.SizeChanged += ItemsView_SizeChangedForRenderWindow;
        ItemsList.SizeChanged += ItemsView_SizeChangedForRenderWindow;
        ViewModel.PropertyChanged += ViewModel_PropertyChangedForRenderWindow;
        // Bulk imports change the item count without any geometry change, so
        // the view model reports each (coalesced) source reconcile and the
        // view re-checks viewport coverage — crossing the activation
        // threshold must not strand unrendered items with no scrollbar.
        ViewModel.RenderWindowSourceChanged += ViewModel_RenderWindowSourceChanged;
    }

    /// <summary>
    /// Releases the render-window event hooks. LayoutUpdated fires on every
    /// layout pass of a live surface, so the tracking must not outlive it.
    /// </summary>
    internal void UnregisterRenderWindowTracking()
    {
        ItemsGrid.Loaded -= ItemsView_LoadedForRenderWindow;
        ItemsList.Loaded -= ItemsView_LoadedForRenderWindow;
        FileItemsViewport.LayoutUpdated -= ItemsView_LayoutUpdatedForRenderWindow;
        ItemsGrid.SizeChanged -= ItemsView_SizeChangedForRenderWindow;
        ItemsList.SizeChanged -= ItemsView_SizeChangedForRenderWindow;
        ViewModel.PropertyChanged -= ViewModel_PropertyChangedForRenderWindow;
        ViewModel.RenderWindowSourceChanged -= ViewModel_RenderWindowSourceChanged;
        if (_gridRenderWindowScrollViewer is { } gridScrollViewer)
        {
            gridScrollViewer.ViewChanged -= ItemsView_ViewChangedForRenderWindow;
            _gridRenderWindowScrollViewer = null;
        }
        if (_listRenderWindowScrollViewer is { } listScrollViewer)
        {
            listScrollViewer.ViewChanged -= ItemsView_ViewChangedForRenderWindow;
            _listRenderWindowScrollViewer = null;
        }
    }

    private void ViewModel_RenderWindowSourceChanged() =>
        QueueRenderWindowViewportCoverage();

    private void QueueRenderWindowViewportCoverage()
    {
        if (_isDisposed || _renderWindowCoverageQueued)
        {
            return;
        }

        // Never insert a large prefix inside ScrollViewer's ViewChanged/layout
        // callback. Coalesce requests and let input/rendering run before growth.
        _renderWindowCoverageQueued = true;
        if (!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low, () =>
        {
            try
            {
                if (_isDisposed || !IsLoaded)
                {
                    return;
                }

                ListViewBase itemsView = GetActiveItemsView();
                EnsureRenderWindowCoversViewport(itemsView);
            }
            finally
            {
                _renderWindowCoverageQueued = false;
            }
        }))
        {
            _renderWindowCoverageQueued = false;
        }
    }

    private void ItemsView_SizeChangedForRenderWindow(
        object sender,
        Microsoft.UI.Xaml.SizeChangedEventArgs e) =>
        QueueRenderWindowViewportCoverage();

    private void ViewModel_PropertyChangedForRenderWindow(
        object? sender,
        System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(WidgetViewModel.IconCellWidth) or
            nameof(WidgetViewModel.IconCellHeight))
        {
            QueueRenderWindowViewportCoverage();
        }
    }

    /// <summary>
    /// Raises the render window to cover the current viewport directly from
    /// its dimensions and item size — the extent-based fallback below only
    /// reacts after a layout pass, so a fixed small prefix that never
    /// overflows could leave unrendered items with no scrollbar at all.
    /// </summary>
    private void EnsureRenderWindowCoversViewport(ListViewBase? itemsView)
    {
        if (_isDisposed || itemsView is null)
        {
            return;
        }

        ScrollViewer? scrollViewer = GetRenderWindowScrollViewer(itemsView);
        // The ScrollViewer content viewport is what actually bounds visible
        // items; the items control frame is only a fallback estimate.
        double viewportWidth = scrollViewer?.ViewportWidth > 0
            ? scrollViewer.ViewportWidth
            : itemsView.ActualWidth;
        double viewportHeight = scrollViewer?.ViewportHeight > 0
            ? scrollViewer.ViewportHeight
            : itemsView.ActualHeight;
        if (viewportWidth <= 0 || viewportHeight <= 0)
        {
            return;
        }

        bool isGrid = ReferenceEquals(itemsView, ItemsGrid);
        // List rows size to content; their realized height is authoritative.
        int viewportMinimum = WidgetViewModel.ComputeViewportRenderMinimum(
            viewportWidth,
            viewportHeight,
            isGrid ? ViewModel.IconCellWidth : Math.Max(1, viewportWidth),
            isGrid ? ViewModel.IconCellHeight : EstimateListRowHeight(itemsView),
            bufferRows: 2);
        bool isNearRenderedEnd = scrollViewer is { ViewportHeight: > 0 } &&
            scrollViewer.VerticalOffset + scrollViewer.ViewportHeight * 2 >= scrollViewer.ExtentHeight;

        // One request means one reconcile and one hydration generation. Never
        // fill the viewport and then prefetch again against its stale extent.
        ViewModel.EnsureRenderWindowCoversViewport(
            FileRenderWindowGrowthPolicy.ComputeRequestedMinimum(
                ViewModel.RenderedItems.Count,
                viewportMinimum,
                ViewModel.CanGrowRenderWindow,
                isNearRenderedEnd));
    }

    private double EstimateListRowHeight(ListViewBase itemsView)
    {
        if (itemsView.ContainerFromIndex(0) is FrameworkElement { ActualHeight: > 0 } firstRow)
        {
            return firstRow.ActualHeight;
        }

        // No realized container yet: prefer a deliberately small estimate so
        // the first page over-renders rather than leaves the viewport empty.
        return Math.Max(24, Math.Min(ViewModel.IconCellHeight, 48));
    }

    private void ItemsView_LoadedForRenderWindow(object sender, RoutedEventArgs e)
    {
        HookRenderWindowScrollViewer(sender as ListViewBase);
    }

    private void HookRenderWindowScrollViewer(ListViewBase? itemsView)
    {
        if (_isDisposed || itemsView is null)
        {
            return;
        }

        if (FindDescendantScrollViewer(itemsView) is { } scrollViewer)
        {
            StoreRenderWindowScrollViewer(itemsView, scrollViewer);
            scrollViewer.ViewChanged -= ItemsView_ViewChangedForRenderWindow;
            scrollViewer.ViewChanged += ItemsView_ViewChangedForRenderWindow;
            QueueRenderWindowViewportCoverage();
        }
    }

    private void ItemsView_ViewChangedForRenderWindow(
        object? sender,
        ScrollViewerViewChangedEventArgs e)
    {
        if (_isDisposed || sender is not ScrollViewer scrollViewer)
        {
            return;
        }

        // The expanded-label overlay is positioned against the viewport;
        // scrolling invalidates that position, so collapse it rather than
        // chase the item.
        HideNameExpansion(isPopoverView: false);

        // Grow while unrendered content remains and the user is within two
        // viewports of the rendered end, so scrolling never hits a wall.
        if (ViewModel.CanGrowRenderWindow &&
            scrollViewer.VerticalOffset + (scrollViewer.ViewportHeight * 2) >=
            scrollViewer.ExtentHeight)
        {
            QueueRenderWindowViewportCoverage();
        }
    }

    /// <summary>
    /// A rendered prefix that fits entirely inside the viewport has no
    /// overflow to scroll, so <see cref="ScrollViewer.ViewChanged"/> never
    /// fires and the window would stay stuck at its initial size. After every
    /// layout pass (initial load, item changes, viewport resizes) grow while
    /// the prefix still does not overflow.
    /// </summary>
    private void ItemsView_LayoutUpdatedForRenderWindow(object? sender, object e)
    {
        if (_isDisposed || !IsLoaded)
        {
            return;
        }

        // WinUI always passes null for LayoutUpdated.sender. Subscribe once
        // for the viewport and explicitly select its active items view.
        ListViewBase itemsView = GetActiveItemsView();
        if (GetRenderWindowScrollViewer(itemsView) is null)
        {
            // The template may not exist during Loaded. Keep the retry on
            // this shared layout hook until its ScrollViewer is available.
            HookRenderWindowScrollViewer(itemsView);
            return;
        }

        TryGrowRenderWindowToFillViewport(GetRenderWindowScrollViewer(itemsView));
    }

    private void TryGrowRenderWindowToFillViewport(ScrollViewer? scrollViewer)
    {
        if (_isDisposed || scrollViewer is null || !ViewModel.CanGrowRenderWindow)
        {
            return;
        }

        if (scrollViewer.ViewportHeight > 0 &&
            scrollViewer.ExtentHeight <= scrollViewer.ViewportHeight)
        {
            QueueRenderWindowViewportCoverage();
        }
    }

    private void StoreRenderWindowScrollViewer(ListViewBase itemsView, ScrollViewer scrollViewer)
    {
        if (ReferenceEquals(itemsView, ItemsGrid))
        {
            _gridRenderWindowScrollViewer = scrollViewer;
        }
        else if (ReferenceEquals(itemsView, ItemsList))
        {
            _listRenderWindowScrollViewer = scrollViewer;
        }
    }

    private ScrollViewer? GetRenderWindowScrollViewer(ListViewBase itemsView) =>
        ReferenceEquals(itemsView, ItemsGrid) ? _gridRenderWindowScrollViewer :
        ReferenceEquals(itemsView, ItemsList) ? _listRenderWindowScrollViewer :
        FindDescendantScrollViewer(itemsView);

    private static ScrollViewer? FindDescendantScrollViewer(DependencyObject parent)
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);
        for (int index = 0; index < count; index++)
        {
            DependencyObject child = VisualTreeHelper.GetChild(parent, index);
            if (child is ScrollViewer scrollViewer)
            {
                return scrollViewer;
            }

            if (FindDescendantScrollViewer(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }
}
