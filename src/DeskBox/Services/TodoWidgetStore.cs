using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Models;

namespace DeskBox.Services;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(
    typeof(TodoWidgetData),
    TypeInfoPropertyName = "StoreData")]
internal sealed partial class TodoJsonContext : JsonSerializerContext
{
}

public sealed class TodoWidgetStore
{
    // DEF-043: Todo data is written by two independent flows - the widget
    // view model (user edits) and TodoReminderService (background reminder
    // bookkeeping). Both follow a load/modify/save-whole-document pattern,
    // so interleaved writers used to let a stale snapshot overwrite a newer
    // document (lost user edits or lost reminder marks). The gate is keyed
    // by store path so every TodoWidgetStore instance targeting the same
    // file shares one serialization point, mirroring the existing pattern
    // in WeatherCacheStore.
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> s_pathGates =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _gate;

    private readonly string _storePath;

    public TodoWidgetStore(string widgetId)
        : this(
            Path.Combine(
                DeskBoxDataPathService.Current.DataDirectory,
                "widgets"),
            widgetId)
    {
    }

    internal TodoWidgetStore(string widgetsDataRoot, string widgetId)
    {
        if (string.IsNullOrWhiteSpace(widgetId))
        {
            throw new ArgumentException("Widget id cannot be empty.", nameof(widgetId));
        }

        string safeWidgetId = SanitizeWidgetId(widgetId);
        string dataDir = Path.Combine(widgetsDataRoot, safeWidgetId);
        Directory.CreateDirectory(dataDir);
        _storePath = Path.Combine(dataDir, "todo.json");
        _gate = s_pathGates.GetOrAdd(_storePath, static _ => new SemaphoreSlim(1, 1));
    }

    internal string StorePath => _storePath;

    internal string AttachmentDirectory => Path.Combine(Path.GetDirectoryName(_storePath)!, "attachments");

