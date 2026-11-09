using DeskBox.Services;

namespace DeskBox.Tests;

internal static class TestServices
{
    /// <summary>
    /// A SettingsService rooted in a fresh unique directory under the test
    /// temp root. The parameterless production ctor resolves the real
    /// per-user data directory (%LOCALAPPDATA%\DeskBox\data); test services
    /// must never read or write that directory, so every test construction
    /// funnels through this factory instead.
    /// </summary>
    internal static SettingsService CreateSettingsService()
    {
        string dataDir = Path.Combine(
            Path.GetTempPath(),
            "DeskBoxTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dataDir);
        return new SettingsService(dataDir);
    }

    public static LocalizationService CreateLocalizationService(string language = SettingsService.LanguageEnglish)
    {
        var settingsService = CreateSettingsService();
        settingsService.Settings.Language = language;
        return new LocalizationService(settingsService);
    }

    public static WidgetContentFactory CreateWidgetContentFactory()
    {
        return new WidgetContentFactory(CreateLocalizationService());
    }
}
