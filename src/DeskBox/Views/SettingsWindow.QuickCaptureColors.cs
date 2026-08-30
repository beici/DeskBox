using DeskBox.Controls.WidgetContents;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DeskBox.Views;

/// <summary>
/// Code-behind for the Quick Capture clipboard record color controls in the
/// "功能格子 - 随记" settings section. Resolves the Quick Capture widget
/// config, opens the shared color editor against the settings window's
/// XamlRoot, and pushes the committed colors to every loaded surface.
/// </summary>
public sealed partial class SettingsWindow
{
    private WidgetConfig? ResolveQuickCaptureWidgetConfig()
    {
        return _settingsService.Settings.Widgets.FirstOrDefault(widget =>
            widget.WidgetKind == WidgetKind.QuickCapture &&
            !widget.IsDisabled &&
            !_settingsService.Settings.DeletedWidgetIds.Contains(widget.Id));
    }

    private async void QuickCaptureRecordTextColor_Click(object sender, RoutedEventArgs e)
    {
        await ShowQuickCaptureRecordColorEditorAsync(isBackground: false);
    }

    private async void QuickCaptureRecordBackgroundColor_Click(object sender, RoutedEventArgs e)
    {
        await ShowQuickCaptureRecordColorEditorAsync(isBackground: true);
    }

    /// <summary>
    /// Loaded handler for the record-colors expander: fills both button
    /// captions from the effective colors so the entry always reflects the
    /// current state (follow-theme vs. custom).
    /// </summary>
    private void QuickCaptureRecordColors_Loaded(object sender, RoutedEventArgs e)
    {
        RefreshQuickCaptureRecordColorButtonText();
    }

    private async Task ShowQuickCaptureRecordColorEditorAsync(bool isBackground)
    {
        if (SettingsRoot.XamlRoot is null ||
            ResolveQuickCaptureWidgetConfig() is not { } config)
        {
            return;
        }

        await QuickCaptureClipboardColorEditor.ShowAsync(
            config,
            _settingsService,
            SettingsRoot.XamlRoot,
            _localizationService,
            isBackground,
            ResolveQuickCaptureEffectiveTextColor(config),
            ResolveQuickCaptureEffectiveBackgroundColor(config));
        App.Current.WidgetManager?.ApplyQuickCaptureClipboardColorsToLoadedWidgets();
        RefreshQuickCaptureRecordColorButtonText();
    }

    private void QuickCaptureRecordColorsReset_Click(object sender, RoutedEventArgs e)
    {
        if (ResolveQuickCaptureWidgetConfig() is not { } config)
        {
            return;
        }

        QuickCaptureClipboardColorEditor.Reset(config, _settingsService);
        App.Current.WidgetManager?.ApplyQuickCaptureClipboardColorsToLoadedWidgets();
        RefreshQuickCaptureRecordColorButtonText();
    }

    private Windows.UI.Color ResolveQuickCaptureEffectiveTextColor(WidgetConfig config)
    {
        return QuickCaptureClipboardColorSettings.GetTextModeOverride(config) ==
                QuickCaptureClipboardColorSettings.ModeCustom &&
            QuickCaptureClipboardColorSettings.TryGetTextColorOverride(config, out Windows.UI.Color textOverride)
            ? textOverride
            : Windows.UI.Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5);
    }

    private Windows.UI.Color ResolveQuickCaptureEffectiveBackgroundColor(WidgetConfig config)
    {
        return QuickCaptureClipboardColorSettings.GetBackgroundModeOverride(config) ==
                QuickCaptureClipboardColorSettings.ModeCustom &&
            QuickCaptureClipboardColorSettings.TryGetBackgroundColorOverride(config, out Windows.UI.Color backgroundOverride)
            ? backgroundOverride
            : Windows.UI.Color.FromArgb(0xFF, 0x28, 0x28, 0x28);
    }

    private void RefreshQuickCaptureRecordColorButtonText()
    {
        WidgetConfig config = ResolveQuickCaptureWidgetConfig() ?? new WidgetConfig();
        if (QuickCaptureRecordTextColorButton is not null)
        {
            QuickCaptureRecordTextColorButton.Content = ResolveQuickCaptureRecordColorSummary();
        }

        if (QuickCaptureRecordBackgroundColorButton is not null)
        {
            Windows.UI.Color background =
                ResolveQuickCaptureEffectiveBackgroundColor(config);
            QuickCaptureRecordBackgroundColorButton.Content =
                $"#{background.R:X2}{background.G:X2}{background.B:X2}";
        }
    }

    private string ResolveQuickCaptureRecordColorSummary()
    {
        if (ResolveQuickCaptureWidgetConfig() is not { } config)
        {
            return _localizationService.T("QuickCapture.ClipboardColor.FollowTheme");
        }

        return QuickCaptureClipboardColorSettings.GetTextModeOverride(config) ==
            QuickCaptureClipboardColorSettings.ModeCustom
            ? _localizationService.T("QuickCapture.ClipboardColor.TextCustom")
            : _localizationService.T("QuickCapture.ClipboardColor.FollowTheme");
    }
}
