using System.Text.Json;
using System.Text.Json.Serialization;

namespace DeskBox.Services;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    UseStringEnumConverter = true,
    WriteIndented = true)]
[JsonSerializable(
    typeof(MusicWidgetSettings),
    TypeInfoPropertyName = "Settings")]
internal sealed partial class MusicSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Per-kind settings store for the Music feature (pluginization roadmap
/// stage 2 pilot). Owns data/music/settings.json, deliberately separate from
/// AppSettings like GlanceWidgetStore. The three legacy AppSettings fields
/// (MusicUseArtworkBackdrop, MusicEnableCoverHoverMotion, MusicDisplayMode)
/// are copied here by Migration_9_To_10 and kept as a real mirror (persisted
/// via the global settings debounce) until the N+2 cleanup release removes
/// them.
///
/// Write-path design (batch E fix): the cache lock is a plain monitor lock
/// held only for in-memory operations, so UI-thread callers can never
/// deadlock on a pending disk write. Disk persistence happens outside the
/// lock, chained behind the previous write (FCFG-05: concurrent Updates must
/// reach the disk in the order they mutated the cache) and on pool threads,
/// so no write ever depends on the caller's synchronization context;
/// exceptions are observed and logged without breaking the chain. The legacy
/// AppSettings mirror is persisted through the global SaveDebounced by the
/// caller so both stores converge.
/// </summary>
public sealed class MusicSettingsStore
{
    private sealed class SharedState
    {
        internal readonly object SyncRoot = new();
        internal MusicWidgetSettings? Cached;
        internal Task WriteChain = Task.CompletedTask;
        internal volatile bool PersistFailed;
    }

    private static readonly object s_statesLock = new();
    private static readonly Dictionary<string, WeakReference<SharedState>> s_states =
        new(StringComparer.OrdinalIgnoreCase);

    private readonly SharedState _state;
    private readonly string _storePath;

    // Aggregate of every live instance's chain tail (the settings page, each
    // music widget view model, and migration temporaries each own an
    // instance). Shutdown's settings-flush step drains this so no music
    // write is lost regardless of which instance issued it. WhenAll never
    // faults because individual links never do.
    private static readonly object s_globalChainLock = new();
    private static Task s_globalWriteChain = Task.CompletedTask;

    public MusicSettingsStore()
        : this(Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory,
            "music"))
    {
    }

    internal MusicSettingsStore(string musicDataDirectory)
    {
        _storePath = Path.GetFullPath(Path.Combine(musicDataDirectory, "settings.json"));
        Directory.CreateDirectory(Path.GetDirectoryName(_storePath)!);
        lock (s_statesLock)
        {
            // A store or pending write keeps its state alive. Retired profile
            // and staging directories must not become process-lifetime caches.
            foreach (var entry in s_states.ToArray())
            {
                if (!entry.Value.TryGetTarget(out _))
                {
                    s_states.Remove(entry.Key);
                }
            }

            if (s_states.TryGetValue(_storePath, out var reference) &&
                reference.TryGetTarget(out var state))
            {
                _state = state;
            }
            else
            {
                _state = new SharedState();
                s_states[_storePath] = new WeakReference<SharedState>(_state);
            }
        }
    }

    internal string StorePath => _storePath;

    /// <summary>
    /// Synchronous load from the in-memory cache; the first call reads the
    /// tiny settings file from disk. Safe to call from UI property setters -
    /// the monitor lock is only held for memory operations and the initial
    /// sequential file read, never for an async write.
    /// </summary>
    public MusicWidgetSettings Load()
    {
        lock (_state.SyncRoot)
        {
            _state.Cached ??= LoadFromDisk();
            return Clone(_state.Cached);
        }
    }

    /// <summary>
    /// Updates the cached settings synchronously (UI-safe), then persists
    /// asynchronously with exception logging. The update action runs under
    /// the lock so concurrent updates serialize against each other, and the
    /// disk write is appended to the persistence chain under the same lock so
    /// concurrent Updates hit the disk in exactly the order they mutated the
    /// cache (FCFG-05). Writes run on pool threads so they never block
    /// callers.
    /// </summary>
    public void Update(Action<MusicWidgetSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (_state.SyncRoot)
        {
            _state.Cached ??= LoadFromDisk();
            update(_state.Cached);
            MusicWidgetSettings snapshot = Clone(_state.Cached);

            Task predecessor = _state.WriteChain;
            Task link = Task.Run(async () =>
            {
                try
                {
                    await predecessor;
                    await PersistAsync(snapshot);
                }
                catch (Exception ex)
                {
                    // PersistAsync already observes its own failures; this
                    // link-level guard keeps the chain unbroken even if the
                    // await on a predecessor ever surfaces one.
                    App.Log($"[MusicSettingsStore] Persist chain failed: {ex.Message}");
                }
            });
            _state.WriteChain = link;
            TrackGlobalLink(link);
        }
    }

    /// <summary>
    /// Blocks until every write enqueued so far has finished. The chain runs
    /// on pool threads (Task.Run), so a caller on any thread — including the
    /// UI thread during the synchronous migration step — waits without
    /// deadlock. The chain never faults (failures are logged by the links),
    /// so this rethrows nothing; it returns <see langword="false"/> when any
    /// queued write failed, so a caller that gates a durable checkpoint on the
    /// write can refuse to advance (R21-S7-02).
    /// </summary>
    internal bool WaitForPendingPersist()
    {
        Task chain;
        lock (_state.SyncRoot)
        {
            chain = _state.WriteChain;
        }

        chain.GetAwaiter().GetResult();
        return !_state.PersistFailed;
    }

    /// <summary>
    /// Blocks until every write enqueued by every live store instance has
    /// finished. Called from the shutdown settings-flush step (R2 ⑨-2), which
    /// previously drained Pomodoro/Screenshot/SearchHistory but missed the
    /// music store's instance chains. Pool-thread links, so any caller waits
    /// without deadlock.
    /// </summary>
    internal static void WaitForAllPendingPersists()
    {
        Task chain;
        lock (s_globalChainLock)
        {
            chain = s_globalWriteChain;
        }

        chain.GetAwaiter().GetResult();
    }

    private static void TrackGlobalLink(Task link)
    {
        lock (s_globalChainLock)
        {
            s_globalWriteChain = Task.WhenAll(s_globalWriteChain, link);
        }
    }

    public async Task SaveAsync(MusicWidgetSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Task link;
        lock (_state.SyncRoot)
        {
            _state.Cached = Normalize(Clone(settings));
            MusicWidgetSettings snapshot = Clone(_state.Cached);

            // Routed through the write chain exactly like Update (FCFG-05):
            // a direct PersistAsync here could interleave with a concurrent
            // Update link and File.Replace an older snapshot over a newer
            // one. Awaiting the link keeps SaveAsync's contract that the
            // mutation is durable when it returns.
            Task predecessor = _state.WriteChain;
            _state.WriteChain = link = Task.Run(async () =>
            {
                try
                {
                    await predecessor;
                    await PersistAsync(snapshot);
                }
                catch (Exception ex)
                {
                    App.Log($"[MusicSettingsStore] Persist chain failed: {ex.Message}");
                }
            });
            TrackGlobalLink(link);
        }

        await link;
    }

    private async Task PersistAsync(MusicWidgetSettings snapshot)
    {
        try
        {
            await ResilientJsonStore.SaveAsync(
                _storePath,
                JsonSerializer.Serialize(snapshot, MusicSettingsJsonContext.Default.Settings));
            _state.PersistFailed = false;
        }
        catch (Exception ex)
        {
            // Logged then rethrown so the failure reaches the chain link's
            // own guard; anything the link runs after this write must only
            // happen for a snapshot that actually reached the disk.
            _state.PersistFailed = true;
            App.Log($"[MusicSettingsStore] Failed to persist '{_storePath}': {ex.Message}");
            throw;
        }
    }

    /// <summary>
    /// Direct synchronous disk read for the first cache fill. Reads the
    /// primary file and falls back to the .bak, then to defaults - a sync
    /// bootstrap that avoids the ResilientJsonStore async pipeline (and its
    /// SemaphoreSlim) on the UI thread.
    /// </summary>
    private MusicWidgetSettings LoadFromDisk()
    {
        if (File.Exists(_storePath))
        {
            try
            {
                return Normalize(JsonSerializer.Deserialize(
                    File.ReadAllText(_storePath),
                    MusicSettingsJsonContext.Default.Settings));
            }
            catch (Exception ex)
            {
                App.Log($"[MusicSettingsStore] Primary load failed, trying backup: {ex.Message}");
            }

            string backupPath = ResilientJsonStore.GetBackupPath(_storePath);
            if (File.Exists(backupPath))
            {
                try
                {
                    return Normalize(JsonSerializer.Deserialize(
                        File.ReadAllText(backupPath),
                        MusicSettingsJsonContext.Default.Settings));
                }
                catch (Exception ex)
                {
                    App.Log($"[MusicSettingsStore] Backup load failed, using defaults: {ex.Message}");
                }
            }
        }

        return new MusicWidgetSettings();
    }

    internal static MusicWidgetSettings Normalize(MusicWidgetSettings? settings)
    {
        settings ??= new MusicWidgetSettings();
        if (settings.SchemaVersion < 1)
        {
            settings.SchemaVersion = 1;
        }

        settings.DisplayMode = SettingsService.NormalizeMusicDisplayMode(settings.DisplayMode);
        return settings;
    }

    private static MusicWidgetSettings Clone(MusicWidgetSettings settings) => new()
    {
        SchemaVersion = settings.SchemaVersion,
        UseArtworkBackdrop = settings.UseArtworkBackdrop,
        EnableCoverHoverMotion = settings.EnableCoverHoverMotion,
        DisplayMode = settings.DisplayMode,
    };
}

/// <summary>
/// Music feature settings (the three fields migrated out of AppSettings by
/// schema version 10). Kept inside the store file so the feature inventory
/// grows by exactly one file.
/// </summary>
public sealed class MusicWidgetSettings
{
    public int SchemaVersion { get; set; } = 1;

    public bool UseArtworkBackdrop { get; set; } = true;

    public bool EnableCoverHoverMotion { get; set; } = true;

    public string DisplayMode { get; set; } = SettingsService.MusicDisplayModeAuto;
}
