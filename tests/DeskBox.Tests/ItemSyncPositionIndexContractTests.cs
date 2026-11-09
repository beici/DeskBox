namespace DeskBox.Tests;

/// <summary>
/// Pins the production integration of the final-order reconciler. Behavior and
/// move-count coverage lives in WidgetItemCollectionReconcilerTests; these checks
/// ensure the actual folder refresh and sort entry points use that path.
/// </summary>
public sealed class ItemSyncPositionIndexContractTests
{
    [Fact]
    public void BulkSyncMethods_NeverCallIndexOfPerItem()
    {
        string hydration = ReadHydration();

        Assert.DoesNotContain("Items.IndexOf(", hydration, StringComparison.Ordinal);
        string sorting = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WidgetViewModel.SortingAndWatchers.cs"))
            .Replace("\r\n", "\n");
        Assert.DoesNotContain("Items.IndexOf(", SliceMethod(sorting, "private void SortItems()"), StringComparison.Ordinal);
    }

    [Fact]
    public void RefreshAndSort_UseTheSharedFinalOrderReconciler()
    {
        string hydration = ReadHydration();

        string sync = SliceMethod(hydration, "private void SyncFolderItems(");
        Assert.Contains("WidgetItemCollectionReconciler.MergeSnapshot(", sync, StringComparison.Ordinal);
        Assert.Contains("preserveExistingIconWhenMissing: true", sync, StringComparison.Ordinal);
        Assert.Contains("WidgetManualOrderPolicy.Reconcile(", sync, StringComparison.Ordinal);
        int sort = sync.IndexOf("WidgetItemCollectionReconciler.Sort(", StringComparison.Ordinal);
        int publish = sync.IndexOf("WidgetItemCollectionReconciler.Reconcile(Items, finalOrder)", StringComparison.Ordinal);
        Assert.True(sort > 0 && publish > sort, "Compute the final order before notifying the collection.");

        string sorting = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WidgetViewModel.SortingAndWatchers.cs"))
            .Replace("\r\n", "\n");
        Assert.Contains("WidgetItemCollectionReconciler.Reconcile(Items, sortedItems)",
            SliceMethod(sorting, "private void SortItems()"), StringComparison.Ordinal);
    }

    [Fact]
    public void FullReload_DoesNotPublishAnIntermediateOrderAndThenSortAgain()
    {
        string hydration = ReadHydration();

        string reload = SliceMethod(hydration, "private async Task<bool> LoadFolderContentsAsync(");
        Assert.Contains("SyncFolderItems(items);", reload, StringComparison.Ordinal);
        Assert.DoesNotContain("SortItems();", reload, StringComparison.Ordinal);
    }

    [Fact]
    public void MetadataOnlyRefresh_InvalidatesEnabledAutomaticStacksThroughTheExistingScheduler()
    {
        string sync = SliceMethod(ReadHydration(), "private void SyncFolderItems(");
        Assert.Contains("UsesStackProjection && _settingsService.Settings.FileWidget.FileStackAutoStacking", sync, StringComparison.Ordinal);
        Assert.Contains("trackStackProjection && !stackProjectionChanged", sync, StringComparison.Ordinal);
        int capture = sync.IndexOf("WidgetStackProjectionInvalidationPolicy.Capture(", StringComparison.Ordinal);
        int merge = sync.IndexOf("ApplyRuntimeItemData(", StringComparison.Ordinal);
        int compare = sync.IndexOf("WidgetStackProjectionInvalidationPolicy.HasChanged(", StringComparison.Ordinal);
        int publish = sync.IndexOf("WidgetItemCollectionReconciler.Reconcile(", StringComparison.Ordinal);
        int invalidation = sync.IndexOf("if (stackProjectionChanged)", StringComparison.Ordinal);
        int schedule = sync.IndexOf("QueueStackDisplayRebuild();", StringComparison.Ordinal);
        Assert.True(capture > 0 && merge > capture && compare > merge && publish > compare &&
                    invalidation > publish && schedule > invalidation);
    }

    private static string ReadHydration()
    {
        return File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WidgetViewModel.ItemHydration.cs"))
            .Replace("\r\n", "\n");
    }

    private static string SliceMethod(string source, string declaration)
    {
        int start = source.IndexOf(declaration, StringComparison.Ordinal);
        Assert.True(start > 0, $"the method must exist: {declaration}");
        int end = source.IndexOf("\n    private ", start, StringComparison.Ordinal);
        Assert.True(end > start, "the method body must be followed by another member");
        return source[start..end];
    }
}
