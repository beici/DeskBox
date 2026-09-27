using System.Text.Json;

namespace DeskBox.Tests;

public sealed class StartupRegistrationContractTests
{
    private static readonly string[] StartupStatusKeys =
    [
        "Settings.AutoStart.WindowsDisabled",
        "Settings.AutoStart.TaskDisabled",
        "Settings.AutoStart.Pending",
        "Settings.AutoStart.Failed",
        "Settings.AutoStart.OpenSystemSettings",
        "Settings.AutoStart.Mode.Title",
        "Settings.AutoStart.Mode.Description",
        "Settings.AutoStart.Mode.Standard",
        "Settings.AutoStart.Mode.ScheduledTask",
        "Settings.AutoStart.Fallback",
        "Settings.AutoStart.ChangeFailed"
    ];

    [Fact]
    public void SettingsAndOnboardingExposeWindowsStartupAppsRecovery()
    {
        string root = FindRepositoryRoot();
        string settingsXaml = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/SettingsWindow.xaml"));
        string settingsStartup = File.ReadAllText(Path.Combine(
            root,
            "src/DeskBox/Views/SettingsWindow.Startup.cs"));
        // R14: OnboardingWindow.Hotkey.cs was retired with the dead legacy
        // five-step flow (DEF-128); the Windows-startup-apps recovery contract
        // is carried by the Settings surface below.

        Assert.Contains(
            "AutoStartSystemSettingsVisibility",
            settingsXaml,
            StringComparison.Ordinal);
        Assert.Contains(
            "ms-settings:startupapps",
            settingsStartup,
            StringComparison.Ordinal);
        Assert.Contains(
            "RefreshAutoStartState()",
            settingsStartup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRecoveryStringsExistInEveryLocale()
    {
        string stringsDirectory = Path.Combine(
            FindRepositoryRoot(),
            "src/DeskBox/Strings");

        foreach (string path in Directory.EnumerateFiles(stringsDirectory, "*.json"))
        {
            using JsonDocument document = JsonDocument.Parse(File.ReadAllText(path));
            foreach (string key in StartupStatusKeys)
            {
                Assert.True(
                    document.RootElement.TryGetProperty(key, out _),
                    $"{Path.GetFileName(path)} is missing {key}");
            }
        }
    }

    [Fact]
    public void PermissionRepairOnlyCountsVerifiedStartupEnablement()
    {
        string source = File.ReadAllText(Path.Combine(
            FindRepositoryRoot(),
            "src/DeskBox/Services/DragDropPermissionService.cs"));

        Assert.Contains(
            "startupResult.State == StartupRegistrationState.Enabled",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "startupResult.RequiresSystemSettings",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "StartupService.Enable();\n                repairedCount++;",
            source,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "DeskBox.slnx")) ||
                File.Exists(Path.Combine(directory.FullName, "DeskBox.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find the DeskBox repository root.");
    }
}
