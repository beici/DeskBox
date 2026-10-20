namespace DeskBox.Tests;

/// <summary>
/// Every ToggleSwitch in the settings domain and onboarding must carry an
/// accessible name. The toolkit SettingsCard template does not propagate its
/// Header to content controls (and marks the header presenter
/// AccessibilityView=Raw), and ToggleSwitch with empty OnContent/OffContent
/// announces only "toggle switch, on/off" to Narrator. The project convention
/// is the svc:Localized.AutomationNameKey attached property reusing the host
/// card's HeaderKey, so switches read like "Automatic organize, toggle
/// switch, off". Onboarding feature toggles reuse the feature title keys.
/// </summary>
public sealed class ToggleSwitchAccessibleNameContractTests
{
    public static TheoryData<string, int> ToggleSwitchHostFiles => new()
    {
        { "src/DeskBox/Views/SettingsWindow.xaml", 48 },
        { "src/DeskBox/Views/SettingsSections/SearchSettingsSection.xaml", 4 },
        { "src/DeskBox/Views/SettingsSections/GlanceWidgetSettingsSection.xaml", 4 },
        { "src/DeskBox/Views/SettingsSections/FileWidgetSettingsSection.xaml", 1 },
        { "src/DeskBox/Views/SettingsSections/DesktopOrganizationSettingsSection.xaml", 2 },
        { "src/DeskBox/Views/SettingsSections/CapsuleModeSettingsSection.xaml", 1 },
        // The fork keeps its own five-step onboarding flow instead of upstream's
        // (docs/architecture/upstream-merge-1.5.8.md: onboarding is local), and
        // that flow hosts eight switches: the two Step-3 toggles
        // (Quick Access / desktop shortcut) plus the six Step-5 feature
        // toggles. Upstream's flow had six, all named from the feature title
        // keys; the local ones must carry the same AutomationNameKey contract.
        { "src/DeskBox/Views/OnboardingWindow.xaml", 8 },
        // The fork extracted the music section into its own UserControl; the
        // merged SettingsWindow also renders that section inline, so this file
        // is a retained duplicate. It still hosts two switches and they carry
        // the same AutomationNameKey contract as the inline copies.
        { "src/DeskBox/Views/SettingsSections/MusicSettingsSection.xaml", 2 },
    };

    [Theory]
    [MemberData(nameof(ToggleSwitchHostFiles))]
    public void EveryToggleSwitch_HasAccessibleNameKey(string path, int expectedCount)
    {
        string xaml = ReadRepositoryFile(path);

        int total = CountOccurrences(xaml, "<ToggleSwitch");
        int named = CountOccurrences(xaml, "<ToggleSwitch svc:Localized.AutomationNameKey=\"");

        Assert.Equal(expectedCount, total);
        Assert.Equal(total, named);
    }

    [Fact]
    public void NoToggleSwitchLivesOutsideTheWhitelistedFiles()
    {
        // The whitelist above is only useful while it covers every host: a
        // ToggleSwitch added to a new XAML file must extend it (and carry an
        // AutomationNameKey), otherwise the per-file assertions never see it.
        string srcRoot = TestPaths.FromRepository("src/DeskBox");
        var offenders = new List<string>();
        foreach (string file in Directory.EnumerateFiles(
                     srcRoot, "*.xaml", SearchOption.AllDirectories))
        {
            if (file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}"))
            {
                continue;
            }

            string xaml = File.ReadAllText(file);
            if (xaml.Contains("<ToggleSwitch", StringComparison.Ordinal) &&
                !ToggleSwitchHostFiles.Any(t => Path.GetFullPath(TestPaths.FromRepository((string)t[0])) == Path.GetFullPath(file)))
            {
                offenders.Add(Path.GetRelativePath(srcRoot, file));
            }
        }

        Assert.True(offenders.Count == 0,
            "New ToggleSwitch host files must be added to ToggleSwitchHostFiles with naming: " +
            string.Join(", ", offenders));
    }

    private static int CountOccurrences(string content, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = content.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }
}
