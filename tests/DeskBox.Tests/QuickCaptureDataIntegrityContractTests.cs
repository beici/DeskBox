using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// Source contracts for the batch-B quick-capture data fixes (DEF-011/012/013).
/// The quick-capture surface cannot be instantiated headlessly, so the UI-side
/// wiring (format retention and contrast re-check) is pinned with source
/// contracts, matching the existing contract-test pattern; the undo-window
/// image retention itself is service-level and covered by behavior tests in
/// QuickCaptureServiceTests.
/// </summary>
public sealed class QuickCaptureDataIntegrityContractTests
{
    [Fact]
    public void DetailEditPaths_KeepRecordOwnContentFormat()
    {
        string source = ReadRepositoryFile(
            "src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        // All three edit-entry points must derive the detail format from the
        // record itself (item.ContentFormat) instead of overwriting it with
        // the app-level default (DEF-011 / QC-02).
        Assert.Contains(
            "_detailContentFormat = item.ContentFormat;",
            source,
            StringComparison.Ordinal);
        Assert.Equal(
            2,
            Regex.Matches(
                source,
                Regex.Escape("_detailContentFormat = _detailItem?.ContentFormat ??"),
                RegexOptions.None).Count);

        // The old overwrite must be gone from every edit path.
        Assert.DoesNotContain(
            "_detailContentFormat = ViewModel.EditorContentFormat;",
            source.Replace(
                "_detailContentFormat = _detailItem?.ContentFormat ??\r\n                    ViewModel.EditorContentFormat;",
                string.Empty, StringComparison.Ordinal)
                .Replace(
                "_detailContentFormat = _detailItem?.ContentFormat ??\n                    ViewModel.EditorContentFormat;",
                string.Empty, StringComparison.Ordinal),
            StringComparison.Ordinal);
    }

    [Fact]
    public void ServiceRegistersUndoWindowImagesOnAllDeletePathsAndReleasesOnRestore()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/QuickCaptureService.cs");

        // Register on all three delete exits (single item / recent item /
        // batch) and release on restore (DEF-012 / QC-03).
        Assert.Equal(
            3,
            Regex.Matches(source, Regex.Escape("RegisterUndoWindowImages(")).Count);
        Assert.Contains(
            "UnregisterUndoWindowImages([item]);",
            source,
            StringComparison.Ordinal);

        // The retention feeds the GC reference set and self-expires.
        Assert.Contains(
            "referenced.UnionWith(_undoWindowImagePaths.Keys);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "TimeSpan.FromSeconds(10)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void ThemeChangePath_ReChecksContrastAndFallsBackToFollowTheme()
    {
        string source = ReadRepositoryFile(
            "src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs");

        // The apply-time contrast re-check (DEF-013 / QC-05) must compare the
        // effective pair and drop the background override when it breaks.
        Assert.Contains(
            "QuickCaptureClipboardColorSettings.ContrastRatio(",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "QuickCaptureClipboardColorSettings.MinimumContrastRatio",
            source,
            StringComparison.Ordinal);
        int ratioCall = source.IndexOf(
            "QuickCaptureClipboardColorSettings.ContrastRatio(",
            StringComparison.Ordinal);
        int fallback = source.IndexOf(
            "QuickCaptureClipboardColorSettings.ModeFollowTheme);",
            StringComparison.Ordinal);
        Assert.True(ratioCall >= 0 && fallback > ratioCall,
            "the follow-theme fallback must be driven by the contrast re-check");

        // The theme-changed handler keeps applying clipboard colors (the
        // re-check rides on every application, including theme flips).
        Assert.Matches(
            new Regex(@"ActualThemeChanged[\s\S]{0,400}ApplyClipboardItemColors\(\);"),
            source);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }
}
