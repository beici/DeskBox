namespace DeskBox.Tests;

/// <summary>
/// Source-scan contract for the todo orphan-data cleanup wiring. The deletion
/// logic itself is covered by TodoWidgetStoreTests against an isolated root;
/// these assertions pin the two WidgetManager call sites so the hooks cannot be
/// removed silently. House precedent: IdleRuntimeLifecycleContractTests-style
/// Contains checks over production sources.
/// </summary>
public sealed class TodoOrphanCleanupContractTests
{
    [Fact]
    public void RemoveWidgetAsync_PersistsRemovalBeforeDeletingTodoStore()
    {
        string manager = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.cs"));

        int todoBranch = manager.IndexOf(
            "else if (config.WidgetKind == WidgetKind.Todo)",
            StringComparison.Ordinal);
        Assert.True(todoBranch >= 0, "RemoveWidgetAsync must handle WidgetKind.Todo.");

        // R19-S2-01 moved the settings write ahead of the store retirement:
        // a failed write must leave the widget's data in place instead of
        // orphaning a config that still points at deleted notes.
        int persistGate = manager.IndexOf(
            "if (!await _settingsService.SaveCheckedAsync())",
            StringComparison.Ordinal);
        int deleteCall = manager.IndexOf(
            "await TodoWidgetStore.DeleteForWidgetAsync(config.Id);",
            StringComparison.Ordinal);
        int remainingCheck = manager.IndexOf(
            "hasRemainingTodoWidget",
            StringComparison.Ordinal);
        int glanceBranch = manager.IndexOf(
            "if (config.WidgetKind == WidgetKind.Glance)",
            StringComparison.Ordinal);

        Assert.True(persistGate > todoBranch, "The todo branch must gate on the settings write.");
        Assert.True(
            deleteCall > persistGate,
            "The todo store may only be deleted after the removal was persisted.");
        Assert.True(
            remainingCheck > todoBranch,
            "The feature may only be disabled after checking for remaining todo widgets.");
        Assert.True(
            glanceBranch >= 0 && todoBranch > glanceBranch,
            "The todo branch mirrors the glance branch structure.");
    }

    [Fact]
    public void ResetFeatureWidgetAsync_DeletesDataForDuplicateTodoWidgets()
    {
        string features = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/Services/WidgetManager.FeatureWidgets.cs"));

        int duplicateLoop = features.IndexOf(
            "ResetFeatureWidget removed duplicate",
            StringComparison.Ordinal);
        Assert.True(duplicateLoop >= 0, "Duplicate removal loop not found.");

        string duplicateRegion = features[..duplicateLoop];
        Assert.Contains(
            "else if (kind == WidgetKind.Todo)",
            duplicateRegion,
            StringComparison.Ordinal);
        Assert.Contains(
            "await TodoWidgetStore.DeleteForWidgetAsync(duplicate.Id);",
            duplicateRegion,
            StringComparison.Ordinal);
    }
}
