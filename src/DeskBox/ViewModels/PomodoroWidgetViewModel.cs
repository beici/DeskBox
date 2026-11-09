using CommunityToolkit.Mvvm.ComponentModel;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Dispatching;

namespace DeskBox.ViewModels;

/// <summary>
/// 将番茄钟状态机投影为格子界面所需的文本与进度。
/// 倒计时刷新只更新内存；仅状态转换或恢复修复会触发配置持久化。
/// 计时设置来自独立的 PomodoroSettingsStore（AppSettings 冻结线之外），
/// 存储变更事件驱动运行中计时器即时应用新时长。
/// </summary>
public sealed class PomodoroWidgetViewModel : ObservableObject, IDisposable
{
    private const string PlayGlyph = "\uE102";
    private const string PauseGlyph = "\uE769";
    private const string CompletionAlertMetadataKey =
        "Pomodoro.CompletionAlertActive";

    private readonly LocalizationService _localizationService;
    private readonly SettingsService? _settingsService;
    private readonly PomodoroSettingsStore _settingsStore;
    private readonly DispatcherQueue? _dispatcherQueue;
    private readonly DispatcherQueueTimer? _refreshTimer;
    private readonly Func<DateTimeOffset> _utcNowProvider;
    private readonly PomodoroTimerStateMachine _stateMachine;
    private PomodoroTimerSnapshot _snapshot;
    private PresentationSnapshot _publishedPresentation;
    private bool _isInitialized;
    private bool _isCompletionAlertActive;
    private bool _isDisposed;

    public PomodoroWidgetViewModel(
        WidgetConfig config,
        LocalizationService localizationService,
        SettingsService? settingsService = null,
        PomodoroSettingsStore? settingsStore = null,
        DispatcherQueue? dispatcherQueue = null,
        Func<DateTimeOffset>? utcNowProvider = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(localizationService);
        if (config.WidgetKind != WidgetKind.Pomodoro)
        {
            throw new ArgumentException(
                "Pomodoro content requires a Pomodoro widget config.",
                nameof(config));
        }

        Config = config;
        _localizationService = localizationService;
        _settingsService = settingsService;
        _settingsStore = settingsStore ?? new PomodoroSettingsStore();
        // 测试线程或尚未初始化 WinUI 的后台线程可能没有可用的
        // DispatcherQueue；这种情况下仍允许使用同步状态机能力，
        // 只是不创建自动刷新计时器。
        _dispatcherQueue = dispatcherQueue ?? TryGetCurrentDispatcherQueue();
        _utcNowProvider = utcNowProvider ?? (() => DateTimeOffset.UtcNow);

        DateTimeOffset now = GetUtcNow();
        PomodoroWidgetSettings timerSettings = _settingsStore.Load();
        _stateMachine = new PomodoroTimerStateMachine(
            config,
            now,
            timerSettings.FocusMinutes,
            timerSettings.ShortBreakMinutes,
            timerSettings.LongBreakMinutes,
            timerSettings.RoundCount);
        _snapshot = _stateMachine.GetSnapshot(now);
        bool completionAlertMetadataChanged = RestoreCompletionAlert();
        if (IsNaturalCompletion(_stateMachine.RestoreTransition))
        {
            completionAlertMetadataChanged |= SetCompletionAlertActive(true);
        }
        _publishedPresentation = CapturePresentation();

        if (_dispatcherQueue is { } queue)
        {
            _refreshTimer = queue.CreateTimer();
            _refreshTimer.Interval = TimeSpan.FromSeconds(1);
            _refreshTimer.IsRepeating = true;
            _refreshTimer.Tick += RefreshTimer_Tick;
        }
        _localizationService.LanguageChanged += LocalizationService_LanguageChanged;
        PomodoroSettingsStore.Changed += SettingsStore_Changed;

        if (_stateMachine.ShouldPersistRestore || completionAlertMetadataChanged)
        {
            PersistConfig();
        }
    }

    public WidgetConfig Config { get; }

