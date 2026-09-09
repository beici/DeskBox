using System.Text.RegularExpressions;
using DeskBox.Models;
using DeskBox.Services;
using Windows.Graphics;

namespace DeskBox.Tests;

/// <summary>
/// Coverage for the N1 fix (margin moves must keep the capsule placement in
/// step with the expanded bounds so a later collapse does not snap back) and
/// the N3 payload change (SearchPopup mixed multi-select must not drop paths
/// that fail StorageItem resolution). WidgetManager and SearchPopupWindow
/// cannot be instantiated headlessly, so wiring is pinned with source
/// contracts (matching the existing WidgetBatchMarginPositioningContractTests
/// pattern) while the capsule math is locked with behavior tests.
/// </summary>
public sealed class MarginMoveCompactPlacementContractTests
{
    [Fact]
    public void CompactPlacement_RederivesFromMovedExpandedBounds_TranslatingByMoveDelta()
    {
        // Mirror of RefreshCompactPlacementFromExpandedBounds: the capsule
        // re-derived from the moved expanded host keeps its anchor-side
        // offset, so a collapse lands next to the moved host instead of the
        // pre-move spot. This is the exact property the N1 wiring must trigger.
        var expandedBefore = new RectInt32(100, 200, 600, 400);
        var expandedAfter = new RectInt32(140, 280, 600, 400);
        const string anchor = WidgetPositionAnchors.RightTop;
        const double scale = 1.0;
        const string contentMode = SettingsService.WidgetCompactContentModeSummary;

        RectInt32 capsuleBefore = WidgetCompactBoundsCalculator.Calculate(
            expandedBefore, anchor, scale, contentMode);
        RectInt32 capsuleAfter = WidgetCompactBoundsCalculator.Calculate(
            expandedAfter, anchor, scale, contentMode);

        int deltaX = expandedAfter.X - expandedBefore.X;
        int deltaY = expandedAfter.Y - expandedBefore.Y;

        // Right-top capsule: right edge tracks the host right edge, top edge
        // tracks the host top — both must move by exactly the host delta.
        Assert.Equal(expandedBefore.X + expandedBefore.Width - capsuleBefore.Width, capsuleBefore.X);
        Assert.Equal(expandedBefore.Y, capsuleBefore.Y);
        Assert.Equal(capsuleBefore.X + deltaX, capsuleAfter.X);
        Assert.Equal(capsuleBefore.Y + deltaY, capsuleAfter.Y);
    }

    [Fact]
    public void WindowOwnedMarginPaths_RefreshCompactPlacementAfterPersist()
    {
        string source = ReadRepositoryFile("src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs");

        // Both window-owned margin persist paths — the single-widget
        // ApplyOwnMarginToSide and the dialog-cancel restore — must refresh
        // the capsule placement after they persist the expanded geometry.
        int count = Regex.Matches(
            source,
            Regex.Escape("RefreshCompactPlacementAfterBoundsMove();")).Count;
        Assert.True(
            count >= 2,
            $"expected single + cancel margin paths to refresh compact placement, found {count}");
    }

    [Fact]
    public void BatchMove_RefreshesCompactPlacement_BeforeGroupAndTopologySync()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/WidgetManager.BulkAppearance.cs");

        int anchor = IndexOf(source, "WidgetPositioningService.CaptureAnchor(window.Config, target, workArea);");
        int compact = IndexOf(source, "window.RefreshCompactPlacementAfterBoundsMove();");
        int group = IndexOf(source, "SynchronizeGroupLayoutFromMember(window.Config);");
        int topology = IndexOf(source, "CaptureCurrentTopologyLayout(window.Config);");

        Assert.True(anchor >= 0, "batch path must still capture the position anchor first");
        Assert.True(compact > anchor, "the capsule placement refresh must follow the anchor capture");
        Assert.True(group > compact, "the group sync must see the repaired placement (host -> group copy)");
        Assert.True(topology > group, "the topology profile must be captured after the group layout sync");
    }

    [Fact]
    public void CompactRefresh_IsExposedOnWindowAndInterface()
    {
        string collapse = ReadRepositoryFile("src/DeskBox/Views/WidgetWindowBase.Collapse.cs");
        string manager = ReadRepositoryFile("src/DeskBox/Services/WidgetManager.cs");

        Assert.Contains(
            "public void RefreshCompactPlacementAfterBoundsMove()",
            collapse,
            StringComparison.Ordinal);
        Assert.Contains(
            "void RefreshCompactPlacementAfterBoundsMove();",
            manager,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SearchPopup_MixedPayload_NoLongerDropsFailedPaths()
    {
        string source = ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs");

        // The mixed-resolution branch must no longer gate SetText behind
        // "items.Count == 0" (that silently discarded the failed paths); the
        // fallback text must be written whenever any path failed to resolve.
        Assert.DoesNotContain(
            "fallbackText.Length > 0 && items.Count == 0",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (fallbackText.Length > 0)",
            source,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }

    private static int IndexOf(string source, string expected)
    {
        return source.IndexOf(expected, StringComparison.Ordinal);
    }
}
