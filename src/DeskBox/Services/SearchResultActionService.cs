using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Performs secondary actions on search results: attaching a file to a todo,
/// saving a file as a quick-capture note, and copying paths to the clipboard.
/// These actions are surfaced through the result context menu in the search popup.
/// </summary>
public sealed class SearchResultActionService
{
    private readonly SettingsService _settingsService;
    private readonly QuickCaptureService _quickCaptureService;
    private readonly Func<TodoReminderService?> _todoReminderServiceAccessor;
    private readonly Func<string, TodoWidgetStore> _todoStoreFactory;

    public SearchResultActionService(
        SettingsService settingsService,
        QuickCaptureService quickCaptureService,
        Func<TodoReminderService?> todoReminderServiceAccessor,
        Func<string, TodoWidgetStore>? todoStoreFactory = null)
    {
        _settingsService = settingsService;
        _quickCaptureService = quickCaptureService;
        _todoReminderServiceAccessor = todoReminderServiceAccessor;
        _todoStoreFactory = todoStoreFactory ?? (widgetId => new TodoWidgetStore(widgetId));
    }

    /// <summary>
    /// Creates a todo in the first available Todo widget with the file attached.
    /// Returns a human-readable outcome for display in a tooltip/toast.
    /// </summary>
    public async Task<bool> AttachFileToTodoAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var todoWidget = _settingsService.Settings.Widgets
                .FirstOrDefault(w => w.WidgetKind == WidgetKind.Todo && !w.IsDisabled);

            if (todoWidget is null)
            {
                App.Log("[SearchAction] No Todo widget available to attach to.");
                return false;
            }

            var store = _todoStoreFactory(todoWidget.Id);

            // FCFG-02: mutate through the store gate (load/modify/save held
            // atomically) instead of a separate Load/Save pair, so a
            // concurrent widget or reminder whole-document save can neither
            // overwrite this attachment nor be overwritten by it.
            TodoItem? insertedItem = null;
            await store.MutateAsync(current =>
            {
                string fileName = Path.GetFileName(path);
                var item = new TodoItem
                {
                    Text = fileName,
                    Notes = path,
                    SortOrder = current.Items.Count,
                    CreatedAt = DateTimeOffset.UtcNow,
                    UpdatedAt = DateTimeOffset.UtcNow
                };
                item.Attachments.Add(new TodoAttachment
                {
                    FilePath = path,
                    DisplayName = fileName,
                    Type = "file",
                    StorageMode = TodoAttachment.LinkedStorageMode,
                    AddedAt = DateTimeOffset.UtcNow
                });

                current.Items.Add(item);
                insertedItem = item;
                return true;
            });

            // DEF-043 relay: merge the external insert into any open todo
            // widget before its next whole-document save, or that save would
            // drop the attachment again with its stale snapshot. The item is
            // passed as insertedItem (not changedItem): open widgets ignore
            // changed items that are absent from their in-memory list.
            if (_todoReminderServiceAccessor() is { } reminderService)
            {
                reminderService.NotifyExternalStoreChanged(todoWidget.Id, changedItem: null, insertedItem);
            }

            App.Log($"[SearchAction] Attached '{Path.GetFileName(path)}' to todo widget '{todoWidget.Id}'.");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[SearchAction] Failed to attach file to todo: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Saves a file as a quick-capture note with the file attached.
    /// </summary>
    public async Task<bool> SaveFileToNoteAsync(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            // FCFG-01: route through the service cache instead of a raw
            // QuickCaptureStore write, so the search-written entry can no
            // longer be lost to the service's own cached-document save.
            QuickCaptureItem? created = await _quickCaptureService.AddExternalLinkedFileItemAsync(path);
            if (created is null)
            {
                App.Log($"[SearchAction] Skipped saving '{Path.GetFileName(path)}' to quick capture: item was not created.");
                return false;
            }

            App.Log($"[SearchAction] Saved '{Path.GetFileName(path)}' to quick capture.");
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[SearchAction] Failed to save file to note: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Whether the given result can be attached to a todo (requires an existing file
    /// and at least one enabled Todo widget).
    /// </summary>
    public bool CanAttachToTodo(SearchResultItem? item)
    {
        return item is not null &&
               item.Kind == SearchResultKind.File &&
               !string.IsNullOrWhiteSpace(item.DetailPath) &&
               File.Exists(item.DetailPath) &&
               _settingsService.Settings.Widgets
                   .Any(w => w.WidgetKind == WidgetKind.Todo && !w.IsDisabled);
    }

    /// <summary>
    /// Whether the given result can be saved as a note (requires an existing file).
    /// </summary>
    public bool CanSaveToNote(SearchResultItem? item)
    {
        return item is not null &&
               item.Kind == SearchResultKind.File &&
               !string.IsNullOrWhiteSpace(item.DetailPath) &&
               File.Exists(item.DetailPath);
    }
}
