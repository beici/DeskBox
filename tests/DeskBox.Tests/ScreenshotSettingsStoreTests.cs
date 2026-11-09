using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Store-level coverage for the screenshot feature settings, mirroring the
/// PomodoroSettingsStore tests: the feature keeps its hotkey configuration
/// in data/screenshot/settings.json (per-feature store pilot pattern)
/// instead of AppSettings, so the surface under test is persistence, the
/// write chain, and modifier-bit normalization.
/// </summary>
public sealed class ScreenshotSettingsStoreTests
{
    private static string CreateStoreDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            $"screenshot-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Defaults_AreEnabledWithCtrlAltS()
    {
        var settings = new ScreenshotSettings();

        Assert.True(settings.HotkeyEnabled);
        Assert.Equal(
            (int)(HotkeyModifierKeys.Control | HotkeyModifierKeys.Alt),
            settings.HotkeyModifiers);
        Assert.Equal(0x53, settings.HotkeyKey);

        // Normalize must not move a healthy snapshot.
        ScreenshotSettings normalized = ScreenshotSettingsStore.Normalize(settings)!;
        Assert.Equal(settings.HotkeyModifiers, normalized.HotkeyModifiers);
        Assert.Equal(settings.HotkeyKey, normalized.HotkeyKey);
    }

    [Fact]
    public void Normalize_MasksUnknownModifierBits()
    {
        var settings = new ScreenshotSettings
        {
            HotkeyModifiers = unchecked((int)0xFFFF_FFFF),
            HotkeyKey = 0x53
        };

        ScreenshotSettingsStore.Normalize(settings);

        Assert.Equal(
            (int)(HotkeyModifierKeys.Alt
                | HotkeyModifierKeys.Control
                | HotkeyModifierKeys.Shift
                | HotkeyModifierKeys.Windows),
            settings.HotkeyModifiers);
    }

    [Fact]
    public void Load_OnAMissingFile_ReturnsDefaults()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var store = new ScreenshotSettingsStore(directory);

            ScreenshotSettings settings = store.Load();

