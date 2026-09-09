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
                // would dead-lock the UI thread.
                _ = task.RequestEnableAsync().AsTask().ContinueWith(t =>
                {
                    if (t.IsCompletedSuccessfully)
                    {
                        global::DeskBox.App.Log($"[StoreStartupService] StartupTask enable requested: {t.Result}");
                    }
                    else if (t.IsFaulted)
                    {
                        global::DeskBox.App.Log($"[StoreStartupService] StartupTask enable failed: {t.Exception?.GetBaseException()?.Message}");
                    }
                }, TaskScheduler.Default);
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
            var task = GetCachedOrFreshTask();
            if (task.State == StartupTaskState.Enabled)
            {
                task.Disable();
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

        // Cache miss: fetch synchronously (blocks UI thread, but only on
        // first access before PrefetchTaskAsync completes).
        var task = StartupTask.GetAsync(StartupTaskId).AsTask().GetAwaiter().GetResult();
        lock (_cacheLock)
        {
            _cachedTask = task;
        }
        return task;
    }
}
