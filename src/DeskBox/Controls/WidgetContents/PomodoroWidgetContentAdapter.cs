using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;

namespace DeskBox.Controls.WidgetContents;

/// <summary>
/// 将番茄钟界面接入统一格子内容生命周期。派生自共享适配器基座,
/// 沿用 File/QuickCapture 冻结的惰性视图与释放语义
/// (widget-group-residency-roadmap-20260918 第 4.6 节)。
/// </summary>
public sealed class PomodoroWidgetContentAdapter :
    WidgetContentAdapterBase,
    IWidgetResponsiveLayoutContent
{
    private bool _isHostWindowVisible;
    private bool _isHostCompactCollapsed;
    private bool _isCompactBoundsTransitionActive;
    private (double Width, double Height, bool IsCollapsing)? _responsiveTransition;

    public PomodoroWidgetContentAdapter(
        WidgetConfig config,
        LocalizationService localizationService,
        SettingsService? settingsService = null,
        PomodoroSettingsStore? settingsStore = null,
        Func<PomodoroWidgetViewModel, FrameworkElement>? viewFactory = null)
        : this(
            config,
            new PomodoroWidgetViewModel(
                config,
                localizationService,
                settingsService,
                settingsStore),
            viewFactory)
    {
    }

    internal PomodoroWidgetContentAdapter(
        WidgetConfig config,
        PomodoroWidgetViewModel viewModel,
        Func<PomodoroWidgetViewModel, FrameworkElement>? viewFactory)
        : base(
            config,
            () => (viewFactory ?? (vm => new PomodoroWidgetContent(vm)))(viewModel))
    {
        if (config.WidgetKind != WidgetKind.Pomodoro)
        {
            throw new ArgumentException(
                "Pomodoro content requires a Pomodoro widget config.",
                nameof(config));
        }

        ViewModel = viewModel;
        _isHostCompactCollapsed = config.IsCollapsed;
    }

    public PomodoroWidgetViewModel ViewModel { get; }

    public override Task InitializeAsync() => ViewModel.InitializeAsync();

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override void ApplyAppearance()
    {
        ViewModel.ApplyAppearance();
        if (MaterializedView is PomodoroWidgetContent content)
        {
            content.ApplyAppearance();
        }
    }

    public override void OnActivated() => ViewModel.OnActivated();

    public override void OnDeactivated() => ViewModel.OnDeactivated();

    public override void OnWindowVisibilityChanged(bool visible)
    {
        _isHostWindowVisible = visible;
        ViewModel.OnWindowVisibilityChanged(visible);
        ApplyHostPresentationState();
    }

    public override void OnWindowRevealCompleted()
    {
        ViewModel.OnWindowRevealCompleted();
        if (MaterializedView is PomodoroWidgetContent content)
        {
            content.RefreshVisiblePresentation();
        }
    }

    public override void OnCompactStateChanged(bool collapsed)
    {
        _isHostCompactCollapsed = collapsed;
        ApplyHostPresentationState();
    }

    public override void OnCompactBoundsTransitionActiveChanged(bool isActive)
    {
        _isCompactBoundsTransitionActive = isActive;
        ApplyHostPresentationState();
    }

    protected override void OnViewMaterialized(FrameworkElement view)
    {
        if (view is not PomodoroWidgetContent content)
        {
            return;
        }

        if (_responsiveTransition is { } transition)
        {
            content.BeginResponsiveLayoutTransition(
                transition.Width,
                transition.Height,
                transition.IsCollapsing);
        }

        ApplyHostPresentationState();
    }

    private void ApplyHostPresentationState()
    {
        if (MaterializedView is PomodoroWidgetContent content)
        {
            content.SetHostPresentationState(
                _isHostWindowVisible,
                _isHostCompactCollapsed,
                _isCompactBoundsTransitionActive);
        }
    }

    public void BeginResponsiveLayoutTransition(
        double targetContentWidth,
        double targetContentHeight,
        bool isCollapsing)
    {
        _responsiveTransition = (
            targetContentWidth,
            targetContentHeight,
            isCollapsing);
        if (MaterializedView is PomodoroWidgetContent content)
        {
            content.BeginResponsiveLayoutTransition(
                targetContentWidth,
                targetContentHeight,
                isCollapsing);
        }
    }

    public void CompleteResponsiveLayoutTransition(
        double finalContentWidth,
        double finalContentHeight)
    {
        _responsiveTransition = null;
        if (MaterializedView is PomodoroWidgetContent content)
        {
            content.CompleteResponsiveLayoutTransition(
                finalContentWidth,
                finalContentHeight);
        }
    }

    public void CancelResponsiveLayoutTransition()
    {
        _responsiveTransition = null;
        if (MaterializedView is PomodoroWidgetContent content)
        {
            content.CancelResponsiveLayoutTransition();
        }
    }

    protected override void Dispose(bool disposing)
    {
        (MaterializedView as IDisposable)?.Dispose();
        ViewModel.Dispose();
    }
}
