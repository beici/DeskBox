using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class QuickRevealLayerContractTests
{
    [Fact]
    public void LayerModeNormalization_AcceptsQuickReveal()
    {
        Assert.Equal(
            SettingsService.WidgetLayerModeQuickReveal,
            SettingsService.NormalizeWidgetLayerModeSetting(
                SettingsService.WidgetLayerModeQuickReveal));
    }

    [Fact]
    public void QuickRevealDismiss_HidesExistingWindowsAndConsumesOnlyMatchingDesktopDoubleClick()
    {
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs"));
        string app = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/App.Tray.cs"));

        Assert.Contains("QueueQuickRevealDismiss", manager, StringComparison.Ordinal);
        Assert.Contains("SetAllWidgetsVisibleAsync(false)", manager, StringComparison.Ordinal);
        Assert.Contains("_quickRevealDesktopDismissTracker.Record", manager, StringComparison.Ordinal);
        Assert.Contains("ConsumeQuickRevealDesktopDoubleClickDismiss", manager, StringComparison.Ordinal);
        Assert.Contains("ConsumeQuickRevealDesktopDoubleClickDismiss", app, StringComparison.Ordinal);
        Assert.DoesNotContain("TimeSpan.FromMilliseconds(700)", manager, StringComparison.Ordinal);
    }

    [Fact]
    public void DesktopDoubleClickRaise_PreservesExplorerForegroundUntilSourceClickSettles()
    {
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.cs"));
        string trayAnimation = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.TrayAnimation.cs"));
        string zOrder = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.ZOrder.cs"));

        Assert.Contains("RaiseWidgetsFromTrayCoreAsync(source)", manager, StringComparison.Ordinal);
        Assert.Contains("string.Equals(source, \"desktop-double-click\"", trayAnimation, StringComparison.Ordinal);
        Assert.Contains("_foregroundAtRaiseTime = sourceForeground", trayAnimation, StringComparison.Ordinal);
        Assert.Contains("if (!preserveSourceForeground)", trayAnimation, StringComparison.Ordinal);
        Assert.Contains("!preserveSourceForeground && IsForegroundDeskBoxWindow()", zOrder, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickReveal_RemainsTopMostWithoutActivationUntilHideAnimationCompletes()
    {
        string trayAnimation = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.TrayAnimation.cs"));
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.cs"));
        string layer = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetLayerService.cs"));
        string contentWindow = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/ContentWidgetWindow.TrayAnimations.cs"));

        Assert.Contains("HoldGroupTopMostWithoutActivation", trayAnimation, StringComparison.Ordinal);
        Assert.Contains("holdQuickRevealTopMostDuringHide", manager, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.SetWindowTopMost(handle)", layer, StringComparison.Ordinal);
        Assert.Contains("WidgetLayerService.ReleaseHiddenWindowLayer(HWnd)", contentWindow, StringComparison.Ordinal);
        int nativeHide = contentWindow.IndexOf("Win32Helper.ShowWindow(HWnd, Win32Helper.SW_HIDE)", StringComparison.Ordinal);
        int releaseLayer = contentWindow.IndexOf("WidgetLayerService.ReleaseHiddenWindowLayer(HWnd)", StringComparison.Ordinal);
        int restoreInput = contentWindow.IndexOf("SetTrayHideInputSuppressed(false)", releaseLayer, StringComparison.Ordinal);
        Assert.True(nativeHide >= 0 && nativeHide < releaseLayer && releaseLayer < restoreInput);

        int releaseStart = layer.IndexOf("internal static void ReleaseHiddenWindowLayer(", StringComparison.Ordinal);
        int releaseEnd = layer.IndexOf("public static void HoldTemporaryTopMost(", releaseStart, StringComparison.Ordinal);
        string hiddenRelease = layer[releaseStart..releaseEnd];
        Assert.Contains("ApplyDesktopPinnedActivationStyle(windowHandle)", hiddenRelease, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.IsWindowTopMost(windowHandle)", hiddenRelease, StringComparison.Ordinal);
        Assert.Contains("Win32Helper.ClearWindowTopMost(windowHandle)", hiddenRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("TryAttachToDesktopIconLayer(", hiddenRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("MoveToDesktopBottom(", hiddenRelease, StringComparison.Ordinal);
        Assert.DoesNotContain("SWP_SHOWWINDOW", hiddenRelease, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickReveal_InactiveWidgetKeepsItsFirstActivatingClick()
    {
        string bounds = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Views/WidgetWindowBase.Bounds.cs"));
        string helper = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Platform/Win32Helper.cs"));

        Assert.Contains(
            "WidgetLayerService.ShouldPreserveQuickRevealActivatingClick()",
            bounds,
            StringComparison.Ordinal);
        Assert.Contains("Win32Helper.MA_ACTIVATE", bounds, StringComparison.Ordinal);
        Assert.Contains("public const int MA_ACTIVATE = 1;", helper, StringComparison.Ordinal);
    }
}
