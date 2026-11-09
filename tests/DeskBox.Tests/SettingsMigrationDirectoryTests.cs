using System.Text.Json;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class SettingsMigrationDirectoryTests
{
    [Fact]
    public async Task LoadAsync_MigratesMusicIntoTheServicesOwnDataDirectory()
    {
        string root = Path.Combine(
            Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "settings.json"), """
                {
                  "schemaVersion": 9,
                  "musicUseArtworkBackdrop": false,
                  "musicEnableCoverHoverMotion": false,
                  "musicDisplayMode": "RecordVertical"
                }
                """);

            var settings = new SettingsService(root);
            await settings.LoadAsync();

            string musicPath = Path.Combine(root, "music", "settings.json");
            Assert.True(File.Exists(musicPath), "Migration must use the service's profile directory.");
            using JsonDocument document = JsonDocument.Parse(await File.ReadAllTextAsync(musicPath));
            Assert.False(document.RootElement.GetProperty("useArtworkBackdrop").GetBoolean());
            Assert.False(document.RootElement.GetProperty("enableCoverHoverMotion").GetBoolean());
            Assert.Equal("RecordVertical", document.RootElement.GetProperty("displayMode").GetString());
            Assert.Equal(SettingsMigrationPipeline.CurrentSchemaVersion, settings.Settings.SchemaVersion);
        }
        finally
        {
            new MusicSettingsStore(Path.Combine(root, "music")).WaitForPendingPersist();
            Directory.Delete(root, recursive: true);
        }
    }
}