    /// <summary>当前阶段的稳定标识，供视觉层选择阶段样式。</summary>
    public PomodoroTimerPhase Phase => _snapshot.Phase;

    /// <summary>当前阶段剩余时间。</summary>
    public TimeSpan Remaining => _snapshot.Remaining;

    public string CountdownText => FormatCountdown(_snapshot.Remaining);

    public string PhaseText => _localizationService.T(Phase switch
    {
        PomodoroTimerPhase.Focus => "Pomodoro.Phase.Focus",
        PomodoroTimerPhase.ShortBreak => "Pomodoro.Phase.ShortBreak",
        PomodoroTimerPhase.LongBreak => "Pomodoro.Phase.LongBreak",
        _ => throw new ArgumentOutOfRangeException()
    });

    public string DescriptionText => _localizationService.T(Phase switch
    {
        PomodoroTimerPhase.Focus => "Pomodoro.Focus.Description",
        PomodoroTimerPhase.ShortBreak => "Pomodoro.ShortBreak.Description",
        PomodoroTimerPhase.LongBreak => "Pomodoro.LongBreak.Description",
        _ => throw new ArgumentOutOfRangeException()
    });

    public string RoundSummaryText => _localizationService.Format(
        "Pomodoro.RoundSummary",
        RoundNumber,
        RoundCount);

    public string PrimaryActionText => _localizationService.T(
        IsRunning ? "Pomodoro.Action.Pause" : "Pomodoro.Action.Start");

    public string PrimaryActionGlyph => IsRunning ? PauseGlyph : PlayGlyph;

    public string ResetActionText =>
        _localizationService.T("Pomodoro.Action.Reset");

    public string SkipActionText =>
        _localizationService.T("Pomodoro.Action.Skip");

    public bool IsRunning => _snapshot.IsRunning;

    public bool IsFocusPhase => Phase == PomodoroTimerPhase.Focus;

    public bool IsShortBreakPhase => Phase == PomodoroTimerPhase.ShortBreak;

    public bool IsLongBreakPhase => Phase == PomodoroTimerPhase.LongBreak;

    public bool IsBreakPhase => !IsFocusPhase;

    /// <summary>
    /// 指示最近一次自然完成是否仍需向用户展示持续提醒。
    /// 该状态仅由开始、跳过或重置操作清除。
    /// </summary>
    public bool IsCompletionAlertActive => _isCompletionAlertActive;

    public int CompletedFocusRounds => _snapshot.CompletedFocusRounds;

    public int RoundCount => _stateMachine.RoundCount;

    public int CompletedRoundsInCycle
    {
        get
        {
            if (IsLongBreakPhase)
            {
                return RoundCount;
            }

            return Math.Clamp(CompletedFocusRounds, 0, RoundCount);
        }
    }

    public int RoundNumber
    {
        get
        {
            return Phase switch
            {
                PomodoroTimerPhase.Focus => Math.Clamp(
                    CompletedRoundsInCycle + 1,
                    1,
                    RoundCount),
                PomodoroTimerPhase.ShortBreak => Math.Clamp(
                    CompletedRoundsInCycle,
                    1,
                    RoundCount),
                PomodoroTimerPhase.LongBreak => RoundCount,
                _ => throw new ArgumentOutOfRangeException()
            };
        }
    }

    public TimeSpan PhaseDuration => _stateMachine.CurrentPhaseDuration;

    public double Progress
    {
        get
        {
            TimeSpan duration = PhaseDuration;
            if (duration <= TimeSpan.Zero)
            {
                return 0;
            }

            return Math.Clamp(
                1 - _snapshot.Remaining.TotalMilliseconds /
                duration.TotalMilliseconds,
                0,
                1);
        }
    }

    /// <summary>自然完成一个阶段时触发，用于播放一次性完成反馈。</summary>
    public event EventHandler? CompletionOccurred;

    public Task InitializeAsync()
    {
        ThrowIfDisposed();
        _isInitialized = true;
        RefreshFromClock();
        UpdateRefreshTimer();
        return Task.CompletedTask;
    }

