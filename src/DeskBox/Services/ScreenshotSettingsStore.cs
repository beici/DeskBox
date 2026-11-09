using System.Text.Json;
using System.Text.Json.Serialization;
using DeskBox.Models;

namespace DeskBox.Services;

[JsonSourceGenerationOptions(
    GenerationMode = JsonSourceGenerationMode.Metadata,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true)]
[JsonSerializable(
    typeof(ScreenshotSettings),
    TypeInfoPropertyName = "Settings")]
internal sealed partial class ScreenshotSettingsJsonContext : JsonSerializerContext
{
}

/// <summary>
/// Per-feature settings store for the screenshot capture feature, cloned
/// from the PomodoroSettingsStore/MusicSettingsStore pilot line: owns
/// data/screenshot/settings.json and deliberately stays outside the frozen
/// AppSettings schema, so the feature adds no settings migration. The file
/// is a flat three-field snapshot with defaults on every property, so
/// future fields migrate by absence alone.
///
/// Write-path design (mirrors the pilots): the monitor lock is held only
/// for in-memory work; persistence runs on pool threads chained behind the
/// previous write so concurrent Updates reach the disk in mutation order.
/// Unlike the music pilot the chain tail is static: the production writers
/// are private per-surface instances the shutdown sequence cannot reach, so
/// a static tail is the only handle the settings-flush step has on their
/// pending disk writes.
/// The static <see cref="Changed"/> event lets the hotkey service refresh
/// its registration when the gesture or enable toggle changes.
/// </summary>
public sealed class ScreenshotSettingsStore
{
    internal const int DefaultHotkeyModifiers =
        (int)HotkeyModifierKeys.Control | (int)HotkeyModifierKeys.Alt;
    internal const int DefaultHotkeyKey = 0x53;

    /// <summary>All modifier bits the app recognizes (see HotkeyModifierKeys).</summary>
    private const int KnownModifierMask =
        (int)(HotkeyModifierKeys.Alt
            | HotkeyModifierKeys.Control
            | HotkeyModifierKeys.Shift
            | HotkeyModifierKeys.Windows);

    private readonly string _storePath;
    private ScreenshotSettings? _cached;
    private int _cachedVersion = -1;

    // Cache mutations and queue publication share one lock. A pending snapshot
    // is authoritative for every instance of its path until its write finishes.
    private static readonly object s_chainLock = new();
    private static Task s_writeChain = Task.CompletedTask;
    private static readonly Dictionary<string, ScreenshotSettings> s_pendingSnapshots =
        new(StringComparer.OrdinalIgnoreCase);

    // Bumped by every committed mutation in ANY store instance; an instance
    // whose cached snapshot predates the current version reloads from disk.
    // Without this the settings section's instance and the hotkey service's
    // instance would each serve their first-read snapshot forever, and the
    // Changed-driven registration refresh would re-register a stale gesture.
    private static int s_dataVersion;

    public static event Action? Changed;

    public ScreenshotSettingsStore()
        : this(Path.Combine(
            DeskBoxDataPathService.Current.DataDirectory,
            "screenshot"))
    {
    }

    internal ScreenshotSettingsStore(string screenshotDataDirectory)
    {
        Directory.CreateDirectory(screenshotDataDirectory);
        _storePath = Path.GetFullPath(Path.Combine(screenshotDataDirectory, "settings.json"));
    }

    internal string StorePath => _storePath;

    public ScreenshotSettings Load()
    {
        lock (s_chainLock)
        {
            EnsureCacheLoadedLocked();
            return Clone(_cached!);
        }
    }

