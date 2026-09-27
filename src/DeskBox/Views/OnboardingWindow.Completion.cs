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
