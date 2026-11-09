using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Models;

namespace DeskBox.Services;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(
    typeof(PomodoroWidgetSettings),
    TypeInfoPropertyName = "Settings")]
internal sealed partial class PomodoroSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Per-kind settings store for the Pomodoro feature, cloned from the
/// MusicSettingsStore stage-2 pilot: owns data/pomodoro/settings.json and
/// deliberately stays outside the frozen AppSettings schema, so the widget
/// adds no settings migration. The file is a flat six-field snapshot with
/// defaults on every property, so future fields migrate by absence alone and
/// no schema version marker is required.
///
/// Write-path design (mirrors MusicSettingsStore): the monitor lock is held
/// only for in-memory work; persistence runs on pool threads chained behind
/// the previous write so concurrent Updates reach the disk in mutation
/// order and callers never block on I/O. Unlike the music pilot the chain
/// tail is static: the production writers are private per-surface instances
/// the shutdown sequence cannot reach, so a static tail is the only handle
/// the settings-flush step has on their pending disk writes.
///
/// Deviation from the music pilot: pomodoro settings must refresh a live
/// running timer, so every successful mutation raises the static
/// <see cref="Changed"/> event. Widget view models own private store
/// instances, so the notification is process-wide rather than per-instance.
/// </summary>
public sealed class PomodoroSettingsStore
{
    internal const int MinRoundCount = 1;
    internal const int MaxRoundCount = 12;
    internal const int MinFocusMinutes = 1;
    internal const int MaxFocusMinutes = 120;
    internal const int MinBreakMinutes = 1;
    internal const int MaxBreakMinutes = 60;

    /// <summary>
    /// Per-field clamp helpers (the bupocc PomodoroSettingsPolicy surface,
    /// relocated onto this store): the settings section NumberBoxes validate
    /// input against the same bounds the state machine enforces.
    /// </summary>
    internal static int NormalizeRoundCount(int value) =>
        Math.Clamp(value, MinRoundCount, MaxRoundCount);
    internal static int NormalizeFocusMinutes(int value) =>
        Math.Clamp(value, MinFocusMinutes, MaxFocusMinutes);
    internal static int NormalizeShortBreakMinutes(int value) =>
        Math.Clamp(value, MinBreakMinutes, MaxBreakMinutes);
    internal static int NormalizeLongBreakMinutes(int value) =>
        Math.Clamp(value, MinBreakMinutes, MaxBreakMinutes);

    private readonly object _lock = new();
    private readonly string _storePath;
    private PomodoroWidgetSettings? _cached;
    private int _cachedVersion = -1;

    // Tail of the persistence chain; see the class doc for why it is static.
    // Reads and appends happen under s_chainLock (nested inside _lock, see
    // Update). The chain runs on pool threads and never faults — PersistAsync
    // logs its own failures and every link re-observes its predecessor.
    private static readonly object s_chainLock = new();
    private static Task s_writeChain = Task.CompletedTask;

    // Bumped by every committed mutation in ANY store instance; an instance
    // whose cached snapshot predates the current version reloads from disk.
    // This is what makes the static Changed event usable across the separate
    // production instances (settings section, widget view models, window):
    // without it each instance would serve its first-read snapshot forever.
    private static int s_dataVersion;

    /// <summary>
    /// Raised after any store instance commits a mutation (settings section
    /// save or programmatic update) so running pomodoro widgets can re-read
    /// their timings. Handlers run on the mutating thread and must marshal
    /// themselves; a throwing handler is logged and does not break others.
    /// </summary>
    public static event Action? Changed;

