using DeskBox.Contracts;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;

namespace DeskBox.Controls.WidgetContents;

/// <summary>
/// Adapts SearchWidgetContent to the IWidgetContent contract.
/// </summary>
public sealed class SearchWidgetContentAdapter :
    WidgetContentAdapterBase,
    IWidgetResponsiveLayoutContent
{
    public SearchWidgetContentAdapter(
        WidgetConfig config,
        LocalizationService localizationService,
        SettingsService? settingsService = null,
        Func<FrameworkElement>? viewFactory = null)
        : base(
            config,
            viewFactory ?? (() => new SearchWidgetContent(localizationService, settingsService)))
    {
        if (config.WidgetKind != WidgetKind.Search)
        {
            throw new ArgumentException("Search content requires a Search widget config.", nameof(config));
        }
    }

    // DEF-099/DEF-100: the fork retired the SearchRequested notification chain
    // — SearchWidgetContent opens the popup itself through
    // App.Current.OpenSearchPopup/OpenSearchPopupWithQuery, so the adapter
    // neither re-publishes the event nor subscribes to the content.

    public override Task RefreshAsync()
    {
        if (MaterializedView is SearchWidgetContent content)
        {
            content.UpdateContent();
        }

        return Task.CompletedTask;
    }

    public override void ApplyAppearance()
    {
        if (MaterializedView is SearchWidgetContent content)
        {
            content.ApplyAppearance();
        }
    }

    public void BeginResponsiveLayoutTransition(
        double targetContentWidth,
        double targetContentHeight,
        bool isCollapsing)
    {
        if (MaterializedView is SearchWidgetContent content)
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
        if (MaterializedView is SearchWidgetContent content)
        {
            content.CompleteResponsiveLayoutTransition(
                finalContentWidth,
                finalContentHeight);
        }
    }

    public void CancelResponsiveLayoutTransition()
    {
        if (MaterializedView is SearchWidgetContent content)
        {
            content.CancelResponsiveLayoutTransition();
        }
    }

    protected override void Dispose(bool disposing)
    {
        (MaterializedView as IDisposable)?.Dispose();
    }
}
