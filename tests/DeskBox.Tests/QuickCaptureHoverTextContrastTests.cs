using System.Text.RegularExpressions;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// DEF-030 regression coverage: the Quick Capture record list hover state
/// must keep the item text readable. The system ListViewItem template swaps
/// in a theme-driven hover foreground on PointerOver, which dissolved the
/// text into light (custom or light-theme) record cards under the dark panel
/// theme. The fix adds a dedicated hover foreground channel resolved from
/// the effective card background (auto white/black by contrast) with an
/// optional custom override, wired through the shared color editor, the
/// widget menu, and the Settings page.
/// </summary>
public class QuickCaptureHoverTextContrastTests
{
    [Theory]
    [InlineData(0xFF, 0xFF, 0xFF, 0xFF, 0x1A, 0x1A, 0x1A)] // white card -> dark hover text
    [InlineData(0xEC, 0xFA, 0xF5, 0xEA, 0x1A, 0x1A, 0x1A)] // light paper card -> dark
    [InlineData(0xB8, 0x2B, 0x3D, 0x53, 0xF5, 0xF5, 0xF5)] // dark mist card -> light
    [InlineData(0x18, 0xFF, 0xFF, 0xFF, 0x1A, 0x1A, 0x1A)] // translucent default card -> dark
    public void ResolveAutoHoverTextColor_PicksHigherContrastCandidate(
        byte bgA, byte bgR, byte bgG, byte bgB,
        byte expectedR, byte expectedG, byte expectedB)
    {
        Windows.UI.Color background = Windows.UI.Color.FromArgb(bgA, bgR, bgG, bgB);

        Windows.UI.Color hover = QuickCaptureClipboardColorSettings.ResolveAutoHoverTextColor(background);

        Assert.Equal(expectedR, hover.R);
        Assert.Equal(expectedG, hover.G);
        Assert.Equal(expectedB, hover.B);
    }

    [Fact]
    public void HoverTextMetadataKeys_AreDistinctFromTextAndBackgroundChannels()
    {
        Assert.NotEqual(
            QuickCaptureClipboardColorSettings.HoverTextModeOverrideMetadataKey,
            QuickCaptureClipboardColorSettings.TextModeOverrideMetadataKey);
        Assert.NotEqual(
            QuickCaptureClipboardColorSettings.HoverTextColorOverrideMetadataKey,
            QuickCaptureClipboardColorSettings.TextColorOverrideMetadataKey);
        Assert.NotEqual(
            QuickCaptureClipboardColorSettings.HoverTextColorOverrideMetadataKey,
            QuickCaptureClipboardColorSettings.BackgroundColorOverrideMetadataKey);
    }

    [Fact]
    public void ResetOverrides_RemovesHoverChannelOverrides()
    {
        var config = new WidgetConfig();
        QuickCaptureClipboardColorSettings.SetHoverTextColorOverride(
            config,
            Windows.UI.Color.FromArgb(0xFF, 0x11, 0x22, 0x33));
        QuickCaptureClipboardColorSettings.SetHoverTextModeOverride(
            config,
            QuickCaptureClipboardColorSettings.ModeCustom);
        Assert.True(QuickCaptureClipboardColorSettings.TryGetHoverTextColorOverride(config, out _));

        QuickCaptureClipboardColorSettings.ResetOverrides(config);

        Assert.False(QuickCaptureClipboardColorSettings.TryGetHoverTextColorOverride(config, out _));
        Assert.NotEqual(
            QuickCaptureClipboardColorSettings.ModeCustom,
            QuickCaptureClipboardColorSettings.GetHoverTextModeOverride(config));
    }

    [Fact]
    public void HoverBrush_IsWiredIntoTemplateAndAppliedOnHover()
    {
        string xaml = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml"));
        string code = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs"));

        // The dedicated hover brush exists and the display TextBlock owns an
        // explicit Foreground plus its own pointer handlers so the hover
        // color has a single writer and the ListViewItem template's hover
        // state can never repaint the text (the earlier card-level handler
        // raced the template state animation and flickered).
        Assert.Contains("QuickCaptureClipboardItemHoverForegroundBrush", xaml);
        Assert.Contains("x:Name=\"QuickCaptureItemDisplayText\"", xaml);
        Assert.Matches(
            new Regex(@"x:Name=""QuickCaptureItemDisplayText""[\s\S]{0,400}?Foreground=""\{StaticResource QuickCaptureClipboardItemForegroundBrush\}""[\s\S]{0,400}?PointerEntered=""QuickCaptureItemText_PointerEntered""[\s\S]{0,400}?PointerExited=""QuickCaptureItemText_PointerExited"""),
            xaml);

        // The text-level handlers swap between the two shared brushes; the
        // card-level race path (Refresh/ClearHoveredItemTextColor) is gone.
        Assert.Contains("QuickCaptureItemText_PointerEntered", code);
        Assert.Contains("QuickCaptureItemText_PointerExited", code);
        Assert.DoesNotContain("RefreshHoveredItemTextColor", code);
        Assert.DoesNotContain("ClearHoveredItemTextColor", code);

        // Follow-theme resolution derives from the effective card background
        // via the shared auto-contrast resolver (never a fixed white).
        Assert.Contains(
            "QuickCaptureClipboardColorSettings.ResolveAutoHoverTextColor(" + Environment.NewLine +
            "            ResolveClipboardItemEffectiveBackgroundColor());",
            code.Replace("\r\n", Environment.NewLine));
    }

    [Fact]
    public void ColorEditorAndEntries_ExposeHoverTextChannel()
    {
        string editor = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/QuickCaptureClipboardColorEditor.cs"));
        string commands = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/ContentWidgetWindow.Commands.cs"));
        string settingsCode = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/SettingsWindow.QuickCaptureColors.cs"));
        string settingsXaml = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/SettingsWindow.xaml"));

        Assert.Contains("isHoverText", editor);
        Assert.Contains("SetHoverTextColorOverride(config, picker.Color);", editor);
        Assert.Contains("SetHoverTextModeOverride(", editor);
        Assert.Contains("ClipboardColorPickerTarget.HoverText", commands);
        Assert.Contains("QuickCaptureRecordHoverTextButton", settingsXaml);
        Assert.Contains("ResolveQuickCaptureEffectiveHoverTextColor", settingsCode);
    }

    [Fact]
    public void HoverTextStrings_ArePresentInAllTwelveLocales()
    {
        string[] locales =
        [
            "en-US", "zh-CN", "zh-TW", "ja-JP", "de-DE", "fr-FR",
            "es-ES", "pt-BR", "ru-RU", "ar-SA", "hi-IN", "bn-BD"
        ];
        string[] keys =
        [
            "QuickCapture.ClipboardColor.HoverText",
            "QuickCapture.ClipboardColor.HoverTextCustom",
            "Settings.QuickCapture.RecordColors.HoverText.Title",
            "Settings.QuickCapture.RecordColors.HoverText.Description"
        ];
        foreach (string locale in locales)
        {
            string json = File.ReadAllText(TestPaths.FromRepository(
                $"src/DeskBox/Strings/{locale}.json"));
            foreach (string key in keys)
            {
                Assert.True(
                    json.Contains($"\"{key}\"", StringComparison.Ordinal),
                    $"{locale} is missing localization key {key}");
            }
        }
    }
}