            Assert.True(settings.HotkeyEnabled);
            Assert.Equal(0x53, settings.HotkeyKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Update_PersistsAndRaisesChanged()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var store = new ScreenshotSettingsStore(directory);
            int changedCalls = 0;
            ScreenshotSettingsStore.Changed += OnChanged;
            try
            {
                store.Update(settings =>
                {
                    settings.HotkeyEnabled = false;
                    settings.HotkeyModifiers = (int)HotkeyModifierKeys.Shift;
                    settings.HotkeyKey = 0x51;
                });

                // Changed is raised on the write chain once the snapshot is
                // persisted, so reloading instances see the new file; keep the
                // subscription alive across the chain drain.
                store.WaitForPendingPersist();
            }
            finally
            {
                ScreenshotSettingsStore.Changed -= OnChanged;
            }

            Assert.Equal(1, changedCalls);

            // A second store instance reads the persisted snapshot.
            var reloaded = new ScreenshotSettingsStore(directory);
            ScreenshotSettings settings = reloaded.Load();
            Assert.False(settings.HotkeyEnabled);
            Assert.Equal((int)HotkeyModifierKeys.Shift, settings.HotkeyModifiers);
            Assert.Equal(0x51, settings.HotkeyKey);

            void OnChanged() => changedCalls++;
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_OnAPreWarmedInstance_PicksUpChangesCommittedByAnotherInstance()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var writer = new ScreenshotSettingsStore(directory);
            var reader = new ScreenshotSettingsStore(directory);
            Assert.Equal(0x53, reader.Load().HotkeyKey); // warm the reader's cache

            writer.Update(settings => settings.HotkeyKey = 0x51);
            writer.WaitForPendingPersist();

            // The settings section and the hotkey service hold separate store
            // instances; the refresh they do on Changed must actually reload,
            // not keep serving the warmed first-read snapshot.
            Assert.Equal(0x51, reader.Load().HotkeyKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_OnACorruptedFile_FallsBackToBackupThenDefaults()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var store = new ScreenshotSettingsStore(directory);
            // The first save creates the primary file with a plain move; the
            // backup only materializes when the second save rolls the prior
            // primary into settings.json.bak via File.Replace.
            store.Update(settings => settings.HotkeyKey = 0x51);
            store.WaitForPendingPersist();
            store.Update(settings => settings.HotkeyEnabled = false);
            store.WaitForPendingPersist();
            string backupPath = ResilientJsonStore.GetBackupPath(
                Path.Combine(directory, "settings.json"));
            Assert.True(File.Exists(backupPath));

            File.WriteAllText(Path.Combine(directory, "settings.json"), "{ not json");

            var fresh = new ScreenshotSettingsStore(directory);
            ScreenshotSettings settings = fresh.Load();

            // The backup holds the first snapshot: key 0x51, still enabled.
            Assert.Equal(0x51, settings.HotkeyKey);
            Assert.True(settings.HotkeyEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Load_DuringWriteWindow_DoesNotPoisonLaterReloads()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var writer = new ScreenshotSettingsStore(directory);
            var reader = new ScreenshotSettingsStore(directory);
            Assert.Equal(0x53, reader.Load().HotkeyKey);

            writer.Update(settings => settings.HotkeyKey = 0x51);

            // A load racing the in-flight persist may legitimately observe
            // the OLD file, but it must never stamp those bytes as current:
            // once the chain drains, the next load has to see the new value.
            _ = reader.Load();
            writer.WaitForPendingPersist();

            Assert.Equal(0x51, reader.Load().HotkeyKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void StaticDrain_DrainsAnInstanceWriteWithoutAnInstanceWait()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var writer = new ScreenshotSettingsStore(directory);
            writer.Update(settings => settings.HotkeyKey = 0x47);

            // The shutdown settings-flush step holds no writer instance, so
            // it can only drain the static chain; once that returns, the
            // write must already be durable for a fresh reader — the exit
            // path where the last settings write used to be lost.
            ScreenshotSettingsStore.WaitForAllPendingPersists();

            var reader = new ScreenshotSettingsStore(directory);
            Assert.Equal(0x47, reader.Load().HotkeyKey);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PendingMutations_AreVisibleAcrossInstancesAndPreserveOtherFields(bool saveFirst)
    {
        string directory = CreateStoreDirectory();
        string blockerDirectory = CreateStoreDirectory();
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        int blockOnce = 0;
        // Changed is synchronous on the shared persistence queue. Hold one
        // completed unrelated write there to exercise a deterministic pending
        // window without relying on disk speed or private implementation state.
        void HoldQueue()
        {
            if (Interlocked.Exchange(ref blockOnce, 1) == 0)
            {
                entered.Set();
                release.Wait(TimeSpan.FromSeconds(15));
            }
        }

        ScreenshotSettingsStore.Changed += HoldQueue;
        try
        {
            var blocker = new ScreenshotSettingsStore(blockerDirectory);
            blocker.Update(settings => settings.HotkeyKey = 0x42);
            Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
            var writer = new ScreenshotSettingsStore(directory);
            var reader = new ScreenshotSettingsStore(Path.Combine(directory, "."));
            _ = writer.Load();
            _ = reader.Load();

            Task save = Task.CompletedTask;
            if (saveFirst)
            {
                save = writer.SaveAsync(new ScreenshotSettings { HotkeyKey = 0x51 });
            }
            else
            {
                writer.Update(settings => settings.HotkeyKey = 0x51);
            }

            Assert.Equal(0x51, reader.Load().HotkeyKey);
            reader.Update(settings => settings.HotkeyEnabled = false);
            Assert.False(writer.Load().HotkeyEnabled);
            Assert.Equal(0x51, writer.Load().HotkeyKey);
            Assert.Equal(0x42, blocker.Load().HotkeyKey);
            release.Set();
            await save;
            ScreenshotSettingsStore.WaitForAllPendingPersists();
            using var persisted = System.Text.Json.JsonDocument.Parse(
                await File.ReadAllTextAsync(Path.Combine(directory, "settings.json")));
            Assert.Equal(0x51, persisted.RootElement.GetProperty("hotkeyKey").GetInt32());
            Assert.False(persisted.RootElement.GetProperty("hotkeyEnabled").GetBoolean());
        }
        finally
        {
            release.Set();
            ScreenshotSettingsStore.WaitForAllPendingPersists();
            ScreenshotSettingsStore.Changed -= HoldQueue;
            Directory.Delete(directory, recursive: true);
            Directory.Delete(blockerDirectory, recursive: true);
        }
    }
}
