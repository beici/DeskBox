using DeskBox.Controls.WidgetContents;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class PomodoroPresentationNotificationTests : IDisposable
{
    private readonly string _tempRoot = Path.Combine(
        Path.GetTempPath(), "DeskBox.Tests", Guid.NewGuid().ToString("N"));
    private readonly PomodoroSettingsStore _store;
    private readonly LocalizationService _localization;
    private DateTimeOffset _now = new(2026, 11, 5, 8, 0, 0, TimeSpan.Zero);

    public PomodoroPresentationNotificationTests()
    {
        _store = new PomodoroSettingsStore(Path.Combine(_tempRoot, "pomodoro"));
        _localization = TestServices.CreateLocalizationService();
    }

    [Fact]
    public async Task RunningTicks_NotifyOnlyChangingCountdownAndProgressValues()
    {
        using PomodoroWidgetViewModel viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.StartPause();
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, args) => notifications.Add(args.PropertyName!);

        for (int second = 0; second < 60; second++)
        {
            _now = _now.AddSeconds(1);
            await viewModel.RefreshAsync();
        }

        Assert.Equal(180, notifications.Count);
        Assert.Equal(60, notifications.Count(name => name == nameof(viewModel.CountdownText)));
        Assert.Equal(60, notifications.Count(name => name == nameof(viewModel.Remaining)));
        Assert.Equal(60, notifications.Count(name => name == nameof(viewModel.Progress)));
        Assert.Equal("24:00", viewModel.CountdownText);
    }

    [Fact]
    public async Task FractionalTicksAndUnchangedRefresh_KeepNotificationsAlignedWithPublicValues()
    {
        using PomodoroWidgetViewModel viewModel = CreateViewModel();
        await viewModel.InitializeAsync();

        AssertChanges(viewModel, viewModel.OnActivated);
        AssertChanges(viewModel, viewModel.ApplyAppearance);
        AssertChanges(viewModel, viewModel.StartPause);
        _now = _now.AddMilliseconds(250);
        AssertChanges(viewModel, viewModel.OnActivated);
        Assert.Equal("25:00", viewModel.CountdownText);
        _now = _now.AddMilliseconds(750);
        AssertChanges(viewModel, viewModel.OnActivated);
        Assert.Equal("24:59", viewModel.CountdownText);
        AssertChanges(viewModel, viewModel.OnActivated);
        AssertChanges(viewModel, viewModel.StartPause);
        _now = _now.AddMinutes(1);
        AssertChanges(viewModel, viewModel.OnActivated);
        Assert.Equal("24:59", viewModel.CountdownText);
        AssertChanges(viewModel, viewModel.Reset);
    }

    [Fact]
    public async Task CompleteCycle_UpdatesEveryChangedPhaseRoundAndAttentionPropertyExactlyOnce()
    {
        using PomodoroWidgetViewModel viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        int completions = 0;
        viewModel.CompletionOccurred += (_, _) => completions++;

        int roundCount = viewModel.RoundCount;
        for (int round = 1; round <= roundCount; round++)
        {
            Assert.Equal(PomodoroTimerPhase.Focus, viewModel.Phase);
            Assert.Equal(round, viewModel.RoundNumber);
            AssertChanges(viewModel, viewModel.StartPause);
            _now += viewModel.PhaseDuration;
            AssertChanges(viewModel, viewModel.OnActivated);
            Assert.True(viewModel.IsCompletionAlertActive);
            Assert.False(viewModel.IsRunning);
            Assert.Equal(round == roundCount
                ? PomodoroTimerPhase.LongBreak
                : PomodoroTimerPhase.ShortBreak, viewModel.Phase);

            AssertChanges(viewModel, viewModel.StartPause);
            Assert.False(viewModel.IsCompletionAlertActive);
            _now += viewModel.PhaseDuration;
            AssertChanges(viewModel, viewModel.OnActivated);
            AssertChanges(viewModel, viewModel.OnActivated);
        }

        Assert.Equal(roundCount * 2, completions);
        Assert.Equal(PomodoroTimerPhase.Focus, viewModel.Phase);
        Assert.Equal(1, viewModel.RoundNumber);
        Assert.Equal(0, viewModel.CompletedFocusRounds);
        AssertChanges(viewModel, viewModel.Skip);
        Assert.False(viewModel.IsCompletionAlertActive);
    }

    [Fact]
    public async Task SettingsChanges_RecomputeDurationProgressAndRoundDependencies()
    {
        using PomodoroWidgetViewModel viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.StartPause();
        _now = _now.AddMinutes(5);
        viewModel.OnActivated();

        AssertChanges(viewModel, () =>
        {
            _store.Update(settings => settings.FocusMinutes = 30);
            _store.WaitForPendingPersist();
        });
        Assert.Equal(TimeSpan.FromMinutes(25), viewModel.Remaining);
        Assert.Equal(TimeSpan.FromMinutes(30), viewModel.PhaseDuration);
        Assert.Equal(1d / 6d, viewModel.Progress, precision: 10);

        viewModel.Skip();
        viewModel.Skip();
        viewModel.Skip();
        Assert.Equal(PomodoroTimerPhase.ShortBreak, viewModel.Phase);
        Assert.Equal(2, viewModel.CompletedFocusRounds);
        AssertChanges(viewModel, () =>
        {
            _store.Update(settings => settings.RoundCount = 2);
            _store.WaitForPendingPersist();
        });
        Assert.Equal(PomodoroTimerPhase.LongBreak, viewModel.Phase);
        Assert.Equal(2, viewModel.RoundCount);
        Assert.Equal(2, viewModel.CompletedRoundsInCycle);
    }

    [Fact]
    public async Task LanguageChange_RefreshesLocalizedTextWithoutInvalidatingTimerState()
    {
        using PomodoroWidgetViewModel viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        viewModel.StartPause();

        AssertChanges(viewModel, () => _localization.SetLanguage("zh-CN"));
        AssertChanges(viewModel, () => _localization.SetLanguage("en-US"));
        AssertChanges(viewModel, viewModel.ApplyAppearance);
    }

    [Fact]
    public async Task ColdAdapterLifecycle_DoesNotCreateAViewOrInterruptHiddenCompletion()
    {
        PomodoroWidgetViewModel viewModel = CreateViewModel();
        int materializations = 0;
        using var adapter = new PomodoroWidgetContentAdapter(
            viewModel.Config,
            viewModel,
            _ =>
            {
                materializations++;
                throw new InvalidOperationException("Lifecycle callbacks must preserve lazy content.");
            });
        await adapter.InitializeAsync();
        int completions = 0;
        viewModel.CompletionOccurred += (_, _) => completions++;
        viewModel.StartPause();

        adapter.OnWindowVisibilityChanged(false);
        adapter.OnCompactStateChanged(true);
        adapter.BeginResponsiveLayoutTransition(300, 330, isCollapsing: false);
        adapter.OnCompactBoundsTransitionActiveChanged(true);
        adapter.ApplyAppearance();
        adapter.CancelResponsiveLayoutTransition();
        adapter.OnCompactBoundsTransitionActiveChanged(false);
        _now += viewModel.PhaseDuration;
        await adapter.RefreshAsync();

        Assert.False(viewModel.IsRunning);
        Assert.True(viewModel.IsCompletionAlertActive);
        Assert.Equal(1, completions);
        adapter.OnWindowVisibilityChanged(true);
        adapter.OnWindowRevealCompleted();
        adapter.OnCompactStateChanged(false);
        adapter.CompleteResponsiveLayoutTransition(300, 330);
        Assert.Equal(1, completions);
        Assert.Equal(0, materializations);
    }

    [Fact]
    public async Task ReentrantRefresh_DoesNotRepeatPublishedStateNotifications()
    {
        using PomodoroWidgetViewModel viewModel = CreateViewModel();
        await viewModel.InitializeAsync();
        var notifications = new List<string>();
        viewModel.PropertyChanged += (_, args) =>
        {
            notifications.Add(args.PropertyName!);
            viewModel.OnActivated();
        };

        viewModel.StartPause();

        Assert.Equal(
            new[]
            {
                nameof(viewModel.PrimaryActionText),
                nameof(viewModel.PrimaryActionGlyph),
                nameof(viewModel.IsRunning)
            },
            notifications);
    }

    private PomodoroWidgetViewModel CreateViewModel() => new(
        new WidgetConfig
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = "Pomodoro",
            WidgetKind = WidgetKind.Pomodoro
        },
        _localization,
        settingsStore: _store,
        utcNowProvider: () => _now);

    private static void AssertChanges(PomodoroWidgetViewModel viewModel, Action mutation)
    {
        Dictionary<string, object> before = CaptureValues(viewModel);
        var notifications = new List<string>();
        void Observe(object? sender, System.ComponentModel.PropertyChangedEventArgs args) =>
            notifications.Add(args.PropertyName!);

        viewModel.PropertyChanged += Observe;
        try
        {
            mutation();
        }
        finally
        {
            viewModel.PropertyChanged -= Observe;
        }

        Dictionary<string, object> after = CaptureValues(viewModel);
        string[] changedProperties = before
            .Where(pair => !Equals(pair.Value, after[pair.Key]))
            .Select(pair => pair.Key)
            .Order(StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(changedProperties, notifications.Order(StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, object> CaptureValues(PomodoroWidgetViewModel value) => new()
    {
        [nameof(value.CountdownText)] = value.CountdownText,
        [nameof(value.Phase)] = value.Phase,
        [nameof(value.Remaining)] = value.Remaining,
        [nameof(value.PhaseText)] = value.PhaseText,
        [nameof(value.DescriptionText)] = value.DescriptionText,
        [nameof(value.RoundSummaryText)] = value.RoundSummaryText,
        [nameof(value.PrimaryActionText)] = value.PrimaryActionText,
        [nameof(value.PrimaryActionGlyph)] = value.PrimaryActionGlyph,
        [nameof(value.ResetActionText)] = value.ResetActionText,
        [nameof(value.SkipActionText)] = value.SkipActionText,
        [nameof(value.IsRunning)] = value.IsRunning,
        [nameof(value.IsFocusPhase)] = value.IsFocusPhase,
        [nameof(value.IsShortBreakPhase)] = value.IsShortBreakPhase,
        [nameof(value.IsLongBreakPhase)] = value.IsLongBreakPhase,
        [nameof(value.IsBreakPhase)] = value.IsBreakPhase,
        [nameof(value.IsCompletionAlertActive)] = value.IsCompletionAlertActive,
        [nameof(value.CompletedFocusRounds)] = value.CompletedFocusRounds,
        [nameof(value.RoundCount)] = value.RoundCount,
        [nameof(value.CompletedRoundsInCycle)] = value.CompletedRoundsInCycle,
        [nameof(value.RoundNumber)] = value.RoundNumber,
        [nameof(value.PhaseDuration)] = value.PhaseDuration,
        [nameof(value.Progress)] = value.Progress
    };

    public void Dispose()
    {
        _store.WaitForPendingPersist();
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
