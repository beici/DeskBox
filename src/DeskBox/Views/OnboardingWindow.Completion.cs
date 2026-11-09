using CommunityToolkit.WinUI.Animations;
using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;
using WinRT.Interop;

namespace DeskBox.Views;

public sealed partial class OnboardingWindow
{
    // ════════════════════════════════════════════════════════════
    //  Localization
    // ════════════════════════════════════════════════════════════

    private void OnLanguageChanged()
    {
        // LanguageChanged can fire from a non-UI thread; every UI touch below
        // must run on the window's dispatcher (same guard as SearchPopup's
        // and SettingsWindow's handlers for this event).
        if (!DispatcherQueue.HasThreadAccess)
        {
            DispatcherQueue.TryEnqueue(OnLanguageChanged);
            return;
        }

        Title = _localizationService.T("Onboarding.WindowTitle");
        Localized.RefreshAll(_localizationService);
        PrepareIntroContent();
        SetupStep(animate: false);
        UpdateFooterState();
    }

    // ════════════════════════════════════════════════════════════
    //  Intro Sequence (preserved from original)
    // ════════════════════════════════════════════════════════════
}