    public PomodoroSettingsStore()
        : this(Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory,
            "pomodoro"))
    {
    }

    internal PomodoroSettingsStore(string pomodoroDataDirectory)
    {
        Directory.CreateDirectory(pomodoroDataDirectory);
        _storePath = Path.Combine(pomodoroDataDirectory, "settings.json");
    }

    internal string StorePath => _storePath;

    /// <summary>
    /// Synchronous load from the in-memory cache; the first call reads the
    /// tiny settings file from disk. Safe from UI property setters — the
    /// lock covers only memory work and the initial sequential read.
    /// </summary>
    public PomodoroWidgetSettings Load()
    {
        lock (_lock)
        {
            EnsureCacheLoadedLocked();
            return Clone(_cached!);
        }
    }

    /// <summary>
    /// Updates the cached settings synchronously (UI-safe), persists
    /// asynchronously on the write chain, and raises <see cref="Changed"/>
    /// once the mutation is durable.
    /// </summary>
    public void Update(Action<PomodoroWidgetSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_lock)
        {
            EnsureCacheLoadedLocked();
            update(_cached!);
            Normalize(_cached!);
            PomodoroWidgetSettings snapshot = Clone(_cached!);

            // Enqueue inside _lock so cache-mutation order equals enqueue
            // order (FCFG-05). The only lock nesting in the store is _lock
            // -> s_chainLock and it cannot deadlock: chain links never take
            // s_chainLock, nobody blocks while holding it, and a link that
            // re-enters _lock (AdvanceVersionAndNotify) only stalls its own
            // pool thread — the enqueueing thread leaves both locks without
            // ever waiting on a link.
            lock (s_chainLock)
            {
                Task predecessor = s_writeChain;
                s_writeChain = Task.Run(async () =>
                {
                    try
                    {
                        await predecessor;
                        await PersistAsync(snapshot);
                        // Advanced only after the snapshot is on disk, then
                        // raised, so reloading instances see the new file.
                        AdvanceVersionAndNotify();
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[PomodoroSettingsStore] Persist chain failed: {ex.Message}");
                    }
                });
            }
        }
    }

    /// <summary>
    /// Reloads the cached snapshot when it predates the current shared
    /// version. The version is advanced (see <see cref="SaveAsync"/> and the
    /// Update write chain) only AFTER a snapshot reaches the disk, so a
    /// version always describes the file's state and a load inside a write
    /// window can never stamp a version newer than the bytes it read.
    /// </summary>
    private void EnsureCacheLoadedLocked()
    {
        int version = Volatile.Read(ref s_dataVersion);
        if (_cached is null || _cachedVersion != version)
        {
            _cached = LoadFromDisk();
            _cachedVersion = version;
        }
    }

    /// <summary>
    /// Blocks until every write enqueued so far — by any store instance —
    /// has finished; test-only convenience with the same non-deadlock
    /// guarantees as the music store: the chain runs on pool threads
    /// (Task.Run), so a caller on any thread waits without deadlock, and it
    /// never faults, so this rethrows nothing.
    /// </summary>
    internal void WaitForPendingPersist()
    {
        Task chain;
        lock (s_chainLock)
        {
            chain = s_writeChain;
        }

        chain.GetAwaiter().GetResult();
    }

    /// <summary>
    /// Shutdown settings-flush entry point: drains the shared chain without
    /// any store instance in hand. The production writers are per-surface
    /// instances the shutdown sequence has no reference to, so this static
    /// drain is what makes the last settings write survive exit. Same
    /// non-deadlock guarantee as <see cref="WaitForPendingPersist"/>.
    /// </summary>
    internal static void WaitForAllPendingPersists()
    {
        Task chain;
        lock (s_chainLock)
        {
            chain = s_writeChain;
        }

        chain.GetAwaiter().GetResult();
    }

    public async Task SaveAsync(PomodoroWidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Task link;
        lock (_lock)
        {
            _cached = Normalize(Clone(settings));
            PomodoroWidgetSettings snapshot = Clone(_cached);

            // Routed through the shared write chain exactly like Update
            // (FCFG-05): a direct PersistAsync here could interleave with a
            // concurrent Update link and File.Replace an older snapshot
            // over a newer one. Awaiting the link keeps SaveAsync's
            // contract that the mutation is durable when it returns.
            lock (s_chainLock)
            {
                Task predecessor = s_writeChain;
                s_writeChain = link = Task.Run(async () =>
                {
                    try
                    {
                        await predecessor;
                        await PersistAsync(snapshot);
                        // Advanced only after the snapshot is on disk, then
                        // raised, so reloading instances see the new file.
                        AdvanceVersionAndNotify();
                    }
                    catch (Exception ex)
                    {
                        App.Log($"[PomodoroSettingsStore] Persist chain failed: {ex.Message}");
                    }
                });
            }
        }

        await link;
    }

    /// <summary>
    /// Called after a snapshot has been persisted: advances the shared
    /// version so other instances reload, re-stamps this instance's cache,
    /// and notifies subscribers. Keeping the bump behind the persist is what
    /// prevents version-stamp poisoning (a reader caching old bytes under a
    /// new version during the write window).
    /// </summary>
    private void AdvanceVersionAndNotify()
    {
        int version = Interlocked.Increment(ref s_dataVersion);
        lock (_lock)
        {
            _cachedVersion = version;
        }

        OnChanged();
    }

    private async Task PersistAsync(PomodoroWidgetSettings snapshot)
    {
        try
        {
            await ResilientJsonStore.SaveAsync(
                _storePath,
                JsonSerializer.Serialize(snapshot, PomodoroSettingsJsonContext.Default.Settings));
        }
        catch (Exception ex)
        {
            // Logged then rethrown: the version bump and Changed event that
            // follow this write in the chain link must only run for a
            // snapshot that actually reached the disk.
            App.Log($"[PomodoroSettingsStore] Failed to persist '{_storePath}': {ex.Message}");
            throw;
        }
    }

    private PomodoroWidgetSettings LoadFromDisk()
    {
        if (File.Exists(_storePath))
        {
            try
            {
                return Normalize(JsonSerializer.Deserialize(
                    File.ReadAllText(_storePath),
                    PomodoroSettingsJsonContext.Default.Settings));
            }
            catch (Exception ex)
            {
                App.Log($"[PomodoroSettingsStore] Primary load failed, trying backup: {ex.Message}");
            }

            string backupPath = ResilientJsonStore.GetBackupPath(_storePath);
            if (File.Exists(backupPath))
            {
                try
                {
                    return Normalize(JsonSerializer.Deserialize(
                        File.ReadAllText(backupPath),
                        PomodoroSettingsJsonContext.Default.Settings));
                }
                catch (Exception ex)
                {
                    App.Log($"[PomodoroSettingsStore] Backup load failed, using defaults: {ex.Message}");
                }
            }
        }

        return new PomodoroWidgetSettings();
    }

    /// <summary>
    /// Clamps every field to the bounds enforced by the timer state machine
    /// (rounds 1-12, focus 1-120, breaks 1-60) so a hand-edited file can
    /// never produce an unusable timer.
    /// </summary>
    internal static PomodoroWidgetSettings Normalize(PomodoroWidgetSettings? settings)
    {
        settings ??= new PomodoroWidgetSettings();
        settings.RoundCount = Math.Clamp(
            settings.RoundCount,
            MinRoundCount,
            MaxRoundCount);
        settings.FocusMinutes = Math.Clamp(
            settings.FocusMinutes,
            MinFocusMinutes,
            MaxFocusMinutes);
        settings.ShortBreakMinutes = Math.Clamp(
            settings.ShortBreakMinutes,
            MinBreakMinutes,
            MaxBreakMinutes);
        settings.LongBreakMinutes = Math.Clamp(
            settings.LongBreakMinutes,
            MinBreakMinutes,
            MaxBreakMinutes);
        return settings;
    }

    private static void OnChanged()
    {
        Action? handlers = Changed;
        if (handlers is null)
        {
            return;
        }

        foreach (Action handler in handlers.GetInvocationList().Cast<Action>())
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                App.Log($"[PomodoroSettingsStore] Changed handler failed: {ex.Message}");
            }
        }
    }

    private static PomodoroWidgetSettings Clone(PomodoroWidgetSettings settings) => new()
    {
        RoundCount = settings.RoundCount,
        FocusMinutes = settings.FocusMinutes,
        ShortBreakMinutes = settings.ShortBreakMinutes,
        LongBreakMinutes = settings.LongBreakMinutes,
        CompletionSoundEnabled = settings.CompletionSoundEnabled,
        CompletionNotificationEnabled = settings.CompletionNotificationEnabled,
    };
}
