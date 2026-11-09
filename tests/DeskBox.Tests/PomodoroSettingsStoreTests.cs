using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Store-level port of bupocc's PomodoroSettingsPolicyTests: this fork keeps
/// pomodoro timings in data/pomodoro/settings.json (MusicSettingsStore pilot
/// pattern) instead of AppSettings, so the policy surface under test is
/// <see cref="PomodoroSettingsStore"/> normalization plus its write chain.
/// bupocc's two LegacyPomodoroBreakMinutes migration tests are intentionally
/// dropped — that legacy field belongs to bupocc's own settings history and
/// never existed in this fork.
/// </summary>
public sealed class PomodoroSettingsStoreTests
{
    private static string CreateStoreDirectory()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            $"pomodoro-store-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        return directory;
    }

    [Fact]
    public void Defaults_UseFourRoundsAndFiveFifteenBreakCadence()
    {
        var settings = new PomodoroWidgetSettings();

        Assert.Equal(4, settings.RoundCount);
        Assert.Equal(25, settings.FocusMinutes);
        Assert.Equal(5, settings.ShortBreakMinutes);
        Assert.Equal(15, settings.LongBreakMinutes);
        Assert.True(settings.CompletionSoundEnabled);
        Assert.True(settings.CompletionNotificationEnabled);

        // Normalize must not move a healthy snapshot.
        PomodoroWidgetSettings normalized = PomodoroSettingsStore.Normalize(settings)!;
        Assert.Equal(4, normalized.RoundCount);
        Assert.Equal(25, normalized.FocusMinutes);
        Assert.Equal(5, normalized.ShortBreakMinutes);
        Assert.Equal(15, normalized.LongBreakMinutes);
    }

    [Fact]
    public void Normalize_ClampsEveryNumericSettingToItsSupportedRange()
    {
        var settings = new PomodoroWidgetSettings
        {
            RoundCount = 99,
            FocusMinutes = 0,
            ShortBreakMinutes = -10,
            LongBreakMinutes = 999
        };

        PomodoroSettingsStore.Normalize(settings);

        Assert.Equal(PomodoroSettingsStore.MaxRoundCount, settings.RoundCount);
        Assert.Equal(PomodoroSettingsStore.MinFocusMinutes, settings.FocusMinutes);
        Assert.Equal(PomodoroSettingsStore.MinBreakMinutes, settings.ShortBreakMinutes);
        Assert.Equal(PomodoroSettingsStore.MaxBreakMinutes, settings.LongBreakMinutes);
    }

    [Fact]
    public void PerFieldNormalizers_MatchTheMachineBounds()
    {
        Assert.Equal(1, PomodoroSettingsStore.NormalizeRoundCount(0));
        Assert.Equal(12, PomodoroSettingsStore.NormalizeRoundCount(13));
        Assert.Equal(1, PomodoroSettingsStore.NormalizeFocusMinutes(0));
        Assert.Equal(120, PomodoroSettingsStore.NormalizeFocusMinutes(9999));
        Assert.Equal(1, PomodoroSettingsStore.NormalizeShortBreakMinutes(-3));
        Assert.Equal(60, PomodoroSettingsStore.NormalizeShortBreakMinutes(61));
        Assert.Equal(1, PomodoroSettingsStore.NormalizeLongBreakMinutes(0));
        Assert.Equal(60, PomodoroSettingsStore.NormalizeLongBreakMinutes(int.MaxValue));
    }

    [Fact]
    public void Normalize_PreservesExplicitlyDisabledCompletionAlerts()
    {
        var settings = new PomodoroWidgetSettings
        {
            CompletionSoundEnabled = false,
            CompletionNotificationEnabled = false
        };

        PomodoroWidgetSettings normalized = PomodoroSettingsStore.Normalize(settings)!;

        Assert.False(normalized.CompletionSoundEnabled);
        Assert.False(normalized.CompletionNotificationEnabled);
    }

    [Fact]
    public void Load_OnAMissingFile_ReturnsDefaults()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var store = new PomodoroSettingsStore(directory);

            PomodoroWidgetSettings settings = store.Load();

            Assert.Equal(4, settings.RoundCount);
            Assert.Equal(25, settings.FocusMinutes);
            Assert.True(settings.CompletionNotificationEnabled);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void Update_NormalizesPersistsAndRaisesChanged()
    {
        string directory = CreateStoreDirectory();
        try
        {
            var store = new PomodoroSettingsStore(directory);
            int changedCalls = 0;
            PomodoroSettingsStore.Changed += OnChanged;
            try
            {
                store.Update(settings =>
                {
                    settings.RoundCount = 99;
                    settings.FocusMinutes = 30;
                    settings.CompletionSoundEnabled = false;
                });

                // Changed is raised on the write chain once the snapshot is
                // persisted, so reloading instances see the new file; keep the
                // subscription alive across the chain drain.
                store.WaitForPendingPersist();
            }
            finally
            {
                PomodoroSettingsStore.Changed -= OnChanged;
            }

            Assert.Equal(1, changedCalls);

            // A second store instance reads the clamped, persisted snapshot.
            var reloaded = new PomodoroSettingsStore(directory);
            PomodoroWidgetSettings settings = reloaded.Load();
            Assert.Equal(12, settings.RoundCount);
            Assert.Equal(30, settings.FocusMinutes);
            Assert.False(settings.CompletionSoundEnabled);
            Assert.True(settings.CompletionNotificationEnabled);

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
            var writer = new PomodoroSettingsStore(directory);
            var reader = new PomodoroSettingsStore(directory);
            Assert.Equal(25, reader.Load().FocusMinutes); // warm the reader's cache

            writer.Update(settings => settings.FocusMinutes = 30);
            writer.WaitForPendingPersist();

            // Production keeps separate store instances (settings section,
            // widget view models, capture window) that only hear about writes
            // through the static Changed event; a per-instance cache that
            // ignores it would keep serving the warmed 25-minute snapshot.
            Assert.Equal(30, reader.Load().FocusMinutes);
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
            var store = new PomodoroSettingsStore(directory);
            // The first save creates the primary file with a plain move; the
            // backup only materializes when the second save rolls the prior
            // primary into settings.json.bak via File.Replace.
            store.Update(settings => settings.FocusMinutes = 40);
            store.WaitForPendingPersist();
            store.Update(settings => settings.ShortBreakMinutes = 7);
            store.WaitForPendingPersist();
            string backupPath = ResilientJsonStore.GetBackupPath(
                Path.Combine(directory, "settings.json"));
            Assert.True(File.Exists(backupPath));

            File.WriteAllText(Path.Combine(directory, "settings.json"), "{ not json");

            var fresh = new PomodoroSettingsStore(directory);
            PomodoroWidgetSettings settings = fresh.Load();

            // The backup holds the first snapshot: focus 40, untouched break.
            Assert.Equal(40, settings.FocusMinutes);
            Assert.Equal(5, settings.ShortBreakMinutes);
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
            var writer = new PomodoroSettingsStore(directory);
            var reader = new PomodoroSettingsStore(directory);
            Assert.Equal(25, reader.Load().FocusMinutes);

            writer.Update(settings => settings.FocusMinutes = 40);

            // A load racing the in-flight persist may legitimately observe
            // the OLD file, but it must never stamp those bytes as current:
            // once the chain drains, the next load has to see the new value.
            _ = reader.Load();
            writer.WaitForPendingPersist();

            Assert.Equal(40, reader.Load().FocusMinutes);
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
            var writer = new PomodoroSettingsStore(directory);
            writer.Update(settings => settings.FocusMinutes = 45);

            // The shutdown settings-flush step holds no writer instance, so
            // it can only drain the static chain; once that returns, the
            // write must already be durable for a fresh reader — the exit
            // path where the last settings write used to be lost.
            PomodoroSettingsStore.WaitForAllPendingPersists();

            var reader = new PomodoroSettingsStore(directory);
            Assert.Equal(45, reader.Load().FocusMinutes);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
