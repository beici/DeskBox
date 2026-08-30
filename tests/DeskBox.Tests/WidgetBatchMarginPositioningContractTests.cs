using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// Source-level contract for the DEF-008 fix package: the batch margin path
/// ("apply to all widgets") must persist the position anchor and the group /
/// topology layout next to the X/Y it writes, and the margin-dialog target
/// math must clamp into the work area on both the single-widget and the
/// batch path. Behavioral round-trips live in WidgetPositioningServiceTests;
/// these asserts pin the manager-level wiring that cannot be instantiated
/// headlessly.
/// </summary>
public sealed class WidgetBatchMarginPositioningContractTests
{
    [Fact]
    public void MoveVisibleWidgets_SuccessBranch_PersistsAnchorGroupAndTopologyLayout()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/WidgetManager.BulkAppearance.cs");

        int persist = IndexOf(source, "WidgetPositioningService.UpdateConfigFromPhysicalBounds(window.Config, target, workArea);");
        int anchor = IndexOf(source, "WidgetPositioningService.CaptureAnchor(window.Config, target, workArea);");
        int groupSync = IndexOf(source, "SynchronizeGroupLayoutFromMember(window.Config);");
        int topology = IndexOf(source, "CaptureCurrentTopologyLayout(window.Config);");

        Assert.True(persist >= 0, "batch success branch must persist bounds via UpdateConfigFromPhysicalBounds");
        Assert.True(anchor > persist, "anchor capture must follow the bounds persist or the restart chain reverts the batch move");
        Assert.True(groupSync > anchor, "group hosts must sync their persisted layout after the member config was updated");
        Assert.True(topology > groupSync, "the topology profile must be captured after the group layout was synced");
    }

    [Fact]
    public void MoveVisibleWidgets_KeepsPositionLockAndCompactGuards()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/WidgetManager.BulkAppearance.cs");

        Assert.Contains("window.Config.IsPositionLocked ||", source, StringComparison.Ordinal);
        Assert.Contains("window.IsCompactArrangementActive ||", source, StringComparison.Ordinal);
        Assert.Contains("window.IsCompactCollapsedState)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarginDialogTargetMath_ClampsIntoWorkAreaOnBothApplyPaths()
    {
        string source = ReadRepositoryFile("src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs");

        // Both shift helpers (per-side and uniform) are shared by the
        // single-widget path and the "apply to all" batch lambdas, so each
        // must route its computed target through the work-area clamp.
        int clampCount = Regex.Matches(
            source,
            Regex.Escape("WidgetPositioningService.EnsureVisible(")).Count;
        Assert.True(
            clampCount >= 2,
            $"expected the margin shift helpers to clamp via EnsureVisible, found {clampCount}");

        // The single-widget path keeps capturing the anchor before it
        // persists — the batch path mirrors this via CaptureAnchor.
        Assert.Contains(
            "CapturePositionAnchor(next.X, next.Y, next.Width, next.Height);",
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
