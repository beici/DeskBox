using DeskBox.Models;
using DeskBox.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;

namespace DeskBox.Controls;

public readonly record struct FileItemDragPackageResult(
    IReadOnlyList<string> SourcePaths,
    bool HasStorageItems,
    bool UsesNativeShellDataObject);

/// <summary>
/// Creates the common file-item drag payload. Hosts remain responsible for
/// deciding which items are dragged and how the completed drop is reconciled.
/// </summary>
public static class FileItemDragPackage
{
    public static IReadOnlyList<WidgetItem> ResolveDraggedItems(
        IReadOnlyList<WidgetItem> eventItems,
        IReadOnlyList<WidgetItem> selectedItems)
    {
        WidgetItem[] distinctEventItems = eventItems.Distinct().ToArray();
        WidgetItem[] distinctSelectedItems = selectedItems.Distinct().ToArray();
        if (distinctSelectedItems.Length <= 1 || distinctEventItems.Length == 0)
        {
            return distinctEventItems;
        }

        // Some WinUI ListView input paths report only the pointer anchor in
        // DragItemsStarting even though it belongs to a larger selection. The
        // visible selection is authoritative whenever the event anchor is one
        // of its members.
        return distinctEventItems.Any(distinctSelectedItems.Contains)
            ? distinctSelectedItems
            : distinctEventItems;
    }

    public static bool TryPrepare(
        DataPackage dataPackage,
        IReadOnlyList<WidgetItem> draggedItems,
        string sourceWidgetId,
        Func<IEnumerable<string>, IReadOnlyList<IStorageItem>> getStorageItems,
        Func<IReadOnlyList<string>, string> getTitle,
        out FileItemDragPackageResult result)
    {
        result = default;
        if (draggedItems.Count == 0)
        {
            return false;
        }

        string[] sourcePaths = draggedItems
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length == 0)
        {
            return false;
        }

        // WinRT's StorageFile broker can reject .lnk files (including ones
        // whose filesystem attributes look normal). More importantly, this
        // event is raised on the UI STA, so synchronously waiting for that
        // broker can deadlock the drag/drop message loop. Wrap a native Shell
        // IDataObject before attempting that broker so Explorer receives the
        // original filesystem item and owns its desktop drop position.
        bool requiresStorageBrokerBypass =
            NativeShellFileDragProvider.RequiresStorageBrokerBypass(
                sourcePaths);
        bool usesNativeShellDataObject =
            requiresStorageBrokerBypass &&
            NativeShellFileDragProvider.TryAttach(dataPackage, sourcePaths);
        IReadOnlyList<IStorageItem> storageItems = [];
        if (requiresStorageBrokerBypass && !usesNativeShellDataObject)
        {
            App.Log(
                $"[DragStart] Canceled broker-blocked file drag because a " +
                $"native Shell payload could not be created paths=" +
                $"{sourcePaths.Length}");
            return false;
        }

        if (!usesNativeShellDataObject)
        {
            storageItems = getStorageItems(sourcePaths);
            if (storageItems.Count == sourcePaths.Length)
            {
                dataPackage.SetStorageItems(storageItems, readOnly: false);
            }
            else
            {
                // Never advertise a partial selection or fall back to a
                // coordinate-free filesystem move after Drop. A native Shell
                // data object can represent the same existing paths without
                // involving the StorageItem broker.
                usesNativeShellDataObject =
                    NativeShellFileDragProvider.TryAttach(
                        dataPackage,
                        sourcePaths);
                storageItems = [];
                if (!usesNativeShellDataObject)
                {
                    App.Log(
                        $"[DragStart] Canceled file drag because only a " +
                        $"partial StorageItems payload was available " +
                        $"paths={sourcePaths.Length}");
                    return false;
                }
            }
        }

        dataPackage.RequestedOperation =
            DataPackageOperation.Copy |
            DataPackageOperation.Move |
            DataPackageOperation.Link;

