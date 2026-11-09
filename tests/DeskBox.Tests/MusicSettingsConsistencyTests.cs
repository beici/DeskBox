using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class MusicSettingsConsistencyTests : IAsyncLifetime
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
    private readonly List<SettingsService> _settingsServices = [];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (SettingsService settings in _settingsServices)
        {
            await settings.FlushPendingSaveAsync();
        }

        CreateStore().WaitForPendingPersist();
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void PreloadedReader_SeesAnUpdateBeforeTheDiskWriteCanFinish()
    {
        MusicSettingsStore reader = CreateStore();
        MusicSettingsStore writer = CreateStore();
        Assert.True(reader.Load().UseArtworkBackdrop);

        // Deny file replacement until the assertion is complete. The public
        // synchronous Update contract must publish the value from memory,
        // even while the async disk operation cannot commit it yet.
        using (var blockedFile = new FileStream(
                   writer.StorePath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None))
        {
            writer.Update(settings => settings.UseArtworkBackdrop = false);
            Assert.False(reader.Load().UseArtworkBackdrop);
        }

        writer.WaitForPendingPersist();
    }

    [Fact]
    public void PreloadedWriters_PreserveEachOthersFieldChanges()
    {
        MusicSettingsStore first = CreateStore();
        MusicSettingsStore second = CreateStore();
        _ = first.Load();
        _ = second.Load();

        first.Update(settings => settings.UseArtworkBackdrop = false);
        second.Update(settings => settings.EnableCoverHoverMotion = false);
        first.WaitForPendingPersist();
        second.WaitForPendingPersist();

        MusicWidgetSettings persisted = ReadPersisted(first);
        Assert.False(persisted.UseArtworkBackdrop);
        Assert.False(persisted.EnableCoverHoverMotion);
        Assert.False(first.Load().EnableCoverHoverMotion);
        Assert.False(second.Load().UseArtworkBackdrop);
    }

    [Fact]
    public async Task SaveThenUpdate_FromDifferentInstances_PreservesTheSavedFields()
    {
        MusicSettingsStore first = CreateStore();
        MusicSettingsStore second = CreateStore();
        _ = second.Load();

        await first.SaveAsync(new MusicWidgetSettings
        {
            UseArtworkBackdrop = false,
            DisplayMode = SettingsService.MusicDisplayModeCover
        });
        second.Update(settings => settings.EnableCoverHoverMotion = false);
        second.WaitForPendingPersist();

        MusicWidgetSettings persisted = ReadPersisted(first);
        Assert.False(persisted.UseArtworkBackdrop);
        Assert.False(persisted.EnableCoverHoverMotion);
        Assert.Equal(SettingsService.MusicDisplayModeCover, persisted.DisplayMode);
    }

    [Fact]
    public void EquivalentPaths_ShareUpdates_WhileOtherDirectoriesStayIsolated()
    {
        MusicSettingsStore first = CreateStore();
        var alias = new MusicSettingsStore(Path.Combine(_root, "music", "."));
        MusicSettingsStore other = CreateStore("other");
        _ = alias.Load();
        _ = other.Load();

        first.Update(settings => settings.UseArtworkBackdrop = false);

        Assert.False(alias.Load().UseArtworkBackdrop);
        Assert.True(other.Load().UseArtworkBackdrop);
        first.WaitForPendingPersist();
    }

    [Fact]
    public void Load_ReturnsAnIndependentSnapshot()
    {
        MusicSettingsStore store = CreateStore();
        MusicWidgetSettings snapshot = store.Load();
        snapshot.UseArtworkBackdrop = false;

        Assert.True(store.Load().UseArtworkBackdrop);
        Assert.False(File.Exists(store.StorePath));
    }

    [Theory]
    [InlineData(SettingsService.MusicDisplayModeCover)]
    [InlineData(SettingsService.MusicDisplayModeControls)]
    [InlineData(SettingsService.MusicDisplayModeRecordVertical)]
    [InlineData(SettingsService.MusicDisplayModeRecordHorizontal)]
    public async Task DisplayModeEdit_UpdatesTheAuthoritativeFileAndNotifiesOnce(string mode)
    {
        SettingsService settings = CreateSettings();
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        MusicSettingsStore reader = CreateStore();
        _ = reader.Load();
        int notifications = 0;
        settings.SettingsChanged += () => notifications++;

        Assert.True(coordinator.SetMusicDisplayMode(mode));
        Assert.False(coordinator.SetMusicDisplayMode(mode));
        Assert.Equal(1, notifications);
        await settings.FlushPendingSaveAsync();
        reader.WaitForPendingPersist();

        Assert.Equal(mode, ReadPersisted(reader).DisplayMode);
        Assert.Equal(mode, reader.Load().DisplayMode);
        Assert.Equal(mode, settings.Settings.Music.MusicDisplayMode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Reset_RestoresTheAuthoritativeFileAndKeepsBatchNotificationSemantics(
        bool scheduleSave)
    {
        SettingsService settings = CreateSettings();
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        MusicSettingsStore reader = CreateStore();
        coordinator.SetMusicUseArtworkBackdrop(false);
        coordinator.SetMusicEnableCoverHoverMotion(false);
        coordinator.SetMusicDisplayMode(SettingsService.MusicDisplayModeControls);
        coordinator.SetMusicHideTimelineWhenUnavailable(false);
        await settings.FlushPendingSaveAsync();
        reader.WaitForPendingPersist();
        _ = reader.Load();
        int notifications = 0;
        settings.SettingsChanged += () => notifications++;

        coordinator.ResetMusicPresentationPreferences(scheduleSave);
        Assert.Equal(scheduleSave ? 1 : 0, notifications);
        await settings.FlushPendingSaveAsync();
        reader.WaitForPendingPersist();

        MusicWidgetSettings persisted = ReadPersisted(reader);
        Assert.True(persisted.UseArtworkBackdrop);
        Assert.True(persisted.EnableCoverHoverMotion);
        Assert.Equal(SettingsService.MusicDisplayModeAuto, persisted.DisplayMode);
        Assert.True(reader.Load().UseArtworkBackdrop);
        Assert.True(settings.Settings.Music.MusicHideTimelineWhenUnavailable);
    }

    [Fact]
    public async Task Widget_UsesTheInjectedProfileAndTracksEditsAndReset()
    {
        SettingsService settings = CreateSettings();
        MusicSettingsStore store = CreateStore();
        await store.SaveAsync(new MusicWidgetSettings
        {
            UseArtworkBackdrop = false,
            EnableCoverHoverMotion = false,
            DisplayMode = SettingsService.MusicDisplayModeRecordHorizontal
        });
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        var config = new WidgetConfig { WidgetKind = WidgetKind.Music };
        using var widget = new MusicWidgetViewModel(
            config, new MusicSessionService(), new LocalizationService(settings), settings);

        Assert.False(widget.UseArtworkBackdrop);
        Assert.False(widget.EnableCoverHoverMotion);
        Assert.Equal(SettingsService.MusicDisplayModeRecordHorizontal, widget.DisplayMode);

        coordinator.SetMusicDisplayMode(SettingsService.MusicDisplayModeCover);
        Assert.Equal(SettingsService.MusicDisplayModeCover, widget.DisplayMode);
        coordinator.SetMusicUseArtworkBackdrop(false);
        coordinator.SetMusicEnableCoverHoverMotion(false);
        coordinator.ResetMusicPresentationPreferences();

        Assert.True(widget.UseArtworkBackdrop);
        Assert.True(widget.EnableCoverHoverMotion);
        Assert.Equal(SettingsService.MusicDisplayModeAuto, widget.DisplayMode);
    }

    [Fact]
    public async Task Reset_RefreshesTheWidgetWhenTheLegacyMirrorAlreadyContainsDefaults()
    {
        SettingsService settings = CreateSettings();
        MusicSettingsStore store = CreateStore();
        await store.SaveAsync(new MusicWidgetSettings
        {
            UseArtworkBackdrop = false,
            EnableCoverHoverMotion = false,
            DisplayMode = SettingsService.MusicDisplayModeCover
        });
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        using var widget = new MusicWidgetViewModel(
            new WidgetConfig { WidgetKind = WidgetKind.Music },
            new MusicSessionService(), new LocalizationService(settings), settings);
        int notifications = 0;
        settings.SettingsChanged += () => notifications++;

        coordinator.ResetMusicPresentationPreferences();

        Assert.Equal(1, notifications);
        Assert.True(widget.UseArtworkBackdrop);
        Assert.True(widget.EnableCoverHoverMotion);
        Assert.Equal(SettingsService.MusicDisplayModeAuto, widget.DisplayMode);
        coordinator.ResetMusicPresentationPreferences();
        Assert.Equal(1, notifications);
    }

    [Theory]
    [InlineData("DisplayMode")]
    [InlineData("ArtworkBackdrop")]
    [InlineData("CoverHoverMotion")]
    public async Task ExplicitEdit_RepairsAnOutdatedAuthoritativeValueEvenWhenTheMirrorMatches(
        string option)
    {
        SettingsService settings = CreateSettings();
        MusicSettingsStore store = CreateStore();
        await store.SaveAsync(new MusicWidgetSettings
        {
            UseArtworkBackdrop = false,
            EnableCoverHoverMotion = false,
            DisplayMode = SettingsService.MusicDisplayModeCover
        });
        var coordinator = new FeatureWidgetsSettingsCoordinator(settings);
        int notifications = 0;
        settings.SettingsChanged += () => notifications++;

        bool Apply() => option switch
        {
            "DisplayMode" => coordinator.SetMusicDisplayMode(SettingsService.MusicDisplayModeAuto),
            "ArtworkBackdrop" => coordinator.SetMusicUseArtworkBackdrop(true),
            _ => coordinator.SetMusicEnableCoverHoverMotion(true)
        };

        Assert.True(Apply());
        Assert.False(Apply());
        Assert.Equal(1, notifications);
        store.WaitForPendingPersist();
        MusicWidgetSettings persisted = ReadPersisted(store);
        Assert.True(option switch
        {
            "DisplayMode" => persisted.DisplayMode == SettingsService.MusicDisplayModeAuto,
            "ArtworkBackdrop" => persisted.UseArtworkBackdrop,
            _ => persisted.EnableCoverHoverMotion
        });
    }

    private SettingsService CreateSettings()
    {
        var settings = new SettingsService(_root);
        _settingsServices.Add(settings);
        return settings;
    }

    private MusicSettingsStore CreateStore(string directory = "music") =>
        new(Path.Combine(_root, directory));

    private static MusicWidgetSettings ReadPersisted(MusicSettingsStore store) =>
        JsonSerializer.Deserialize(
            File.ReadAllText(store.StorePath), MusicSettingsJsonContext.Default.Settings)!;
}
