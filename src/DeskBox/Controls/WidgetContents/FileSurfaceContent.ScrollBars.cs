using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;

namespace DeskBox.Controls.WidgetContents;

public sealed partial class FileSurfaceContent
{
    private void RegisterScrollBarActivityTracking(ListViewBase itemsView)
    {
        // Keep the viewport stable. WinUI's ScrollViewer owns the indicator's
        // fade/hover/drag states; toggling Hidden/Auto from PointerMoved forces
        // layout and can remove the thumb while the user still holds it.
        itemsView.SetValue(ScrollViewer.VerticalScrollBarVisibilityProperty,
            ScrollBarVisibility.Auto);
        itemsView.AddHandler(UIElement.PointerWheelChangedEvent,
            new PointerEventHandler(ItemsView_IconSizePointerWheel), handledEventsToo: true);
    }

    private void DisposeScrollBarActivityTracking()
    {
        foreach (ListViewBase itemsView in new ListViewBase[] { ItemsGrid, ItemsList })
        {
            itemsView.RemoveHandler(UIElement.PointerWheelChangedEvent,
                new PointerEventHandler(ItemsView_IconSizePointerWheel));
        }
    }
}
