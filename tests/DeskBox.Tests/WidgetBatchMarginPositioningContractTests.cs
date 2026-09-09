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

        // There is now exactly one clamp, and both apply paths reach it: the
        // uniform helper resolves which side is closest and then delegates to
        // the per-side helper instead of duplicating the shift math. Assert the
        // delegation rather than a count so the contract survives that.
        Assert.Contains(
            "WidgetPositioningService.EnsureVisible(",
            source,
            StringComparison.Ordinal);
        int perSideClamp = source.IndexOf(
            "private static RectInt32? ShiftSideToMargin(",
            StringComparison.Ordinal);
        int uniformHelper = source.IndexOf(
            "private static RectInt32? ShiftBoundsToNearestEdge(",
            StringComparison.Ordinal);
        Assert.True(perSideClamp >= 0 && uniformHelper > perSideClamp);
        Assert.Contains(
            "WidgetPositioningService.EnsureVisible(",
            source[perSideClamp..uniformHelper],
            StringComparison.Ordinal);
        Assert.Contains(
            "return ShiftSideToMargin(",
            source[uniformHelper..],
            StringComparison.Ordinal);

        // The single-widget path keeps capturing the anchor before it
        // persists — the batch path mirrors this via CaptureAnchor. The move is
        // expressed on the live window rect because the resolved target belongs
        // to the resting (possibly capsule) rect.
        int anchorCapture = source.IndexOf(
            "CapturePositionAnchor(nextX, nextY, live.Width, live.Height);",
            StringComparison.Ordinal);
        int persist = source.IndexOf(
            "UpdateConfigBoundsFromPhysical(nextX, nextY, live.Width, live.Height, persist: true);",
            StringComparison.Ordinal);
        Assert.True(anchorCapture >= 0, "the single-widget path must capture the position anchor");
        Assert.True(persist > anchorCapture, "the anchor must be captured before the bounds are persisted");
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