        dataPackage.Properties[DeskBoxDragData.SourceWidgetIdProperty] =
            sourceWidgetId;
        dataPackage.Properties[DeskBoxDragData.SourcePathsProperty] =
            sourcePaths;
        dataPackage.Properties[
            DeskBoxDragData.InternalFileDragTokenProperty] =
            DeskBoxDragData.InternalFileDragToken;
        dataPackage.Properties.Title = getTitle(sourcePaths);
        dataPackage.SetText(string.Join(Environment.NewLine, sourcePaths));

        result = new FileItemDragPackageResult(
            sourcePaths,
            storageItems.Count > 0 || usesNativeShellDataObject,
            usesNativeShellDataObject);
        return true;
    }

    /// <summary>
    /// DEF-023 (THR-03): deferred-payload variant of <see cref="TryPrepare"/>
    /// for the drag-start path. The main path is fully synchronous (drag
    /// commit semantics preserved, no UI-thread yield) and the StorageItem
    /// broker round-trips move into a SetDataProvider callback (mirroring
    /// QuickCaptureDragPackage), so a slow or network drive can no longer
    /// freeze the shell on every drag — the broker is only hit when a drop
    /// target actually asks for the items, and Explorer-style targets that
    /// consume the native Shell payload never trigger it at all. Returns
    /// false when the drag must be canceled (no resolvable paths or a
    /// broker-blocked drag without a native Shell fallback); the caller
    /// cancels the drag-start event.
    /// </summary>
    public static bool TryPrepareDeferred(
        DataPackage dataPackage,
        IReadOnlyList<WidgetItem> draggedItems,
        string sourceWidgetId,
        FileService fileService,
        Func<IReadOnlyList<string>, string> getTitle,
        out FileItemDragPackageResult result)
    {
        result = default;
        if (draggedItems.Count == 0)
        {
            return false;
        }

        string[] sourcePaths = draggedItems
            .Select(item => item.Path)
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (sourcePaths.Length == 0)
        {
            return false;
        }

        bool requiresStorageBrokerBypass =
            NativeShellFileDragProvider.RequiresStorageBrokerBypass(
                sourcePaths);
        bool usesNativeShellDataObject =
            requiresStorageBrokerBypass &&
            NativeShellFileDragProvider.TryAttach(dataPackage, sourcePaths);
        if (requiresStorageBrokerBypass && !usesNativeShellDataObject)
        {
            App.Log(
                $"[DragStart] Canceled broker-blocked file drag because a " +
                $"native Shell payload could not be created paths=" +
                $"{sourcePaths.Length}");
            return false;
        }

        if (!usesNativeShellDataObject)
        {
            // Deferred payload: resolved on the thread pool when the drop
            // target requests StorageItems, never on the UI STA.
            dataPackage.SetDataProvider(StandardDataFormats.StorageItems, async request =>
            {
                var deferral = request.GetDeferral();
                try
                {
                    IReadOnlyList<IStorageItem> items =
                        await fileService.GetStorageItemsAsync(sourcePaths);
                    if (items.Count == sourcePaths.Length)
                    {
                        request.SetData(items);
                    }
                    else
                    {
                        App.Log(
                            $"[DragStart] Drop target requested StorageItems but " +
                            $"only a partial payload was available " +
                            $"resolved={items.Count} requested={sourcePaths.Length}");
                    }
                }
                catch (Exception ex)
                {
                    App.Log($"[DragStart] Deferred StorageItems provider failed: {ex}");
                }
                finally
                {
                    deferral.Complete();
                }
            });
        }

        dataPackage.RequestedOperation =
            DataPackageOperation.Copy |
            DataPackageOperation.Move |
            DataPackageOperation.Link;

        dataPackage.Properties[DeskBoxDragData.SourceWidgetIdProperty] =
            sourceWidgetId;
        dataPackage.Properties[DeskBoxDragData.SourcePathsProperty] =
            sourcePaths;
        dataPackage.Properties[
            DeskBoxDragData.InternalFileDragTokenProperty] =
            DeskBoxDragData.InternalFileDragToken;
        dataPackage.Properties.Title = getTitle(sourcePaths);
        dataPackage.SetText(string.Join(Environment.NewLine, sourcePaths));

        result = new FileItemDragPackageResult(
            sourcePaths,
            HasStorageItems: true,
            usesNativeShellDataObject);
        return true;
    }
}
