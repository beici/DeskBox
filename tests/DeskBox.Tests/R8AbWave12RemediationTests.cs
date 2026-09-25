using DeskBox.Services;
using Windows.UI;

namespace DeskBox.Tests;

/// <summary>
/// Contracts and behavior tests for the R8-AB W1-2 wave-③ fixes:
/// FQC-04 (theme flip re-resolves record colors), DEF-098 (theme-aware
/// follow-theme baselines shared by both entrances), FQC-01 (mixed
/// multi-select delete ownership wiring), FEXC-03 (todo save-failure
/// surfacing) and DEF-094 (capture-lost wasEngaged gate).
/// Widget hosts cannot be instantiated headlessly, so the wiring is pinned
/// with source contracts following the established pattern.
/// </summary>
public sealed class R8AbWave12RemediationTests
{
    // ── FQC-04 ─────────────────────────────────────────────────

    [Fact]
    public void QuickCaptureSurface_ThemeFlip_ReappliesClipboardItemColors()
    {
        string source = ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        int handlerStart = source.IndexOf(
            "private void QuickCaptureSurfaceContent_ActualThemeChanged(",
            StringComparison.Ordinal);
        int helpersStart = source.IndexOf(
            "private void RefreshItemMaterialSurfaces()",
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0 && helpersStart > handlerStart, "theme-changed handler must exist");
        string handler = source[handlerStart..helpersStart];

        // The follow-theme channels are snapshots: a light/dark flip must
        // re-resolve them, not only the material surfaces.
        Assert.Contains("ApplyDetailMaterialSurface();", handler, StringComparison.Ordinal);
        Assert.Contains("RefreshItemMaterialSurfaces();", handler, StringComparison.Ordinal);
        Assert.Contains("ApplyClipboardItemColors();", handler, StringComparison.Ordinal);
        Assert.Contains("ApplySegmentedStyle();", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void QuickCaptureSurface_FollowThemeColors_ResolveByElementThemeNotAppResources()
    {
        string source = ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        // The record text baseline must go through the shared, element-theme
        // resolver (same source as the Settings entrance).
        Assert.Contains(
            "QuickCaptureClipboardColorSettings.ResolveFollowThemeTextColor(",
            source,
            StringComparison.Ordinal);
        // The theme token lookup must resolve against this element's theme.
        Assert.Contains(
            "NeutralInteractionBrush.ResolveThemedResource(resourceKey, this)",
            source,
            StringComparison.Ordinal);
        // The former App.Current.Resources lookup (pinned to the startup
        // theme) must be gone.
        Assert.DoesNotContain(
            "App.Current.Resources.TryGetValue(resourceKey,",
            source,
            StringComparison.Ordinal);
    }

    // ── DEF-098 ────────────────────────────────────────────────

    [Fact]
    public void FollowThemeBaselines_DifferPerTheme_AndMatchSurfacePalette()
    {
        Color darkText = QuickCaptureClipboardColorSettings.ResolveFollowThemeTextColor(isDarkTheme: true);
        Color lightText = QuickCaptureClipboardColorSettings.ResolveFollowThemeTextColor(isDarkTheme: false);
        Color darkBackground = QuickCaptureClipboardColorSettings.ResolveFollowThemeBackgroundColor(isDarkTheme: true);
        Color lightBackground = QuickCaptureClipboardColorSettings.ResolveFollowThemeBackgroundColor(isDarkTheme: false);

        // Dark pair: the palette the feature shipped with (surface fallback).
        Assert.Equal(0xFF, darkText.A);
        Assert.Equal((byte)0xF5, darkText.R);
        Assert.Equal((byte)0xF5, darkText.G);
        Assert.Equal((byte)0xF5, darkText.B);
        Assert.Equal((byte)0x28, darkBackground.R);
        Assert.Equal((byte)0x28, darkBackground.G);
        Assert.Equal((byte)0x28, darkBackground.B);

        // Light pair: the light-theme counterpart, distinct from dark.
        Assert.NotEqual(darkText, lightText);
        Assert.NotEqual(darkBackground, lightBackground);
        Assert.Equal(0xFF, lightText.A);
        Assert.Equal(0xFF, lightBackground.A);
    }

    [Fact]
    public void FollowThemeBaselines_DefaultPairs_AreReadablePerTheme()
    {
        double darkRatio = QuickCaptureClipboardColorSettings.ContrastRatio(
            QuickCaptureClipboardColorSettings.ResolveFollowThemeTextColor(isDarkTheme: true),
            QuickCaptureClipboardColorSettings.ResolveFollowThemeBackgroundColor(isDarkTheme: true));
        double lightRatio = QuickCaptureClipboardColorSettings.ContrastRatio(
            QuickCaptureClipboardColorSettings.ResolveFollowThemeTextColor(isDarkTheme: false),
            QuickCaptureClipboardColorSettings.ResolveFollowThemeBackgroundColor(isDarkTheme: false));

        Assert.True(darkRatio >= QuickCaptureClipboardColorSettings.MinimumContrastRatio);
        Assert.True(lightRatio >= QuickCaptureClipboardColorSettings.MinimumContrastRatio);
    }

    [Fact]
    public void SettingsWindow_QuickCaptureColors_UseThemeAwareSharedBaselines()
    {
        string source = ReadRepositoryFile("src/DeskBox/Views/SettingsWindow.QuickCaptureColors.cs");

        // Both entrances resolve the effective colors from the same shared,
        // theme-aware baselines (contrast validation stays same-sourced).
        Assert.Contains(
            "QuickCaptureClipboardColorSettings.ResolveFollowThemeTextColor(IsQuickCaptureDarkBaselineTheme)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "QuickCaptureClipboardColorSettings.ResolveFollowThemeBackgroundColor(IsQuickCaptureDarkBaselineTheme)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "SettingsRoot.ActualTheme == ElementTheme.Dark",
            source,
            StringComparison.Ordinal);
        // The hardcoded dark baselines must be gone.
        Assert.DoesNotContain("0xF5, 0xF5, 0xF5", source, StringComparison.Ordinal);
        Assert.DoesNotContain("0x28, 0x28, 0x28", source, StringComparison.Ordinal);
        // Displayed captions re-resolve after a theme flip while open.
        Assert.Contains(
            "ActualThemeChanged -= QuickCaptureRecordColorsHost_ThemeChanged;",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "ActualThemeChanged += QuickCaptureRecordColorsHost_ThemeChanged;",
            source,
            StringComparison.Ordinal);
    }

    // ── FQC-01 ─────────────────────────────────────────────────

    [Fact]
    public void MixedSelectionDelete_PassesPerItemOwnershipThroughTheChain()
    {
        string service = ReadRepositoryFile("src/DeskBox/Services/QuickCaptureService.cs");
        string viewModel = ReadRepositoryFile("src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.Operations.cs");
        string surface = ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        // Service: the per-item ownership overload groups recent/record ids.
        Assert.Contains(
            "IEnumerable<(string ItemId, bool IsRecent)> itemOwnerships",
            service,
            StringComparison.Ordinal);
        // VM: maps every selected view model to its real ownership.
        Assert.Contains(
            "items.Select(item => (item.Id, item.IsRecent))",
            viewModel,
            StringComparison.Ordinal);
        // The delete chain must no longer take the ids-only shape; pinning
        // (SetPinnedAsync) legitimately keeps its ids signature — out of scope.
        Assert.DoesNotContain(
            "DeleteItemsAsync(IEnumerable<string> itemIds",
            viewModel,
            StringComparison.Ordinal);
        // Surface: the selection is passed as view models; the single
        // All(IsRecent) bool must be gone.
        Assert.Contains(
            "await ViewModel.DeleteItemsAsync(selectedItems);",
            surface,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "selectedItems.All(item => item.IsRecent)",
            surface,
            StringComparison.Ordinal);
    }

    // ── FEXC-03 ────────────────────────────────────────────────

    [Fact]
    public void TodoClearAllDiscard_IsBackstoppedAndSaveFailureIsSurfaced()
    {
        string content = ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.EditingAndUndo.cs");

        // The bare discard must be gone; the call goes through the app-wide
        // backstop and the VM's SaveFailed event drives the content's
        // existing feedback channel.
        Assert.DoesNotContain("_ = ViewModel.ClearAllAsync();", content, StringComparison.Ordinal);
        Assert.Contains(
            "App.SafeFireAndForget(() => viewModel.ClearAllAsync());",
            content,
            StringComparison.Ordinal);
        Assert.Contains("ViewModel.SaveFailed += TodoViewModel_SaveFailed;", content, StringComparison.Ordinal);
        Assert.Contains(
            "FeedbackRequested?.Invoke(",
            content,
            StringComparison.Ordinal);
    }

    // ── DEF-094 ────────────────────────────────────────────────

    [Fact]
    public void DragCaptureLost_GatesRestoreCallsBehindWasEngaged()
    {
        string source = ReadRepositoryFile("src/DeskBox/Views/WidgetWindowBase.Interaction.cs");

        int methodStart = source.IndexOf(
            "protected void DragPointerCaptureLostCore(",
            StringComparison.Ordinal);
        int methodEnd = source.IndexOf(
            "// ── Interaction layer helpers",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0 && methodEnd > methodStart, "capture-lost handler must exist");
        string method = source[methodStart..methodEnd];

        // Same gate as the pointer-released path: the desktop-layer restore
        // churn only runs when a drag was actually engaged.
        Assert.Contains("bool wasEngaged = _isWindowDragEngaged;", method, StringComparison.Ordinal);
        Assert.Contains("_isWindowDragEngaged = false;", method, StringComparison.Ordinal);
        int gate = method.IndexOf("if (wasEngaged)", StringComparison.Ordinal);
        // The leading quote makes this match only the gated call, not the
        // coordinated branch's "coordinated-drag-capture-lost".
        int restore = method.IndexOf("\"drag-capture-lost\"", StringComparison.Ordinal);
        Assert.True(gate >= 0, "the wasEngaged gate must exist in the capture-lost path");
        Assert.True(restore > gate, "the restore call must sit behind the wasEngaged gate");

        // The resets are preserved (capture loss still closes out drag state).
        Assert.Contains("HasMovedTitleBarDrag = false;", method, StringComparison.Ordinal);
        Assert.Contains("DisplayChangeWatcher?.ResumeRestore();", method, StringComparison.Ordinal);
        Assert.Contains("RestoreBackdropAfterInteraction();", method, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }
}
