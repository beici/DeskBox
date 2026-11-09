using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace DeskBox.Services;

public sealed class StoreStartupService : IStartupService
{
    private const string StartupTaskId = "DeskBoxStartupTask";

    // Cache the StartupTask handle to avoid repeated UI-thread blocking
    // calls to StartupTask.GetAsync() in sync contexts. Populated lazily
    // on first access, then reused for all subsequent calls.
    private StartupTask? _cachedTask;
    private readonly object _cacheLock = new();
    private Task? _enableRequestInFlight;

    // Set when Disable stops waiting for a still-settling enable request
    // (consent dialog left open past the wait bound): the off-intent could
    // not land yet, so the request's settle continuation applies it. A new
    // Enable clears it — the latest intent wins. Consumed atomically with
    // Interlocked so racing settle continuations apply it exactly once and
    // cannot act on a stale read after a newer Enable cleared it.
    private int _disableIntentAwaitingEnableSettle;

    public StartupRegistrationState GetState()
    {
        try
        {
            var task = GetCachedOrFreshTask();
            return task.State switch
            {
                StartupTaskState.Enabled => StartupRegistrationState.Enabled,
                StartupTaskState.EnabledByPolicy => StartupRegistrationState.Enabled,
                StartupTaskState.DisabledByUser => StartupRegistrationState.DisabledByUser,
                StartupTaskState.Disabled => StartupRegistrationState.NotRegistered,
                StartupTaskState.DisabledByPolicy => StartupRegistrationState.BlockedOrFailed,
                _ => StartupRegistrationState.BlockedOrFailed
            };
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Failed to query startup state: {ex.Message}");
            return StartupRegistrationState.BlockedOrFailed;
        }
    }

    public bool IsEnabled() => GetState() == StartupRegistrationState.Enabled;

    public string? GetRunValue() => null;

    public StartupOperationResult Enable()
    {
        try
        {
            var task = GetCachedOrFreshTask();
            if (task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy)
            {
                return new StartupOperationResult(StartupRegistrationState.Enabled);
            }

            if (task.State == StartupTaskState.Disabled)
            {
                // Fire-and-forget: RequestEnableAsync may show a consent dialog
                // that requires the UI thread. Blocking with GetAwaiter().GetResult()
                // would dead-lock the UI thread. The continuation refreshes the
                // cached handle only after the request settles — a synchronous
                // refresh here would still observe the pre-request Disabled
                // state and keep reporting stale results.
                Task request;
                lock (_cacheLock)
                {
                    // Start and publish inside the lock: registering only
                    // after the call returned would leave a gap where a
                    // concurrent Disable reads no pending request, skips its
                    // wait, and acts on the stale Disabled state while this
                    // enable is still landing. The new request also supersedes
                    // any off-intent a timed-out Disable wait deferred.
                    Volatile.Write(ref _disableIntentAwaitingEnableSettle, 0);
                    _enableRequestInFlight = null;
                    request = RequestEnableAndRefreshAsync(task);
                    _enableRequestInFlight = request;
                }
                _ = request;
                return new StartupOperationResult(StartupRegistrationState.Pending);
            }

            if (task.State == StartupTaskState.DisabledByUser)
            {
                const string message =
                    "Windows Startup apps has disabled the DeskBox startup task.";
                global::DeskBox.App.Log($"[StoreStartupService] {message}");
                return new StartupOperationResult(
                    StartupRegistrationState.DisabledByUser,
                    message);
            }

            string failure =
                $"StartupTask cannot be enabled from app state: {task.State}";
            global::DeskBox.App.Log($"[StoreStartupService] {failure}");
            return new StartupOperationResult(
                StartupRegistrationState.BlockedOrFailed,
                failure);
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Failed to enable startup: {ex.Message}");
            return new StartupOperationResult(
                StartupRegistrationState.BlockedOrFailed,
                ex.Message);
        }
    }

    public StartupOperationResult Disable()
    {
        try
        {
            // If an enable request is still settling (consent dialog open),
            // let it finish first: otherwise the stale Disabled handle makes
            // the check below skip task.Disable() and the pending enable
            // lands Enabled, contradicting the user's latest intent.
            WaitForPendingEnableRequest();
            var task = GetCachedOrFreshTask();
            if (task.State == StartupTaskState.Enabled)
            {
                task.Disable();
                // Disable() has no consent dialog, so the state change is
                // complete when it returns; swap in a fresh handle so the
                // result below (and later GetState calls) do not depend on
                // whether StartupTask.State updates in place.
                RefreshCache();
            }

            return new StartupOperationResult(GetState());
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Failed to disable startup: {ex.Message}");
            return new StartupOperationResult(
                StartupRegistrationState.BlockedOrFailed,
                ex.Message);
        }
    }

    public StartupOperationResult SetEnabled(bool enabled)
    {
        if (enabled)
        {
            return Enable();
        }

        return Disable();
    }

    /// <summary>
    /// Pre-fetches the StartupTask handle asynchronously so that subsequent
    /// synchronous callers do not block the UI thread. Invoked fire-and-forget
    /// from the UI thread during startup; the await resumes on the UI thread
    /// (or the thread pool when no UI context is posted) and only briefly
    /// takes the cache lock to publish the handle.
    /// </summary>
    internal async Task PrefetchTaskAsync()
    {
        try
        {
            var task = await StartupTask.GetAsync(StartupTaskId).AsTask();
            lock (_cacheLock)
            {
                _cachedTask = task;
            }
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Failed to prefetch startup task: {ex.Message}");
        }
    }

