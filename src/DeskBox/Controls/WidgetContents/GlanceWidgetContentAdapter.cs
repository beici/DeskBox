using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace DeskBox.Controls.WidgetContents;

public sealed class GlanceWidgetContentAdapter :
    WidgetContentAdapterBase,
    IWidgetResponsiveLayoutContent
{
    private string? _compactBackgroundPath;
    private int _compactBackgroundDecodeWidth;
    private ImageSource? _compactBackground;
    private long _compactBackgroundEstimatedBytes;
    // R1-087: the responsive-layout protocol pushes absolute sizes into the
    // view model. Remember the last pushed size so a Cancelled transition can
    // push it back — otherwise the widget keeps the aborted transition's
    // target layout until the next transition "heals" it.
    private double _lastPushedLayoutWidth;
    private double _lastPushedLayoutHeight;
    private double _preTransitionLayoutWidth;
    private double _preTransitionLayoutHeight;

    public GlanceWidgetContentAdapter(
        WidgetConfig config,
        LocalizationService localizationService,
        GlanceWidgetStore? store = null,
        GlanceImageService? imageService = null,
        ICalendarPresentationSource? calendarSource = null,
        Func<GlanceWidgetViewModel, FrameworkElement>? viewFactory = null,
        SettingsService? settingsService = null)
        : this(
            config,
            new GlanceWidgetViewModel(
                config,
                localizationService,
                store,
                imageService,
                calendarSource,
                settingsService: settingsService),
            viewFactory)
    {
    }

    private GlanceWidgetContentAdapter(
        WidgetConfig config,
        GlanceWidgetViewModel viewModel,
        Func<GlanceWidgetViewModel, FrameworkElement>? viewFactory)
        : base(
            config,
            () => (viewFactory ?? (vm => new GlanceWidgetContent(vm)))(viewModel))
    {
        ViewModel = viewModel;
    }

    public GlanceWidgetViewModel ViewModel { get; }

    public override Task InitializeAsync() => ViewModel.InitializeAsync();

    public override Task RefreshAsync() => ViewModel.RefreshAsync();

    public override void ApplyAppearance() => ViewModel.ApplyAppearance();

    public override void OnActivated() => ViewModel.OnActivated();

    public override void OnDeactivated() => ViewModel.OnDeactivated();

    public override void OnWindowVisibilityChanged(bool visible) => ViewModel.OnWindowVisibilityChanged(visible);

    public override void OnWindowRevealCompleted() => ViewModel.OnWindowRevealCompleted();

    public override void OnCompactStateChanged(bool collapsed) => ViewModel.OnCompactStateChanged(collapsed);

    public void BeginResponsiveLayoutTransition(double targetContentWidth, double targetContentHeight, bool isCollapsing)
    {
        _preTransitionLayoutWidth = _lastPushedLayoutWidth;
        _preTransitionLayoutHeight = _lastPushedLayoutHeight;
        _lastPushedLayoutWidth = targetContentWidth;
        _lastPushedLayoutHeight = targetContentHeight;
        ViewModel.UpdateAvailableSize(targetContentWidth, targetContentHeight);
    }

    public void CompleteResponsiveLayoutTransition(double finalContentWidth, double finalContentHeight)
    {
        _lastPushedLayoutWidth = finalContentWidth;
        _lastPushedLayoutHeight = finalContentHeight;
        ViewModel.UpdateAvailableSize(finalContentWidth, finalContentHeight);
    }

    public void CancelResponsiveLayoutTransition()
    {
        // R1-087: push the pre-transition size back so a cancelled collapse or
        // expansion does not leave the view model laying out for a transition
        // that never landed. Zero means no prior size was ever pushed through
        // this protocol — nothing to restore (the VM keeps its own defaults).
        if (_preTransitionLayoutWidth <= 0 || _preTransitionLayoutHeight <= 0)
        {
            return;
        }

        (_lastPushedLayoutWidth, _preTransitionLayoutWidth) =
            (_preTransitionLayoutWidth, _lastPushedLayoutWidth);
        (_lastPushedLayoutHeight, _preTransitionLayoutHeight) =
            (_preTransitionLayoutHeight, _lastPushedLayoutHeight);
        ViewModel.UpdateAvailableSize(_lastPushedLayoutWidth, _lastPushedLayoutHeight);
    }

    public ImageSource? GetCompactBackgroundImage(int decodePixelWidth)
    {
        string? path = ViewModel.CurrentImagePath;
        if (string.Equals(path, _compactBackgroundPath, StringComparison.OrdinalIgnoreCase) &&
            _compactBackgroundDecodeWidth == decodePixelWidth)
        {
            return _compactBackground;
        }

        _compactBackgroundPath = path;
        _compactBackgroundDecodeWidth = decodePixelWidth;
        _compactBackground = null;
        SetCompactBackgroundEstimatedBytes(0);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return null;
        }

        try
        {
            var bitmap = new BitmapImage
            {
                DecodePixelType = DecodePixelType.Physical,
                DecodePixelWidth = Math.Max(1, decodePixelWidth)
            };
            // Streamed like the expanded surface: URI loads would accumulate
            // one un-evictable framework cache entry per rotation step the
            // capsule has shown. The bitmap fills in when the decode
            // completes, matching the URI path's own async first-load.
            _ = GlanceImageStreamLoader.LoadAsync(bitmap, path);
            _compactBackground = bitmap;
            PerformanceLogger.RecordGlanceCompactBackgroundDecode();
            SetCompactBackgroundEstimatedBytes((long)decodePixelWidth * decodePixelWidth * 4);
        }
        catch (Exception ex)
        {
            App.LogVerbose($"[GlanceWidget] Compact background failed for '{path}': {ex.Message}");
        }

        return _compactBackground;
    }

    private void SetCompactBackgroundEstimatedBytes(long estimatedBytes)
    {
        long normalizedBytes = Math.Max(0, estimatedBytes);
        long delta = normalizedBytes - _compactBackgroundEstimatedBytes;
        _compactBackgroundEstimatedBytes = normalizedBytes;
        if (delta != 0)
        {
            PerformanceLogger.AdjustGlanceCompactBackgroundEstimatedBytes(
                delta);
        }
    }

    protected override void Dispose(bool disposing)
    {
        _compactBackground = null;
        _compactBackgroundPath = null;
        SetCompactBackgroundEstimatedBytes(0);
        ViewModel.Dispose();
    }
}
