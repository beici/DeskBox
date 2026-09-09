using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI;

namespace DeskBox.Views;

/// <summary>
/// Shared color-picker flow for the Quick Capture clipboard record list.
/// Used by both the widget's own menu and the Settings window entry so the
/// contrast validation, persistence, and default-reset behavior cannot drift
/// between the two entrances.
/// </summary>
internal static class QuickCaptureClipboardColorEditor
{
    public static async Task ShowAsync(
        WidgetConfig config,
        SettingsService settingsService,
        XamlRoot xamlRoot,
        LocalizationService localization,
        bool isBackground,
        Windows.UI.Color effectiveTextColor,
        Windows.UI.Color effectiveBackgroundColor) =>
        await ShowAsync(
            config,
            settingsService,
            xamlRoot,
            localization,
            isBackground,
            effectiveTextColor,
            effectiveBackgroundColor,
            isHoverText: false,
            effectiveHoverTextColor: effectiveTextColor);

    public static async Task ShowAsync(
        WidgetConfig config,
        SettingsService settingsService,
        XamlRoot xamlRoot,
        LocalizationService localization,
        bool isBackground,
        Windows.UI.Color effectiveTextColor,
        Windows.UI.Color effectiveBackgroundColor,
        bool isHoverText,
        Windows.UI.Color effectiveHoverTextColor)
    {
        string titleKey = isHoverText
            ? "QuickCapture.ClipboardColor.HoverText"
            : isBackground
                ? "QuickCapture.ClipboardColor.Background"
                : "QuickCapture.ClipboardColor.Text";
        Windows.UI.Color initialColor = isHoverText
            ? effectiveHoverTextColor
            : isBackground
                ? effectiveBackgroundColor
                : effectiveTextColor;
        var picker = new ColorPicker
        {
            Color = initialColor,
            IsAlphaEnabled = false,
            MinWidth = 340
        };
        var dialog = new ContentDialog
        {
            XamlRoot = xamlRoot,
            Title = localization.T(titleKey),
            Content = picker,
            PrimaryButtonText = localization.T("Common.Save"),
            CloseButtonText = localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        try
        {
            if (await dialog.ShowAsync() != ContentDialogResult.Primary)
            {
                return;
            }

            (Windows.UI.Color pendingText, Windows.UI.Color pendingBackground) = isBackground
                ? (effectiveTextColor, picker.Color)
                : (picker.Color, effectiveBackgroundColor);
            if (!QuickCaptureClipboardColorSettings.IsPairReadable(
                    pendingText,
                    pendingBackground,
                    out double contrastRatio))
            {
                var rejection = new ContentDialog
                {
                    XamlRoot = xamlRoot,
                    Title = localization.T("QuickCapture.ClipboardColor.RejectedTitle"),
                    Content = string.Format(
                        localization.T("QuickCapture.ClipboardColor.RejectedBody"),
                        contrastRatio.ToString("F2", System.Globalization.CultureInfo.InvariantCulture),
                        QuickCaptureClipboardColorSettings.MinimumContrastRatio
                            .ToString("F1", System.Globalization.CultureInfo.InvariantCulture)),
                    CloseButtonText = localization.T("Common.Close")
                };
                await rejection.ShowAsync();
                return;
            }

            if (isHoverText)
            {
                QuickCaptureClipboardColorSettings.SetHoverTextColorOverride(config, picker.Color);
                QuickCaptureClipboardColorSettings.SetHoverTextModeOverride(
                    config,
                    QuickCaptureClipboardColorSettings.ModeCustom);
            }
            else if (isBackground)
            {
                QuickCaptureClipboardColorSettings.SetBackgroundColorOverride(config, picker.Color);
                QuickCaptureClipboardColorSettings.SetBackgroundModeOverride(
                    config,
                    QuickCaptureClipboardColorSettings.ModeCustom);
            }
            else
            {
                QuickCaptureClipboardColorSettings.SetTextColorOverride(config, picker.Color);
                QuickCaptureClipboardColorSettings.SetTextModeOverride(
                    config,
                    QuickCaptureClipboardColorSettings.ModeCustom);
            }

            settingsService.UpdateWidget(config);
        }
        catch (Exception ex)
        {
            App.Log($"[QuickCaptureClipboardColor] Color picker failed: {ex.Message}");
        }
    }

    public static void Reset(WidgetConfig config, SettingsService settingsService)
    {
        QuickCaptureClipboardColorSettings.ResetOverrides(config);
        settingsService.UpdateWidget(config);
    }
}
