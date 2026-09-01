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

    private async void QuickCaptureRecordHoverText_Click(object sender, RoutedEventArgs e)
    {
        await ShowQuickCaptureRecordColorEditorAsync(isBackground: false, isHoverText: true);
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

    private async Task ShowQuickCaptureRecordColorEditorAsync(
        bool isBackground,
        bool isHoverText = false)
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
            ResolveQuickCaptureEffectiveBackgroundColor(config),
            isHoverText,
            ResolveQuickCaptureEffectiveHoverTextColor(config));
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

    private Windows.UI.Color ResolveQuickCaptureEffectiveHoverTextColor(WidgetConfig config)
    {
        return QuickCaptureClipboardColorSettings.GetHoverTextModeOverride(config) ==
                QuickCaptureClipboardColorSettings.ModeCustom &&
            QuickCaptureClipboardColorSettings.TryGetHoverTextColorOverride(config, out Windows.UI.Color hoverOverride)
            ? hoverOverride
            : QuickCaptureClipboardColorSettings.ResolveAutoHoverTextColor(
                ResolveQuickCaptureEffectiveBackgroundColor(config));
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

        if (QuickCaptureRecordHoverTextButton is not null)
        {
            QuickCaptureRecordHoverTextButton.Content =
                ResolveQuickCaptureRecordHoverTextSummary(config);
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

    private string ResolveQuickCaptureRecordHoverTextSummary(WidgetConfig config)
    {
        return QuickCaptureClipboardColorSettings.GetHoverTextModeOverride(config) ==
            QuickCaptureClipboardColorSettings.ModeCustom
            ? _localizationService.T("QuickCapture.ClipboardColor.HoverTextCustom")
            : _localizationService.T("QuickCapture.ClipboardColor.FollowTheme");
    }
}