    public async Task<TodoWidgetData> LoadAsync()
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            return await LoadUnsafeAsync().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(TodoWidgetData data)
    {
        data = Normalize(data);
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            await SaveUnsafeAsync(data).ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Runs a load/modify/save cycle while holding the store gate across the
    /// whole sequence (DEF-043). Background writers must use this instead of
    /// separate Load/Save calls: holding the gate prevents a concurrent
    /// writer's whole-document save from being overwritten by this writer's
    /// stale snapshot, and vice versa.
    /// </summary>
    public async Task<TodoWidgetData> MutateAsync(Func<TodoWidgetData, bool> mutate)
    {
        ArgumentNullException.ThrowIfNull(mutate);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            TodoWidgetData data = await LoadUnsafeAsync().ConfigureAwait(false);
            bool changed = mutate(data);
            if (changed)
            {
                await SaveUnsafeAsync(data).ConfigureAwait(false);
            }

            return data;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TodoWidgetData> LoadUnsafeAsync()
    {
        return await ResilientJsonStore.LoadAsync(
            _storePath,
            json => Normalize(JsonSerializer.Deserialize(
                json,
                TodoJsonContext.Default.StoreData)),
            () => new TodoWidgetData(),
            nameof(TodoWidgetStore));
    }

    /// <summary>
    /// Persists an already-normalized document. Callers must hold
    /// <see cref="_gate"/> (Load/Save/Mutate wrappers do); the JSON
    /// serialization call sites intentionally stay at the frozen
    /// JsonSerializationBaseline inventory count for this file.
    /// </summary>
    private async Task SaveUnsafeAsync(TodoWidgetData data)
    {
        string json = JsonSerializer.Serialize(
            data,
            TodoJsonContext.Default.StoreData);
        await ResilientJsonStore.SaveAsync(_storePath, json).ConfigureAwait(false);
    }

    private static TodoWidgetData Normalize(TodoWidgetData? data)
    {
        data ??= new TodoWidgetData();
        data.Version = Math.Max(3, data.Version);
        data.Items ??= [];

        int fallbackSortOrder = 0;
        foreach (var item in data.Items.Where(item => item is not null))
        {
            if (string.IsNullOrWhiteSpace(item.Id))
            {
                item.Id = Guid.NewGuid().ToString("N");
            }

            item.Text = item.Text?.Trim() ?? string.Empty;
            item.ColorMarker = TodoItem.NormalizeColorMarker(item.ColorMarker);
            item.Recurrence = TodoRecurrence.Normalize(item.Recurrence, item.DueDate);
            item.RecurrenceSeriesId = TodoRecurrenceService.NormalizeSeriesId(item.RecurrenceSeriesId);
            // Notes may contain Markdown where trailing spaces and indentation
            // are meaningful, so only collapse an entirely blank document.
            item.Notes = string.IsNullOrWhiteSpace(item.Notes) ? null : item.Notes;
            item.Steps ??= [];
            item.Attachments ??= [];
            NormalizeSteps(item.Steps);
            NormalizeAttachments(item.Attachments);
            item.ReminderOffsetMinutes = TodoReminderOptions.NormalizeOffsetMinutes(item.ReminderOffsetMinutes);
            item.GeneratedNextItemId = string.IsNullOrWhiteSpace(item.GeneratedNextItemId)
                ? null
                : item.GeneratedNextItemId.Trim();
            if (item.CreatedAt == default)
            {
                item.CreatedAt = DateTimeOffset.UtcNow;
            }

            if (item.UpdatedAt == default)
            {
                item.UpdatedAt = item.CreatedAt;
            }

            if (item.IsCompleted)
            {
                item.CompletedAt ??= item.UpdatedAt == default ? item.CreatedAt : item.UpdatedAt;
            }
            else
            {
                item.CompletedAt = null;
            }

            if (item.DueDate is null)
            {
                item.Recurrence = null;
                item.RecurrenceSeriesId = null;
                item.ReminderLastNotifiedAt = null;
                item.ReminderDismissedForDueDate = null;
                item.SnoozedUntil = null;
                item.SnoozeLastNotifiedAt = null;
            }
            else if (item.IsCompleted)
            {
                item.SnoozedUntil = null;
                item.SnoozeLastNotifiedAt = null;
            }
            else if (item.ReminderDismissedForDueDate is { } dismissedForDueDate &&
                     !DateTimeOffset.Equals(dismissedForDueDate, item.DueDate.Value))
            {
                item.ReminderLastNotifiedAt = null;
                item.ReminderDismissedForDueDate = null;
                item.SnoozedUntil = null;
                item.SnoozeLastNotifiedAt = null;
            }

            if (!item.IsCompleted || item.Recurrence is null)
            {
                item.GeneratedNextItemId = null;
            }

            if (item.Recurrence is null)
            {
                item.RecurrenceSeriesId = null;
            }

            if (item.SortOrder < 0)
            {
                item.SortOrder = fallbackSortOrder;
            }

            fallbackSortOrder++;
        }

        data.Items = data.Items
            .Where(item => item is not null && !string.IsNullOrWhiteSpace(item.Text))
            .GroupBy(item => item.Id, StringComparer.Ordinal)
            .Select(group => group.First())
            .OrderBy(item => item.SortOrder)
            .ThenByDescending(item => item.UpdatedAt)
            .ToList();

        NormalizeRecurrenceSeriesIds(data.Items);
        NormalizeSortOrders(data.Items);
        return data;
    }

    public Task ClearAsync()
    {
        return SaveAsync(new TodoWidgetData());
    }

    private static void NormalizeSteps(List<TodoStep> steps)
    {
        int sortOrder = 0;
        foreach (TodoStep step in steps.Where(step => step is not null))
        {
            step.Id = string.IsNullOrWhiteSpace(step.Id) ? Guid.NewGuid().ToString("N") : step.Id.Trim();
            step.Text = step.Text?.Trim() ?? string.Empty;
            step.SortOrder = sortOrder++;
        }

        steps.RemoveAll(step => step is null || string.IsNullOrWhiteSpace(step.Text));
        for (int index = 0; index < steps.Count; index++)
        {
            steps[index].SortOrder = index;
        }
    }

    private static void NormalizeAttachments(List<TodoAttachment> attachments)
    {
        foreach (TodoAttachment attachment in attachments.Where(attachment => attachment is not null))
        {
            attachment.Id = string.IsNullOrWhiteSpace(attachment.Id)
                ? Guid.NewGuid().ToString("N")
                : attachment.Id.Trim();
            attachment.FilePath = attachment.FilePath?.Trim() ?? string.Empty;
            attachment.DisplayName = string.IsNullOrWhiteSpace(attachment.DisplayName)
                ? Path.GetFileName(attachment.FilePath)
                : attachment.DisplayName.Trim();
            attachment.Type = string.IsNullOrWhiteSpace(attachment.Type) ? "file" : attachment.Type.Trim();
            attachment.StorageMode = TodoAttachment.NormalizeStorageMode(attachment.StorageMode);
            attachment.AddedAt = attachment.AddedAt == default ? DateTimeOffset.UtcNow : attachment.AddedAt;
        }

        attachments.RemoveAll(attachment => attachment is null || string.IsNullOrWhiteSpace(attachment.FilePath));
    }

    private static void NormalizeRecurrenceSeriesIds(List<TodoItem> items)
    {
        var recurringItems = items
            .Where(item => item.Recurrence is not null)
            .ToList();
        if (recurringItems.Count == 0)
        {
            return;
        }

        var itemsById = recurringItems.ToDictionary(item => item.Id, StringComparer.Ordinal);
        var visitedIds = new HashSet<string>(StringComparer.Ordinal);

        foreach (var item in recurringItems)
        {
            if (!visitedIds.Add(item.Id))
            {
                continue;
            }

            var component = new List<TodoItem>();
            var queue = new Queue<TodoItem>();
            queue.Enqueue(item);

            while (queue.Count > 0)
            {
                TodoItem current = queue.Dequeue();
                component.Add(current);

                if (!string.IsNullOrWhiteSpace(current.GeneratedNextItemId) &&
                    itemsById.TryGetValue(current.GeneratedNextItemId, out TodoItem? nextItem) &&
                    visitedIds.Add(nextItem.Id))
                {
                    queue.Enqueue(nextItem);
                }

                foreach (var previousItem in recurringItems.Where(entry =>
                             string.Equals(entry.GeneratedNextItemId, current.Id, StringComparison.Ordinal)))
                {
                    if (visitedIds.Add(previousItem.Id))
                    {
                        queue.Enqueue(previousItem);
                    }
                }
            }

            string seriesId = component
                .Select(entry => TodoRecurrenceService.NormalizeSeriesId(entry.RecurrenceSeriesId))
                .FirstOrDefault(seriesId => !string.IsNullOrWhiteSpace(seriesId))
                ?? Guid.NewGuid().ToString("N");

            foreach (var componentItem in component)
            {
                componentItem.RecurrenceSeriesId = seriesId;
            }
        }
    }

    private static void NormalizeSortOrders(List<TodoItem> items)
    {
        for (int index = 0; index < items.Count; index++)
        {
            items[index].SortOrder = index;
        }
    }

    private static string SanitizeWidgetId(string widgetId)
    {
        string trimmed = widgetId.Trim();
        char[] invalidChars = Path.GetInvalidFileNameChars();
        var safeChars = trimmed.Select(ch => invalidChars.Contains(ch) ? '_' : ch).ToArray();
        string safe = new(safeChars);
        return string.IsNullOrWhiteSpace(safe) ? Guid.NewGuid().ToString("N") : safe;
    }

}