    public Task RefreshAsync()
    {
        ThrowIfDisposed();
        RefreshFromClock();
        return Task.CompletedTask;
    }

    public void StartPause()
    {
        RunOnUiThread(() =>
        {
            bool isStarting = !IsRunning;
            PomodoroTimerUpdate update = isStarting
                ? _stateMachine.Start(GetUtcNow())
                : _stateMachine.Pause(GetUtcNow());
            ApplyUpdate(update, clearCompletionAlert: isStarting);
        });
    }

    public void Reset()
    {
        RunOnUiThread(() => ApplyUpdate(
            _stateMachine.Reset(GetUtcNow()),
            clearCompletionAlert: true));
    }

    public void Skip()
    {
        RunOnUiThread(() => ApplyUpdate(
            _stateMachine.Skip(GetUtcNow()),
            clearCompletionAlert: true));
    }

    public void OnActivated()
    {
        if (!_isDisposed)
        {
            RefreshFromClock();
        }
    }

    public void OnDeactivated()
    {
        // 格子失去前台焦点后仍可能可见，因此继续刷新倒计时。
    }

    public void OnWindowVisibilityChanged(bool visible)
    {
        if (_isDisposed)
        {
            return;
        }

        if (visible && _isInitialized)
        {
            RefreshFromClock();
        }

        UpdateRefreshTimer();
    }

    public void OnWindowRevealCompleted()
    {
        if (!_isDisposed)
        {
            RefreshFromClock();
        }
    }

    public void ApplyAppearance()
    {
        PublishPresentation();
    }

    private void RefreshTimer_Tick(DispatcherQueueTimer sender, object args)
    {
        RefreshFromClock();
    }

    private void RefreshFromClock()
    {
        if (_isDisposed)
        {
            return;
        }

        ApplyUpdate(_stateMachine.Tick(GetUtcNow()));
    }

