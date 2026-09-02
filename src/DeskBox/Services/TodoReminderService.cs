using DeskBox.Models;
using Microsoft.UI.Dispatching;

namespace DeskBox.Services;

public sealed record TodoReminderNotification(
    string Title,
    string Message,
    int Count,
    string? WidgetId = null,
    string? ItemId = null,
    bool HasTodayDueItem = false);

public sealed class TodoReminderService : IDisposable
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(8);
    private static readonly TimeSpan MissedReminderGrace = TimeSpan.FromMinutes(1);

    private readonly SettingsService _settingsService;
    private readonly LocalizationService _localizationService;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly Action<TodoReminderNotification> _notify;
    private readonly Func<string, TodoWidgetStore> _storeFactory;
    private readonly Func<DateTimeOffset> _clock;
    private readonly HashSet<string> _sessionNotifiedKeys = new(StringComparer.Ordinal);

    /// <summary>
    /// Raised on the UI thread after this service persists a change to a
    /// todo store (DEF-043). Open todo widgets listen and merge the change
    /// into their in-memory state, so the next user-driven save cannot
    /// overwrite the reminder bookkeeping with a stale snapshot.
    /// </summary>
    public event Action<string, TodoItem?, TodoItem?>? TodoStoreChanged;

    private DispatcherQueueTimer? _timer;
    private bool _isChecking;
    private bool _disposed;

    private enum ReminderTriggerKind
    {
        Due,
        Snooze
    }

    public TodoReminderService(
        SettingsService settingsService,
        LocalizationService localizationService,
        DispatcherQueue dispatcherQueue,
        Action<TodoReminderNotification> notify)
        : this(
            settingsService,
            localizationService,
            dispatcherQueue,
            notify,
            widgetId => new TodoWidgetStore(widgetId),
            () => DateTimeOffset.Now)
    {
    }

    internal TodoReminderService(
        SettingsService settingsService,
        LocalizationService localizationService,
        DispatcherQueue? dispatcherQueue,
        Action<TodoReminderNotification> notify,
        Func<string, TodoWidgetStore> storeFactory,
        Func<DateTimeOffset> clock)
    {
        _settingsService = settingsService;
        _localizationService = localizationService;
        _dispatcherQueue = dispatcherQueue;
        _notify = notify;
        _storeFactory = storeFactory;
        _clock = clock;
    }

    public void Start()
    {
        if (_disposed || _dispatcherQueue is null || _timer is not null)
        {
            return;
        }

        if (!ShouldBeRunning())
        {
            return;
        }

        // Clear notified keys from any previous session so that reminders
        // that were already shown before the app restarted can fire again.
        _sessionNotifiedKeys.Clear();

        _timer = _dispatcherQueue.CreateTimer();
        _timer.Interval = ScanInterval;
        _timer.Tick += Timer_Tick;
        _timer.Start();

        _ = RunDelayedInitialCheckAsync();
    }

    /// <summary>
    /// Called when settings change. Starts or stops the timer based on whether
    /// the Todo widget and reminder feature are enabled.
    /// </summary>
    public void Refresh()
    {
        if (_disposed)
        {
            return;
        }

        if (ShouldBeRunning())
        {
            if (_timer is null && _dispatcherQueue is not null)
            {
                // Clear notified keys when the timer is being (re)started
                // after being stopped — old keys from a previous active
                // period are no longer relevant.
                _sessionNotifiedKeys.Clear();
                _timer = _dispatcherQueue.CreateTimer();
                _timer.Interval = ScanInterval;
                _timer.Tick += Timer_Tick;
                _timer.Start();
            }
        }
        else if (_timer is not null)
        {
            _timer.Tick -= Timer_Tick;
            _timer.Stop();
            _timer = null;
            // Clear notified keys when the feature is disabled so they
            // don't accumulate indefinitely while inactive.
            _sessionNotifiedKeys.Clear();
        }
    }

    private bool ShouldBeRunning()
    {
        var settings = _settingsService.Settings;
        return settings.TodoReminderEnabled &&
               FeatureWidgetSettings.IsEnabled(settings, WidgetKind.Todo);
    }

    public async Task<int> CheckNowAsync(DateTimeOffset now)
    {
        if (_disposed || _isChecking)
        {
            return 0;
        }

        _isChecking = true;
        try
        {
            var settings = _settingsService.Settings;
            if (!settings.TodoReminderEnabled ||
                !FeatureWidgetSettings.IsEnabled(settings, WidgetKind.Todo))
            {
                return 0;
            }

            int defaultOffsetMinutes = SettingsService.NormalizeTodoReminderOffsetMinutes(
                settings.TodoDefaultReminderOffsetMinutes);
            var widgets = settings.Widgets
                .Where(widget =>
                    widget.WidgetKind == WidgetKind.Todo &&
                    !widget.IsDisabled &&
                    !settings.DeletedWidgetIds.Contains(widget.Id))
                .ToList();

            if (widgets.Count == 0)
            {
                return 0;
            }

            List<TodoReminderCandidate> candidates = [];
            foreach (var widget in widgets)
            {
                await CollectWidgetCandidatesAsync(widget, now, defaultOffsetMinutes, candidates);
            }

            if (candidates.Count == 0)
            {
                return 0;
            }

            _notify(BuildNotification(candidates));
            return candidates.Count;
        }
        catch (Exception ex)
        {
            App.Log($"[TodoReminder] Check failed: {ex}");
            return 0;
        }
        finally
        {
            _isChecking = false;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        if (_timer is not null)
        {
            _timer.Tick -= Timer_Tick;
            _timer.Stop();
            _timer = null;
        }
    }

    internal static bool ShouldNotify(TodoItem item, DateTimeOffset now, TimeSpan reminderOffset)
    {
        return ShouldNotifyDue(item, now, reminderOffset);
    }

    internal static bool ShouldNotify(TodoItem item, DateTimeOffset now, int defaultOffsetMinutes)
    {
        return TryGetReminderTrigger(item, now, defaultOffsetMinutes, out _, out _);
    }

    public async Task<bool> SnoozeAsync(string? widgetId, string? itemId, TimeSpan snoozeFor)
    {
        if (_disposed ||
            string.IsNullOrWhiteSpace(widgetId) ||
            string.IsNullOrWhiteSpace(itemId) ||
            snoozeFor <= TimeSpan.Zero)
        {
            return false;
        }

        return await SnoozeUntilAsync(widgetId, itemId, _clock().Add(snoozeFor));
    }

    public async Task<bool> SnoozeUntilAsync(string? widgetId, string? itemId, DateTimeOffset snoozedUntil)
    {
        if (_disposed ||
            string.IsNullOrWhiteSpace(widgetId) ||
            string.IsNullOrWhiteSpace(itemId) ||
            snoozedUntil <= _clock())
        {
            return false;
        }

        try
        {
            if (!TryGetTodoReminderWidget(widgetId, requireReminderEnabled: true, out WidgetConfig? widget))
            {
                return false;
            }

            var store = _storeFactory(widget.Id);
            TodoItem? changedItem = null;
            _ = await store.MutateAsync(current =>
            {
                var item = current.Items.FirstOrDefault(entry =>
                    string.Equals(entry.Id, itemId, StringComparison.Ordinal));
                if (item is null ||
                    item.IsCompleted ||
                    item.DueDate is null ||
                    TodoReminderOptions.IsReminderOff(item.ReminderOffsetMinutes))
                {
                    return false;
                }

                item.SnoozedUntil = snoozedUntil;
                item.SnoozeLastNotifiedAt = null;
                item.ReminderDismissedForDueDate = item.DueDate;
                item.UpdatedAt = _clock().ToUniversalTime();
                changedItem = item;
                return true;
            }).ConfigureAwait(false);

            if (changedItem is null)
            {
                return false;
            }

            App.Log($"[TodoReminder] Snoozed widget={widgetId} item={itemId} until={changedItem.SnoozedUntil:O}");
            PublishStoreChanged(widget.Id, changedItem, insertedItem: null);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TodoReminder] Snooze failed: {ex}");
            return false;
        }
    }

    public async Task<bool> CompleteAsync(string? widgetId, string? itemId)
    {
        if (_disposed ||
            string.IsNullOrWhiteSpace(widgetId) ||
            string.IsNullOrWhiteSpace(itemId))
        {
            return false;
        }

        try
        {
            if (!TryGetTodoReminderWidget(widgetId, requireReminderEnabled: false, out WidgetConfig? widget))
            {
                return false;
            }

            var store = _storeFactory(widget.Id);
            TodoItem? completedItem = null;
            TodoItem? insertedNext = null;
            DateTimeOffset now = _clock().ToUniversalTime();
            _ = await store.MutateAsync(current =>
            {
                int itemIndex = current.Items.FindIndex(item =>
                    string.Equals(item.Id, itemId, StringComparison.Ordinal));
                if (itemIndex < 0)
                {
                    return false;
                }

                var item = current.Items[itemIndex];
                if (item.IsCompleted)
                {
                    return false;
                }

                if (item.Recurrence is not null)
                {
                    item.RecurrenceSeriesId ??= Guid.NewGuid().ToString("N");
                }

                item.IsCompleted = true;
                item.CompletedAt = now;
                item.UpdatedAt = now;
                item.GeneratedNextItemId = null;
                item.SnoozedUntil = null;
                item.SnoozeLastNotifiedAt = null;

                if (TodoRecurrenceService.TryCreateNextOccurrence(item, now, out TodoItem? nextItem) &&
                    nextItem is not null)
                {
                    item.GeneratedNextItemId = nextItem.Id;
                    current.Items.Insert(Math.Clamp(itemIndex + 1, 0, current.Items.Count), nextItem);
                    insertedNext = nextItem;
                }

                completedItem = item;
                return true;
            }).ConfigureAwait(false);

            if (completedItem is null)
            {
                // The item may have been completed by an open widget in the
                // meantime; the store now reflects whichever write landed
                // first and there is nothing left to do.
                return itemExists(store, itemId);
            }

            App.Log($"[TodoReminder] Completed from notification widget={widgetId} item={itemId}");
            PublishStoreChanged(widget.Id, completedItem, insertedNext);
            return true;
        }
        catch (Exception ex)
        {
            App.Log($"[TodoReminder] Complete failed: {ex}");
            return false;
        }

        static async Task<bool> itemExists(TodoWidgetStore store, string itemId)
        {
            var data = await store.LoadAsync().ConfigureAwait(false);
            return data.Items.Any(entry =>
                string.Equals(entry.Id, itemId, StringComparison.Ordinal) && entry.IsCompleted);
        }
    }

    private async Task RunDelayedInitialCheckAsync()
    {
        try
        {
            await Task.Delay(StartupDelay);
            await CheckNowAsync(_clock());
        }
        catch (Exception ex)
        {
            App.Log($"[TodoReminder] Initial check failed: {ex}");
        }
    }

    private bool TryGetTodoReminderWidget(
        string widgetId,
        bool requireReminderEnabled,
        out WidgetConfig widget)
    {
        widget = null!;
        var settings = _settingsService.Settings;
        if ((requireReminderEnabled && !settings.TodoReminderEnabled) ||
            !FeatureWidgetSettings.IsEnabled(settings, WidgetKind.Todo))
        {
            return false;
        }

        var match = settings.Widgets.FirstOrDefault(entry =>
            entry.WidgetKind == WidgetKind.Todo &&
            !entry.IsDisabled &&
            !settings.DeletedWidgetIds.Contains(entry.Id) &&
            string.Equals(entry.Id, widgetId, StringComparison.Ordinal));
        if (match is null)
        {
            return false;
        }

        widget = match;
        return true;
    }

    private async void Timer_Tick(DispatcherQueueTimer sender, object args)
    {
        await CheckNowAsync(_clock());
    }

    private async Task CollectWidgetCandidatesAsync(
        WidgetConfig widget,
        DateTimeOffset now,
        int defaultOffsetMinutes,
        List<TodoReminderCandidate> candidates)
    {
        var store = _storeFactory(widget.Id);
        List<TodoReminderCandidate> widgetCandidates = [];
        TodoItem? lastChangedItem = null;
        _ = await store.MutateAsync(current =>
        {
            bool changed = false;
            foreach (var item in current.Items)
            {
                if (!TryGetReminderTrigger(item, now, defaultOffsetMinutes, out ReminderTriggerKind triggerKind, out int? effectiveOffsetMinutes))
                {
                    continue;
                }

                string reminderKey = GetReminderKey(widget.Id, item, triggerKind, effectiveOffsetMinutes);
                if (!_sessionNotifiedKeys.Add(reminderKey))
                {
                    continue;
                }

                if (triggerKind == ReminderTriggerKind.Snooze)
                {
                    item.SnoozeLastNotifiedAt = now;
                    item.SnoozedUntil = null;
                }
                else
                {
                    item.ReminderLastNotifiedAt = now;
                    item.ReminderDismissedForDueDate = item.DueDate;
                }

                changed = true;
                lastChangedItem = item;
                widgetCandidates.Add(new TodoReminderCandidate(widget.Id, widget.Name, item));
            }

            return changed;
        }).ConfigureAwait(false);

        if (widgetCandidates.Count > 0)
        {
            candidates.AddRange(widgetCandidates);
            PublishStoreChanged(widget.Id, lastChangedItem, insertedItem: null);
        }
    }

    /// <summary>
    /// Notifies open todo widgets about a persisted store change (DEF-043).
    /// The event is raised on the UI thread so subscribers can touch their
    /// observable state directly. The service may outlive individual widgets,
    /// so this is best-effort fire-and-forget; widgets that are closed simply
    /// have no subscribers.
    /// </summary>
    private void PublishStoreChanged(
        string widgetId,
        TodoItem? changedItem,
        TodoItem? insertedItem)
    {
        if (TodoStoreChanged is null)
        {
            return;
        }

        void Raise()
        {
            try
            {
                TodoStoreChanged?.Invoke(widgetId, changedItem, insertedItem);
            }
            catch (Exception ex)
            {
                App.Log($"[TodoReminder] Store-changed subscriber failed: {ex.Message}");
            }
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            Raise();
            return;
        }

        _dispatcherQueue.TryEnqueue(Raise);
    }

    private TodoReminderNotification BuildNotification(IReadOnlyList<TodoReminderCandidate> candidates)
    {
        var first = candidates
            .OrderBy(candidate => candidate.Item.DueDate)
            .First();
        string title = _localizationService.T("Todo.Reminder.NotificationTitle");
        string dueText = FormatDueDate(first.Item.DueDate!.Value);
        string itemText = NormalizeNotificationText(first.Item.Text);
        string message = candidates.Count == 1
            ? _localizationService.Format("Todo.Reminder.NotificationSingle", itemText, dueText)
            : _localizationService.Format("Todo.Reminder.NotificationMultiple", candidates.Count, itemText, dueText);

        bool hasTodayDueItem = candidates.Any(candidate =>
            candidate.Item.DueDate is { } dueDate &&
            dueDate.ToLocalTime().Date == _clock().Date);

        return new TodoReminderNotification(
            title,
            message,
            candidates.Count,
            first.WidgetId,
            first.Item.Id,
            hasTodayDueItem);
    }

    private string FormatDueDate(DateTimeOffset dueDate)
    {
        DateTimeOffset localDueDate = dueDate.ToLocalTime();
        var today = _clock().Date;
        string time = localDueDate.Second == 0
            ? localDueDate.ToString("HH:mm")
            : localDueDate.ToString("HH:mm:ss");

        if (localDueDate.Date == today)
        {
            return _localizationService.Format("Todo.Due.TodayAt", time);
        }

        if (localDueDate.Date == today.AddDays(1))
        {
            return _localizationService.Format("Todo.Due.TomorrowAt", time);
        }

        return localDueDate.Second == 0
            ? localDueDate.ToString("yyyy/M/d HH:mm")
            : localDueDate.ToString("yyyy/M/d HH:mm:ss");
    }

    private string NormalizeNotificationText(string? text)
    {
        string normalized = string.Join(
            " ",
            (text ?? string.Empty)
                .Split(['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)
                .Select(part => part.Trim())
                .Where(part => !string.IsNullOrWhiteSpace(part)));

        if (string.IsNullOrWhiteSpace(normalized))
        {
            return _localizationService.T("Todo.Reminder.Untitled");
        }

        const int maxLength = 48;
        return normalized.Length <= maxLength
            ? normalized
            : $"{normalized[..maxLength]}...";
    }

    private static bool TryGetReminderTrigger(
        TodoItem item,
        DateTimeOffset now,
        int defaultOffsetMinutes,
        out ReminderTriggerKind triggerKind,
        out int? effectiveOffsetMinutes)
    {
        triggerKind = ReminderTriggerKind.Due;
        effectiveOffsetMinutes = null;

        if (item.IsCompleted || item.DueDate is null)
        {
            return false;
        }

        if (TodoReminderOptions.IsReminderOff(item.ReminderOffsetMinutes))
        {
            return false;
        }

        if (item.SnoozedUntil is { } snoozedUntil)
        {
            triggerKind = ReminderTriggerKind.Snooze;
            return now >= snoozedUntil;
        }

        int normalizedDefaultOffsetMinutes = SettingsService.NormalizeTodoReminderOffsetMinutes(defaultOffsetMinutes);
        effectiveOffsetMinutes = TodoReminderOptions.NormalizeOffsetMinutes(item.ReminderOffsetMinutes) ??
                                 normalizedDefaultOffsetMinutes;
        if (TodoReminderOptions.IsReminderOff(effectiveOffsetMinutes))
        {
            return false;
        }

        return ShouldNotifyDue(
            item,
            now,
            TimeSpan.FromMinutes(Math.Max(0, effectiveOffsetMinutes.Value)));
    }

    private static bool ShouldNotifyDue(TodoItem item, DateTimeOffset now, TimeSpan reminderOffset)
    {
        if (item.IsCompleted || item.DueDate is not { } dueDate)
        {
            return false;
        }

        if (item.ReminderDismissedForDueDate is { } dismissedForDueDate &&
            DateTimeOffset.Equals(dismissedForDueDate, dueDate))
        {
            return false;
        }

        DateTimeOffset reminderAt = dueDate - reminderOffset;
        if (now < reminderAt)
        {
            return false;
        }

        return now <= dueDate + MissedReminderGrace;
    }

    private static string GetReminderKey(
        string widgetId,
        TodoItem item,
        ReminderTriggerKind triggerKind,
        int? effectiveOffsetMinutes)
    {
        string dueKey = item.DueDate?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none";
        string triggerKey = triggerKind == ReminderTriggerKind.Snooze
            ? item.SnoozedUntil?.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "none"
            : effectiveOffsetMinutes?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "default";
        return $"{widgetId}:{item.Id}:{triggerKind}:{dueKey}:{triggerKey}";
    }

    private sealed record TodoReminderCandidate(
        string WidgetId,
        string WidgetName,
        TodoItem Item);
}
