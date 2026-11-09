using DeskBox.Models;
using DeskBox.Services;
using DeskBox.Views.Dialogs;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

/// <summary>
/// Code-behind for the screenshot capture settings section. The
/// <see cref="ScreenshotSettingsStore"/> owns the state (AppSettings stays
/// frozen at schema v15); every control writes through
/// <see cref="ScreenshotSettingsStore.Update(Action{ScreenshotSettings})"/>,
/// and the store's <see cref="ScreenshotSettingsStore.Changed"/> event —
/// also consumed by the hotkey service — keeps this view fresh, including
/// for writes from other windows.
/// </summary>
public sealed partial class ScreenshotSettingsSection : UserControl
{
    private readonly ScreenshotSettingsStore _settingsStore = new();
    private bool _isLoading = true;

    public ScreenshotSettingsSection()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private LocalizationService Localization => App.Current.LocalizationService;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ScreenshotSettingsStore.Changed += OnSettingsStoreChanged;
        _isLoading = true;
        RefreshHotkeyControls();
        _isLoading = false;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ScreenshotSettingsStore.Changed -= OnSettingsStoreChanged;
    }

    private void OnSettingsStoreChanged()
    {
        // Persist completions run on the store's write chain, not the UI
        // thread; marshal back before touching controls.
        _ = DispatcherQueue.TryEnqueue(RefreshHotkeyControls);
    }

    private void RefreshHotkeyControls()
    {
        ScreenshotSettings settings = _settingsStore.Load();
        ScreenshotHotkeyService? service = App.Current.ScreenshotHotkeyService;
        bool enabled = settings.HotkeyEnabled;
        bool registered = enabled && service is { IsRegistered: true };

        // Content-level gating mirrors the search hotkey expander: the
        // expander stays readable while only the mutating controls go gray.
        ScreenshotHotkeyToggle.IsEnabled = service is not null;
        ScreenshotHotkeyCaptureButton.IsEnabled = enabled && service is not null;
        ResetScreenshotHotkeyButton.IsEnabled = enabled && service is not null;
        ScreenshotHotkeyToggle.IsOn = enabled;
        ScreenshotHotkeyCaptureButton.Content = GlobalHotkeyService.FormatGesture(
            GlobalHotkeyService.NormalizeGesture(settings.HotkeyModifiers, settings.HotkeyKey),
            Localization);

        ScreenshotHotkeyStatusText.Text = !enabled || service is null
            ? Localization.T("Settings.Screenshot.Hotkey.Status.Disabled")
            : Localization.T(registered
                ? "Settings.Screenshot.Hotkey.Status.Active"
                : "Settings.Screenshot.Hotkey.Status.Failed");
    }

    private void ScreenshotHotkeyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        _settingsStore.Update(settings => settings.HotkeyEnabled = ScreenshotHotkeyToggle.IsOn);
    }

    private async void ScreenshotHotkeyCaptureButton_Click(object sender, RoutedEventArgs e)
    {
        if (XamlRoot is null || App.Current.ScreenshotHotkeyService is not { } service)
        {
            return;
        }

        HotkeyRecorderDialog.Preset[] presets =
        [
            new(
                GlobalHotkeyService.FormatGesture(new GlobalHotkeyGesture(
                    (HotkeyModifierKeys)ScreenshotSettingsStore.DefaultHotkeyModifiers,
                    ScreenshotSettingsStore.DefaultHotkeyKey), Localization),
                GlobalHotkeyActivation.FromChord(new GlobalHotkeyGesture(
                    (HotkeyModifierKeys)ScreenshotSettingsStore.DefaultHotkeyModifiers,
                    ScreenshotSettingsStore.DefaultHotkeyKey)))
        ];

        var dialog = new HotkeyRecorderDialog(
            XamlRoot,
            Localization,
            HotkeyRecorderDialog.Scope.Screenshot,
            GlobalHotkeyActivation.FromChord(service.CurrentGesture),
            presets,
            ApplyHotkeyFromRecorderAsync,
            GetHotkeyRecorderConflict);
        await dialog.ShowAsync();
    }

    private Task<string?> ApplyHotkeyFromRecorderAsync(GlobalHotkeyActivation activation)
    {
        try
        {
            _settingsStore.Update(settings =>
            {
                settings.HotkeyModifiers = (int)activation.Gesture.Modifiers;
                settings.HotkeyKey = activation.Gesture.VirtualKey;
            });

            // The store change already queued a refresh on the UI thread;
            // re-register synchronously so the recorder gets immediate
            // feedback for a combination the system refuses.
            if (App.Current.ScreenshotHotkeyService is { } service)
            {
                service.RefreshRegistration();
                if (!service.IsRegistered)
                {
                    return Task.FromResult<string?>(
                        Localization.T("Settings.Screenshot.Hotkey.Status.Failed"));
                }
            }

            return Task.FromResult<string?>(null);
        }
        catch (Exception ex)
        {
            App.Log($"[ScreenshotSettings] Failed to apply hotkey: {ex}");
            return Task.FromResult<string?>(
                Localization.T("Settings.Screenshot.Hotkey.Status.Failed"));
        }
    }

    private string? GetHotkeyRecorderConflict(GlobalHotkeyGesture gesture)
    {
        return App.Current.GlobalHotkeyService?.CurrentActivation is
            { Kind: HotkeyActivationKind.Chord } activation &&
            activation.Gesture.Equals(gesture)
                ? Localization.T("Settings.Screenshot.Hotkey.Status.GlobalHotkeyConflict")
                : null;
    }

    private void ResetScreenshotHotkeyButton_Click(object sender, RoutedEventArgs e)
    {
        _settingsStore.Update(settings =>
        {
            settings.HotkeyModifiers = ScreenshotSettingsStore.DefaultHotkeyModifiers;
            settings.HotkeyKey = ScreenshotSettingsStore.DefaultHotkeyKey;
        });
    }
}