    public void Update(Action<ScreenshotSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);
        lock (s_chainLock)
        {
            EnsureCacheLoadedLocked();
            update(_cached!);
            Normalize(_cached!);
            ScreenshotSettings snapshot = Clone(_cached!);

            EnqueuePersistLocked(snapshot);
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
        if (s_pendingSnapshots.TryGetValue(_storePath, out ScreenshotSettings? pending))
        {
            _cached = Clone(pending);
            // When the pending snapshot retires, reload the committed bytes.
            _cachedVersion = -1;
            return;
        }

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

    public async Task SaveAsync(ScreenshotSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        Task link;
        lock (s_chainLock)
        {
            _cached = Normalize(Clone(settings));
            link = EnqueuePersistLocked(Clone(_cached));
        }

        await link;
    }

    private Task EnqueuePersistLocked(ScreenshotSettings snapshot)
    {
        s_pendingSnapshots[_storePath] = snapshot;
        Task predecessor = s_writeChain;
        return s_writeChain = Task.Run(async () =>
        {
            bool persisted = false;
            try
            {
                await predecessor;
                await PersistAsync(snapshot);
                persisted = true;
            }
            catch (Exception ex)
            {
                App.Log($"[ScreenshotSettingsStore] Persist chain failed: {ex.Message}");
            }
            finally
            {
                lock (s_chainLock)
                {
                    if (persisted)
                    {
                        Interlocked.Increment(ref s_dataVersion);
                    }

                    // An earlier completion must not discard a newer queued edit.
                    if (s_pendingSnapshots.TryGetValue(_storePath, out var pending) &&
                        ReferenceEquals(pending, snapshot))
                    {
                        s_pendingSnapshots.Remove(_storePath);
                    }
                }
            }

            if (persisted)
            {
                OnChanged();
            }
        });
    }

    private async Task PersistAsync(ScreenshotSettings snapshot)
    {
        try
        {
            await ResilientJsonStore.SaveAsync(
                _storePath,
                JsonSerializer.Serialize(snapshot, ScreenshotSettingsJsonContext.Default.Settings));
        }
        catch (Exception ex)
        {
            // Logged then rethrown: the version bump and Changed event that
            // follow this write in the chain link must only run for a
            // snapshot that actually reached the disk.
            App.Log($"[ScreenshotSettingsStore] Failed to persist '{_storePath}': {ex.Message}");
            throw;
        }
    }

    private ScreenshotSettings LoadFromDisk()
    {
        if (File.Exists(_storePath))
        {
            try
            {
                return Normalize(JsonSerializer.Deserialize(
                    File.ReadAllText(_storePath),
                    ScreenshotSettingsJsonContext.Default.Settings));
            }
            catch (Exception ex)
            {
                App.Log($"[ScreenshotSettingsStore] Primary load failed, trying backup: {ex.Message}");
            }

            string backupPath = ResilientJsonStore.GetBackupPath(_storePath);
            if (File.Exists(backupPath))
            {
                try
                {
                    return Normalize(JsonSerializer.Deserialize(
                        File.ReadAllText(backupPath),
                        ScreenshotSettingsJsonContext.Default.Settings));
                }
                catch (Exception ex)
                {
                    App.Log($"[ScreenshotSettingsStore] Backup load failed, using defaults: {ex.Message}");
                }
            }
        }

        return new ScreenshotSettings();
    }

    /// <summary>
    /// Masks unknown modifier bits off a hand-edited file; deep gesture
    /// validation (reserved chords, risky combos) happens at registration
    /// time in the hotkey service, which falls back to defaults + log.
    /// </summary>
    internal static ScreenshotSettings Normalize(ScreenshotSettings? settings)
    {
        settings ??= new ScreenshotSettings();
        settings.HotkeyModifiers &= KnownModifierMask;
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
                App.Log($"[ScreenshotSettingsStore] Changed handler failed: {ex.Message}");
            }
        }
    }

    private static ScreenshotSettings Clone(ScreenshotSettings settings) => new()
    {
        HotkeyEnabled = settings.HotkeyEnabled,
        HotkeyModifiers = settings.HotkeyModifiers,
        HotkeyKey = settings.HotkeyKey,
    };
}
