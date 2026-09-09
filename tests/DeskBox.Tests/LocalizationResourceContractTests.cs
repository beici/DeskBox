using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace DeskBox.Tests;

public sealed partial class LocalizationResourceContractTests
{
    private static readonly string[] SupportedLocales =
    [
        "en-US",
        "zh-CN",
        "zh-TW",
        "ja-JP",
        "de-DE",
        "pt-BR",
        "hi-IN",
        "es-ES",
        "fr-FR",
        "ar-SA",
        "bn-BD",
        "ru-RU"
    ];

    [Fact]
    public void JsonLocales_HaveIdenticalKeysValuesAndPlaceholders()
    {
        string root = FindRepositoryRoot();
        string stringsDirectory = Path.Combine(root, "src", "DeskBox", "Strings");
        string[] actualLocales = Directory.EnumerateFiles(stringsDirectory, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(locale => locale, StringComparer.Ordinal)
            .ToArray()!;

        Assert.Equal(
            SupportedLocales.OrderBy(locale => locale, StringComparer.Ordinal),
            actualLocales);

        IReadOnlyDictionary<string, string> english = ReadJsonLocale(
            Path.Combine(stringsDirectory, "en-US.json"));

        foreach (string locale in SupportedLocales)
        {
            IReadOnlyDictionary<string, string> localized = ReadJsonLocale(
                Path.Combine(stringsDirectory, locale + ".json"));

            Assert.Equal(
                english.Keys.OrderBy(key => key, StringComparer.Ordinal),
                localized.Keys.OrderBy(key => key, StringComparer.Ordinal));

            foreach ((string key, string englishValue) in english)
            {
                string localizedValue = localized[key];
                Assert.False(
                    string.IsNullOrWhiteSpace(localizedValue),
                    $"{locale}:{key} has an empty translation.");
                Assert.Equal(
                    GetPlaceholderIndexes(englishValue),
                    GetPlaceholderIndexes(localizedValue));
            }
        }
    }

    [Fact]
    public void JsonLocales_DateTimeFormatSpecifiers_OnlyUseValidNetFormatLetters()
    {
        // DEF-039 regression guard: es-ES/fr-FR/ru-RU shipped translated
        // format letters ("aaaa", "гггг/М/д ЧЧ:мм") that .NET renders as
        // literal text, so file timestamps showed placeholder garbage.
        // The key-parity test above only compares placeholder indexes and
        // cannot catch this; validate the format specifier letters instead.
        string root = FindRepositoryRoot();
        string stringsDirectory = Path.Combine(root, "src", "DeskBox", "Strings");
        var violations = new List<string>();

        foreach (string locale in SupportedLocales)
        {
            IReadOnlyDictionary<string, string> localized = ReadJsonLocale(
                Path.Combine(stringsDirectory, locale + ".json"));

            foreach ((string key, string value) in localized)
            {
                foreach (Match match in FormatSpecifierRegex().Matches(value))
                {
                    // Quoted sections ('...' / "...") are literal text by
                    // definition (e.g. yyyy'年'M'月'd'日') and always valid.
                    string spec = QuotedLiteralRegex()
                        .Replace(match.Groups["spec"].Value, string.Empty);

                    foreach (char character in spec.Where(char.IsLetter))
                    {
                        if (!ValidNetFormatLetters.Contains(character))
                        {
                            violations.Add(
                                $"{locale}:{key} uses invalid .NET format letter " +
                                $"'{character}' in \"{match.Value}\".");
                        }
                    }
                }
            }
        }

        Assert.True(
            violations.Count == 0,
            "Invalid date/time format letters found:\n" +
            string.Join(Environment.NewLine, violations));
    }

    [Fact]
    public void PackagedResources_DeclareEverySupportedLocale()
    {
        string root = FindRepositoryRoot();
        string projectDirectory = Path.Combine(root, "src", "DeskBox");
        var manifest = XDocument.Load(Path.Combine(projectDirectory, "Package.appxmanifest"));
        string[] manifestLocales = manifest.Descendants()
            .Where(element => element.Name.LocalName == "Resource")
            .Select(element => (string?)element.Attribute("Language"))
            .Where(language => !string.IsNullOrWhiteSpace(language))
            .Cast<string>()
            .ToArray();

        Assert.Equal(SupportedLocales, manifestLocales);

        foreach (string locale in SupportedLocales)
        {
            string resourcePath = Path.Combine(
                projectDirectory,
                "Resources",
                locale,
                "Resources.resw");
            Assert.True(File.Exists(resourcePath), $"Missing packaged resource: {locale}");

            var resource = XDocument.Load(resourcePath);
            Dictionary<string, string> values = resource.Descendants("data")
                .ToDictionary(
                    element => (string)element.Attribute("name")!,
                    element => element.Element("value")?.Value ?? string.Empty,
                    StringComparer.Ordinal);

            Assert.Equal(
                new[] { "AppDescription", "AppDisplayName" },
                values.Keys.OrderBy(key => key, StringComparer.Ordinal));
            Assert.All(values.Values, value => Assert.False(string.IsNullOrWhiteSpace(value)));
        }
    }

    [Fact]
    public void UserFacingFailureSurfaces_DoNotExposeRawExceptionMessages()
    {
        string root = FindRepositoryRoot();
        string[] relativePaths =
        [
            "src/DeskBox/Controls/DesktopOrganizationTaskView.Actions.cs",
            "src/DeskBox/Controls/DesktopOrganizationTaskView.xaml.cs",
            "src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs",
            "src/DeskBox/Views/SettingsSections/DesktopOrganizationSettingsSection.xaml.cs",
            "src/DeskBox/Views/SettingsWindow.StorageAndUpdates.cs"
        ];

        foreach (string relativePath in relativePaths)
        {
            string content = File.ReadAllText(Path.Combine(
                root,
                relativePath.Replace('/', Path.DirectorySeparatorChar)));

            Assert.DoesNotContain("ResultInfo.Message = ex.Message", content, StringComparison.Ordinal);
            Assert.DoesNotContain("RuleStatusInfo.Message = ex.Message", content, StringComparison.Ordinal);
            Assert.DoesNotContain("RaiseFeedback(ex.Message", content, StringComparison.Ordinal);
            Assert.DoesNotContain("ShowStatusToast(ex.Message", content, StringComparison.Ordinal);
        }
    }

    private static IReadOnlyDictionary<string, string> ReadJsonLocale(string path)
    {
        return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path))
            ?? throw new InvalidDataException($"Localization file is empty: {path}");
    }

    private static string[] GetPlaceholderIndexes(string value)
    {
        return CompositePlaceholderRegex().Matches(value)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(index => index, StringComparer.Ordinal)
            .ToArray();
    }

    private static string FindRepositoryRoot()
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "src", "DeskBox", "DeskBox.csproj")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("DeskBox repository root was not found.");
    }

    [GeneratedRegex(@"\{(\d+)(?:[^{}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex CompositePlaceholderRegex();

    [GeneratedRegex(@"\{\d+:(?<spec>[^}]*)\}", RegexOptions.CultureInvariant)]
    private static partial Regex FormatSpecifierRegex();

    [GeneratedRegex(@"'[^']*'|""[^""]*""", RegexOptions.CultureInvariant)]
    private static partial Regex QuotedLiteralRegex();

    // Custom .NET date/time format letters that are valid when the argument
    // is a DateTime. Deliberately excludes "z"/"Z" (DateTimeOffset-only, a
    // DateTime throws FormatException) and everything outside the official
    // custom-specifier alphabet, so translated letters like Cyrillic "гггг"
    // or Spanish "aaaa" are rejected at test time instead of rendering as
    // literal garbage in the UI.
    private static readonly HashSet<char> ValidNetFormatLetters =
        new("yYmMdDhHsStTfFgK");
}
