using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Views.SettingsSections;

/// <summary>
/// 番茄钟的全局节奏设置。数值写入 data/pomodoro/settings.json 的
/// per-kind 存储,并由与计时状态机一致的钳制范围限制合法值
/// (bupocc 原版走 AppSettings + PomodoroSettingsPolicy,这里按
/// MusicSettingsStore 先例改为独立小存储,不触碰冻结的设置 schema)。
/// </summary>
public sealed partial class PomodoroSettingsSection : UserControl
{
    private readonly PomodoroSettingsStore _settingsStore = new();
    private bool _isLoading;

    public PomodoroSettingsSection()
    {
        InitializeComponent();
        ApplyNumberBoxRanges();
        Loaded += OnLoaded;
    }

    private LocalizationService Localization => App.Current.LocalizationService;

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        RefreshFromSettings();
    }

    private void ApplyNumberBoxRanges()
    {
        RoundCountNumberBox.Minimum = PomodoroSettingsStore.MinRoundCount;
        RoundCountNumberBox.Maximum = PomodoroSettingsStore.MaxRoundCount;
        FocusMinutesNumberBox.Minimum = PomodoroSettingsStore.MinFocusMinutes;
        FocusMinutesNumberBox.Maximum = PomodoroSettingsStore.MaxFocusMinutes;
        ShortBreakMinutesNumberBox.Minimum = PomodoroSettingsStore.MinBreakMinutes;
        ShortBreakMinutesNumberBox.Maximum = PomodoroSettingsStore.MaxBreakMinutes;
        LongBreakMinutesNumberBox.Minimum = PomodoroSettingsStore.MinBreakMinutes;
        LongBreakMinutesNumberBox.Maximum = PomodoroSettingsStore.MaxBreakMinutes;
    }

    /// <summary>
    /// 重新读取当前设置,并同步页面中的数值、摘要与无障碍文本。
    /// </summary>
    public void RefreshFromSettings()
    {
        PomodoroWidgetSettings settings = _settingsStore.Load();
        _isLoading = true;
        try
        {
            RoundCountNumberBox.Value =
                PomodoroSettingsStore.NormalizeRoundCount(settings.RoundCount);
            FocusMinutesNumberBox.Value =
                PomodoroSettingsStore.NormalizeFocusMinutes(settings.FocusMinutes);
            ShortBreakMinutesNumberBox.Value =
                PomodoroSettingsStore.NormalizeShortBreakMinutes(
                    settings.ShortBreakMinutes);
            LongBreakMinutesNumberBox.Value =
                PomodoroSettingsStore.NormalizeLongBreakMinutes(
                    settings.LongBreakMinutes);
            CompletionSoundToggle.IsOn =
                settings.CompletionSoundEnabled;
            CompletionNotificationToggle.IsOn =
                settings.CompletionNotificationEnabled;
        }
        finally
        {
            _isLoading = false;
        }

        RefreshLocalizedContent();
    }

    private void RefreshLocalizedContent()
    {
        AutomationProperties.SetName(
            RoundCountNumberBox,
            Localization.T("Settings.Pomodoro.RoundCount.Title"));
        AutomationProperties.SetHelpText(
            RoundCountNumberBox,
            Localization.T("Settings.Pomodoro.RoundCount.Description"));
        AutomationProperties.SetName(
            FocusMinutesNumberBox,
            Localization.T("Settings.Pomodoro.FocusMinutes.Title"));
        AutomationProperties.SetHelpText(
            FocusMinutesNumberBox,
            Localization.T("Settings.Pomodoro.FocusMinutes.Description"));
        AutomationProperties.SetName(
            ShortBreakMinutesNumberBox,
            Localization.T("Settings.Pomodoro.ShortBreakMinutes.Title"));
        AutomationProperties.SetHelpText(
            ShortBreakMinutesNumberBox,
            Localization.T("Settings.Pomodoro.ShortBreakMinutes.Description"));
        AutomationProperties.SetName(
            LongBreakMinutesNumberBox,
            Localization.T("Settings.Pomodoro.LongBreakMinutes.Title"));
        AutomationProperties.SetHelpText(
            LongBreakMinutesNumberBox,
            Localization.T("Settings.Pomodoro.LongBreakMinutes.Description"));
        AutomationProperties.SetName(
            CompletionSoundToggle,
            Localization.T("Settings.Pomodoro.CompletionSound.Title"));
        AutomationProperties.SetHelpText(
            CompletionSoundToggle,
            Localization.T("Settings.Pomodoro.CompletionSound.Description"));
        AutomationProperties.SetName(
            CompletionNotificationToggle,
            Localization.T("Settings.Pomodoro.CompletionNotification.Title"));
        AutomationProperties.SetHelpText(
            CompletionNotificationToggle,
            Localization.T("Settings.Pomodoro.CompletionNotification.Description"));

        PomodoroWidgetSettings settings = _settingsStore.Load();
        ScheduleSummaryText.Text = Localization.Format(
            "Settings.Pomodoro.Summary",
            PomodoroSettingsStore.NormalizeFocusMinutes(settings.FocusMinutes),
            PomodoroSettingsStore.NormalizeShortBreakMinutes(
                settings.ShortBreakMinutes),
            PomodoroSettingsStore.NormalizeLongBreakMinutes(
                settings.LongBreakMinutes),
            PomodoroSettingsStore.NormalizeRoundCount(settings.RoundCount));
    }

    private void RoundCountNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        PomodoroWidgetSettings settings = _settingsStore.Load();
        CommitValue(
            sender,
            args.NewValue,
            settings.RoundCount,
            PomodoroSettingsStore.NormalizeRoundCount,
            value => _settingsStore.Update(s => s.RoundCount = value));
    }

    private void FocusMinutesNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        PomodoroWidgetSettings settings = _settingsStore.Load();
        CommitValue(
            sender,
            args.NewValue,
            settings.FocusMinutes,
            PomodoroSettingsStore.NormalizeFocusMinutes,
            value => _settingsStore.Update(s => s.FocusMinutes = value));
    }

    private void ShortBreakMinutesNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        PomodoroWidgetSettings settings = _settingsStore.Load();
        CommitValue(
            sender,
            args.NewValue,
            settings.ShortBreakMinutes,
            PomodoroSettingsStore.NormalizeShortBreakMinutes,
            value => _settingsStore.Update(s => s.ShortBreakMinutes = value));
    }

    private void LongBreakMinutesNumberBox_ValueChanged(
        NumberBox sender,
        NumberBoxValueChangedEventArgs args)
    {
        PomodoroWidgetSettings settings = _settingsStore.Load();
        CommitValue(
            sender,
            args.NewValue,
            settings.LongBreakMinutes,
            PomodoroSettingsStore.NormalizeLongBreakMinutes,
            value => _settingsStore.Update(s => s.LongBreakMinutes = value));
    }

    private void CompletionSoundToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        PomodoroWidgetSettings settings = _settingsStore.Load();
        if (settings.CompletionSoundEnabled == CompletionSoundToggle.IsOn)
        {
            return;
        }

        _settingsStore.Update(s => s.CompletionSoundEnabled = CompletionSoundToggle.IsOn);
    }

    private void CompletionNotificationToggle_Toggled(
        object sender,
        RoutedEventArgs e)
    {
        if (_isLoading)
        {
            return;
        }

        PomodoroWidgetSettings settings = _settingsStore.Load();
        if (settings.CompletionNotificationEnabled ==
            CompletionNotificationToggle.IsOn)
        {
            return;
        }

        _settingsStore.Update(
            s => s.CompletionNotificationEnabled = CompletionNotificationToggle.IsOn);
    }

    private void CommitValue(
        NumberBox numberBox,
        double proposedValue,
        int currentValue,
        Func<int, int> normalize,
        Action<int> assign)
    {
        if (_isLoading)
        {
            return;
        }

        int normalizedCurrent = normalize(currentValue);
        int normalizedValue = double.IsNaN(proposedValue) || double.IsInfinity(proposedValue)
            ? normalizedCurrent
            : normalize((int)Math.Round(
                Math.Clamp(proposedValue, int.MinValue, int.MaxValue),
                MidpointRounding.AwayFromZero));

        if (double.IsNaN(numberBox.Value) || numberBox.Value != normalizedValue)
        {
            _isLoading = true;
            try
            {
                numberBox.Value = normalizedValue;
            }
            finally
            {
                _isLoading = false;
            }
        }

        if (currentValue != normalizedValue)
        {
            assign(normalizedValue);
        }

        RefreshLocalizedContent();
    }
}