    private void ApplyUpdate(
        PomodoroTimerUpdate update,
        bool clearCompletionAlert = false)
    {
        bool completedNaturally = IsNaturalCompletion(update.Transition);
        bool completionAlertChanged = completedNaturally &&
            SetCompletionAlertActive(true);
        if (clearCompletionAlert)
        {
            completionAlertChanged |= SetCompletionAlertActive(false);
        }

        _snapshot = update.Snapshot;
        if (update.ShouldPersist || completionAlertChanged)
        {
            PersistConfig();
        }

        PublishPresentation();
        UpdateRefreshTimer();
        if (completedNaturally)
        {
            CompletionOccurred?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool RestoreCompletionAlert()
    {
        if (!Config.Metadata.TryGetValue(
                CompletionAlertMetadataKey,
                out string? storedValue) ||
            !bool.TryParse(storedValue, out bool isActive) ||
            !isActive)
        {
            _isCompletionAlertActive = false;
            return Config.Metadata.Remove(CompletionAlertMetadataKey);
        }

        _isCompletionAlertActive = true;
        if (storedValue == bool.TrueString)
        {
            return false;
        }

        Config.Metadata[CompletionAlertMetadataKey] = bool.TrueString;
        return true;
    }

    private bool SetCompletionAlertActive(bool isActive)
    {
        bool changed = _isCompletionAlertActive != isActive;
        _isCompletionAlertActive = isActive;

        if (isActive)
        {
            if (!Config.Metadata.TryGetValue(
                    CompletionAlertMetadataKey,
                    out string? storedValue) ||
                storedValue != bool.TrueString)
            {
                Config.Metadata[CompletionAlertMetadataKey] = bool.TrueString;
                changed = true;
            }
        }
        else if (Config.Metadata.Remove(CompletionAlertMetadataKey))
        {
            changed = true;
        }

        return changed;
    }

    private static bool IsNaturalCompletion(PomodoroTimerTransition transition) =>
        transition is PomodoroTimerTransition.FocusCompleted or
            PomodoroTimerTransition.ShortBreakCompleted or
            PomodoroTimerTransition.LongBreakCompleted;

    private void PersistConfig()
    {
        if (_settingsService is null)
        {
            return;
        }

        try
        {
            _settingsService.UpdateWidget(Config, notifySubscribers: false);
        }
        catch (Exception ex)
        {
            App.Log($"[PomodoroWidget] Failed to persist timer state: {ex}");
        }
    }

    private void UpdateRefreshTimer()
    {
        // 这是负责阶段完成判定的低频状态计时器，不是视觉帧循环。
        // 即使窗口不可见也必须继续运行，才能准时触发提示音和系统通知。
        bool shouldRun = _isInitialized &&
            !_isDisposed &&
            IsRunning;
        if (shouldRun)
        {
            if (_refreshTimer is not null && !_refreshTimer.IsRunning)
            {
                _refreshTimer.Start();
            }
        }
        else if (_refreshTimer is not null && _refreshTimer.IsRunning)
        {
            _refreshTimer.Stop();
        }
    }

    private void LocalizationService_LanguageChanged()
    {
        if (_isDisposed)
        {
            return;
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            PublishPresentation(localizedTextChanged: true);
        }
        else
        {
            _dispatcherQueue.TryEnqueue(() =>
                PublishPresentation(localizedTextChanged: true));
        }
    }

    private void SettingsStore_Changed()
    {
        if (_isDisposed)
        {
            return;
        }

        void ApplyTimerSettings()
        {
            if (_isDisposed)
            {
                return;
            }

            PomodoroWidgetSettings settings = _settingsStore.Load();
            ApplyUpdate(_stateMachine.UpdateSettings(
                settings.FocusMinutes,
                settings.ShortBreakMinutes,
                settings.LongBreakMinutes,
                settings.RoundCount,
                GetUtcNow()));
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            ApplyTimerSettings();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(ApplyTimerSettings);
        }
    }

    private static DispatcherQueue? TryGetCurrentDispatcherQueue()
    {
        try
        {
            return DispatcherQueue.GetForCurrentThread();
        }
        catch (Exception ex) when (
            ex is System.Runtime.InteropServices.COMException or
            InvalidOperationException)
        {
            // WinUI/COM 尚未在当前线程初始化时，探测 DispatcherQueue
            // 会抛异常；降级为无计时器模式即可保持状态操作可用。
            return null;
        }
    }

    private readonly record struct PresentationSnapshot(
        PomodoroTimerPhase Phase,
        TimeSpan Remaining,
        bool IsRunning,
        bool IsCompletionAlertActive,
        int CompletedFocusRounds,
        int RoundCount,
        int CompletedRoundsInCycle,
        int RoundNumber,
        TimeSpan PhaseDuration,
        double Progress);

    private PresentationSnapshot CapturePresentation() => new(
        Phase,
        Remaining,
        IsRunning,
        IsCompletionAlertActive,
        CompletedFocusRounds,
        RoundCount,
        CompletedRoundsInCycle,
        RoundNumber,
        PhaseDuration,
        Progress);

    private void PublishPresentation(bool localizedTextChanged = false)
    {
        if (_isDisposed)
        {
            return;
        }

        PresentationSnapshot previous = _publishedPresentation;
        PresentationSnapshot current = CapturePresentation();
        // Publish the baseline before notifying synchronous consumers. A
        // re-entrant refresh must not replay the same state transition.
        _publishedPresentation = current;
        bool phaseChanged = previous.Phase != current.Phase;
        bool runningChanged = previous.IsRunning != current.IsRunning;
        bool roundSummaryChanged = previous.RoundNumber != current.RoundNumber ||
            previous.RoundCount != current.RoundCount;
        bool focusChanged =
            (previous.Phase == PomodoroTimerPhase.Focus) !=
            (current.Phase == PomodoroTimerPhase.Focus);

        NotifyPresentationChange(
            GetCountdownSeconds(previous.Remaining) != GetCountdownSeconds(current.Remaining),
            nameof(CountdownText));
        NotifyPresentationChange(phaseChanged, nameof(Phase));
        NotifyPresentationChange(previous.Remaining != current.Remaining, nameof(Remaining));
        NotifyPresentationChange(phaseChanged || localizedTextChanged, nameof(PhaseText));
        NotifyPresentationChange(phaseChanged || localizedTextChanged, nameof(DescriptionText));
        NotifyPresentationChange(roundSummaryChanged || localizedTextChanged, nameof(RoundSummaryText));
        NotifyPresentationChange(runningChanged || localizedTextChanged, nameof(PrimaryActionText));
        NotifyPresentationChange(runningChanged, nameof(PrimaryActionGlyph));
        NotifyPresentationChange(localizedTextChanged, nameof(ResetActionText));
        NotifyPresentationChange(localizedTextChanged, nameof(SkipActionText));
        NotifyPresentationChange(runningChanged, nameof(IsRunning));
        NotifyPresentationChange(focusChanged, nameof(IsFocusPhase));
        NotifyPresentationChange(
            (previous.Phase == PomodoroTimerPhase.ShortBreak) !=
                (current.Phase == PomodoroTimerPhase.ShortBreak),
            nameof(IsShortBreakPhase));
        NotifyPresentationChange(
            (previous.Phase == PomodoroTimerPhase.LongBreak) !=
                (current.Phase == PomodoroTimerPhase.LongBreak),
            nameof(IsLongBreakPhase));
        NotifyPresentationChange(focusChanged, nameof(IsBreakPhase));
        NotifyPresentationChange(
            previous.IsCompletionAlertActive != current.IsCompletionAlertActive,
            nameof(IsCompletionAlertActive));
        NotifyPresentationChange(
            previous.CompletedFocusRounds != current.CompletedFocusRounds,
            nameof(CompletedFocusRounds));
        NotifyPresentationChange(previous.RoundCount != current.RoundCount, nameof(RoundCount));
        NotifyPresentationChange(
            previous.CompletedRoundsInCycle != current.CompletedRoundsInCycle,
            nameof(CompletedRoundsInCycle));
        NotifyPresentationChange(previous.RoundNumber != current.RoundNumber, nameof(RoundNumber));
        NotifyPresentationChange(previous.PhaseDuration != current.PhaseDuration, nameof(PhaseDuration));
        NotifyPresentationChange(previous.Progress != current.Progress, nameof(Progress));
    }

    private void NotifyPresentationChange(bool changed, string propertyName)
    {
        if (changed)
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void RunOnUiThread(Action action)
    {
        // Disposed is a soft skip here, mirroring the enqueue path below: a
        // timer tick that races disposal must not throw from a thread-pool
        // callback. The public entry points (:212/:221) keep ThrowIfDisposed.
        if (_isDisposed)
        {
            return;
        }

        if (_dispatcherQueue is null || _dispatcherQueue.HasThreadAccess)
        {
            action();
            return;
        }

        _dispatcherQueue.TryEnqueue(() =>
        {
            if (!_isDisposed)
            {
                action();
            }
        });
    }

    private DateTimeOffset GetUtcNow() =>
        _utcNowProvider().ToUniversalTime();

    private static string FormatCountdown(TimeSpan remaining)
    {
        long totalSeconds = GetCountdownSeconds(remaining);
        return $"{totalSeconds / 60:00}:{totalSeconds % 60:00}";
    }

    private static long GetCountdownSeconds(TimeSpan remaining) =>
        (long)Math.Ceiling(Math.Max(0, remaining.TotalMilliseconds) / 1000d);

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_isDisposed, this);
    }

    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        _isDisposed = true;
        if (_refreshTimer is not null)
        {
            _refreshTimer.Stop();
            _refreshTimer.Tick -= RefreshTimer_Tick;
        }
        _localizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        PomodoroSettingsStore.Changed -= SettingsStore_Changed;
        CompletionOccurred = null;
    }
}
