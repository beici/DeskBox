using System.Text.Json;
using DeskBox.Models;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class FileWidgetIconLabelOverrideTests
{
    [Fact]
    public void ResolveEffectiveFileNameLineCount_FollowsGlobalWhenOverrideIsNull()
    {
        Assert.Equal(
            SettingsService.HiddenFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(
                SettingsService.HiddenFileNameLineCount, null));
        Assert.Equal(
            SettingsService.MinFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(
                SettingsService.MinFileNameLineCount, null));
        Assert.Equal(
            SettingsService.MaxFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(
                SettingsService.MaxFileNameLineCount, null));
    }

    [Fact]
    public void ResolveEffectiveFileNameLineCount_OverrideWinsOverGlobal()
    {
        Assert.Equal(
            SettingsService.HiddenFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(
                SettingsService.MaxFileNameLineCount, true));
        Assert.Equal(
            SettingsService.MinFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(
                SettingsService.HiddenFileNameLineCount, false));
        Assert.Equal(
            SettingsService.MaxFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(
                SettingsService.MaxFileNameLineCount, false));
    }

    [Fact]
    public void ResolveEffectiveFileNameLineCount_NormalizesOutOfRangeGlobalValues()
    {
        Assert.Equal(
            SettingsService.MaxFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(7, null));
        Assert.Equal(
            SettingsService.HiddenFileNameLineCount,
            FileWidgetIconLayout.ResolveEffectiveFileNameLineCount(7, true));
    }

    [Fact]
    public void Calculate_HiddenOverrideCollapsesLabelAndShrinksTileHeight()
    {
        var settings = new AppSettings
        {
            IconSize = 48,
            TextSize = 14,
            FileNameLineCount = 2,
            VerticalSpacingScale = 0,
        };
        var shown = FileWidgetIconLayout.Calculate(settings);
        var hidden = FileWidgetIconLayout.Calculate(
            settings, iconLabelHiddenOverride: true);
        Assert.True(shown.ShowLabel);
        Assert.False(hidden.ShowLabel);
        Assert.True(hidden.TileHeight < shown.TileHeight);
        Assert.True(hidden.CellHeight < shown.CellHeight);
        // The global setting itself must stay untouched by the override.
        Assert.Equal(2, settings.FileNameLineCount);
    }

    [Fact]
    public void Calculate_ForcedShowOverridePromotesGlobalHiddenToSingleLine()
    {
        var settings = new AppSettings
        {
            IconSize = 48,
            TextSize = 14,
            FileNameLineCount = SettingsService.HiddenFileNameLineCount,
            VerticalSpacingScale = 0,
        };
        var followsGlobal = FileWidgetIconLayout.Calculate(settings);
        var forcedShow = FileWidgetIconLayout.Calculate(
            settings, iconLabelHiddenOverride: false);
        Assert.False(followsGlobal.ShowLabel);
        Assert.True(forcedShow.ShowLabel);
        Assert.Equal(SettingsService.MinFileNameLineCount, forcedShow.LabelMaxLines);
        Assert.True(forcedShow.TileHeight > followsGlobal.TileHeight);
    }

    [Fact]
    public void Calculate_HiddenOverrideMatchesGlobalHiddenGeometry()
    {
        var settings = new AppSettings
        {
            IconSize = 48,
            TextSize = 14,
            FileNameLineCount = SettingsService.MaxFileNameLineCount,
        };
        var overrideHidden = FileWidgetIconLayout.Calculate(
            settings, iconLabelHiddenOverride: true);
        settings.FileNameLineCount = SettingsService.HiddenFileNameLineCount;
        var globalHidden = FileWidgetIconLayout.Calculate(settings);
        Assert.Equal(globalHidden.ShowLabel, overrideHidden.ShowLabel);
        Assert.Equal(globalHidden.TileHeight, overrideHidden.TileHeight);
        Assert.Equal(globalHidden.CellHeight, overrideHidden.CellHeight);
    }

    [Fact]
    public void WidgetConfig_SerializesIconLabelHiddenOverrideOnlyWhenSet()
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new WidgetKindJsonConverter() },
        };

        string untouched = JsonSerializer.Serialize(new WidgetConfig(), options);
        Assert.DoesNotContain("IconLabelHiddenOverride", untouched);

        var hidden = new WidgetConfig { IconLabelHiddenOverride = true };
        string hiddenJson = JsonSerializer.Serialize(hidden, options);
        Assert.Contains("\"IconLabelHiddenOverride\": true", hiddenJson);
        WidgetConfig restored = JsonSerializer.Deserialize<WidgetConfig>(hiddenJson, options)!;
        Assert.True(restored.IconLabelHiddenOverride);

        var forcedShow = new WidgetConfig { IconLabelHiddenOverride = false };
        string forcedShowJson = JsonSerializer.Serialize(forcedShow, options);
        Assert.Contains("\"IconLabelHiddenOverride\": false", forcedShowJson);
        WidgetConfig restoredForcedShow =
            JsonSerializer.Deserialize<WidgetConfig>(forcedShowJson, options)!;
        Assert.False(restoredForcedShow.IconLabelHiddenOverride);

        WidgetConfig restoredUntouched =
            JsonSerializer.Deserialize<WidgetConfig>(untouched, options)!;
        Assert.Null(restoredUntouched.IconLabelHiddenOverride);
    }
}