    /// <summary>
    /// Awaits the startup-task enable request, then replaces the cached
    /// handle with a fresh query so later GetState() calls observe the
    /// post-request state.
    /// </summary>
    private async Task RequestEnableAndRefreshAsync(StartupTask task)
    {
        try
        {
            var result = await task.RequestEnableAsync().AsTask();
            global::DeskBox.App.Log($"[StoreStartupService] StartupTask enable requested: {result}");
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] StartupTask enable failed: {ex.Message}");
        }

        try
        {
            var fresh = await StartupTask.GetAsync(StartupTaskId).AsTask();
            lock (_cacheLock)
            {
                _cachedTask = fresh;
            }
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Failed to refresh cached startup task: {ex.Message}");
        }
    }

    /// <summary>
    /// Synchronous cache refresh for callers that have already completed a
    /// state change (Disable). Uses the same pool-side wait as the
    /// cache-miss path in GetCachedOrFreshTask so a UI-thread caller never
    /// blocks on its own STA queue.
    /// </summary>
    private void RefreshCache()
    {
        try
        {
            var fresh = Task.Run(async () => await StartupTask.GetAsync(StartupTaskId).AsTask())
                .GetAwaiter()
                .GetResult();
            lock (_cacheLock)
            {
                _cachedTask = fresh;
            }
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Failed to refresh cached startup task after disable: {ex.Message}");
        }
    }

    /// <summary>
    /// Waits (bounded) for an in-flight enable request (consent dialog) to
    /// settle so a subsequent Disable observes the post-enable state.
    /// Pool-side wait: the enable continuation may need the UI thread, so
    /// blocking the caller's STA would dead-lock (same reasoning as
    /// GetCachedOrFreshTask). A request that outlives the bound defers the
    /// off-intent to the request's settle continuation instead of freezing
    /// the caller for as long as the dialog stays open.
    /// </summary>
    private void WaitForPendingEnableRequest()
    {
        Task? pending;
        lock (_cacheLock)
        {
            pending = _enableRequestInFlight;
        }

        if (pending is null || pending.IsCompleted)
        {
            return;
        }

        try
        {
            Task.Run(() => pending.WaitAsync(TimeSpan.FromSeconds(3)))
                .GetAwaiter()
                .GetResult();
        }
        catch (TimeoutException)
        {
            // A settings page frozen for as long as the dialog stays open is
            // worse than a late disable: proceed on the pre-enable state and
            // let the settle continuation re-apply the off-intent (a newer
            // Enable clears it — latest intent wins).
            Volatile.Write(ref _disableIntentAwaitingEnableSettle, 1);
            _ = DisableAfterEnableRequestSettlesAsync(pending);
        }
        catch
        {
            // RequestEnableAndRefreshAsync swallows its own failures; this
            // only observes the (never-throwing) task.
        }
    }

    private async Task DisableAfterEnableRequestSettlesAsync(Task enableRequest)
    {
        try
        {
            await enableRequest;
        }
        catch
        {
            // Same observation-only catch as WaitForPendingEnableRequest.
        }

        if (Interlocked.Exchange(ref _disableIntentAwaitingEnableSettle, 0) == 0)
        {
            return;
        }
        try
        {
            // Apply directly instead of re-entering Disable(): an enable
            // request registered after this off-intent must settle freely
            // and win, and Disable's wait would suppress it. The settled
            // request has already refreshed the cached handle, so this
            // observes the post-enable state.
            var task = GetCachedOrFreshTask();
            if (task.State == StartupTaskState.Enabled)
            {
                task.Disable();
                RefreshCache();
            }
        }
        catch (Exception ex)
        {
            global::DeskBox.App.Log($"[StoreStartupService] Deferred disable after enable settle failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Returns the cached StartupTask if available, otherwise fetches it
    /// synchronously. The cache is populated by PrefetchTaskAsync() during
    /// startup; falls back to the blocking call only on first access.
    /// </summary>
    private StartupTask GetCachedOrFreshTask()
    {
        lock (_cacheLock)
        {
            if (_cachedTask is not null)
            {
                return _cachedTask;
            }
        }

        // Cache miss: StartupTask is agile, so its completion is dispatched on
        // the thread pool and never needs the caller's STA pump. Running the
        // wait on the pool (same pattern as App.ApplyDefaultAutoStartOnce)
        // keeps a UI-thread caller from deadlocking on a blocked STA queue.
        // Known synchronous callers: SettingsViewModel construction,
        // PreferenceCallbacks (GetState/SetEnabled), DragDropPermissionService
        // (GetState/Enable), plus App.ApplyDefaultAutoStartOnce which already
        // runs inside Task.Run.
        var task = Task.Run(async () => await StartupTask.GetAsync(StartupTaskId).AsTask())
            .GetAwaiter()
            .GetResult();
        lock (_cacheLock)
        {
            _cachedTask = task;
        }
        return task;
    }
}
