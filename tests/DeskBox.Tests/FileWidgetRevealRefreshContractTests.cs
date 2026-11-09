namespace DeskBox.Tests;

public sealed class FileWidgetRevealRefreshContractTests
{
    [Fact]
    public void DiskReconciliation_PreservesHydratedIconsWhenSnapshotOmitsThem()
    {
        string hydration = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WidgetViewModel.ItemHydration.cs"));
        string merge = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WidgetViewModel.SortingAndWatchers.cs"));

        // Manual and automatic ordering now share one identity-preserving merge.
        Assert.Contains("WidgetItemCollectionReconciler.MergeSnapshot(", hydration, StringComparison.Ordinal);
        Assert.Equal(
            1,
            CountOccurrences(
                hydration,
                "preserveExistingIconWhenMissing: true"));
        Assert.Contains(
            "if (!preserveExistingIconWhenMissing || source.Icon is not null)",
            merge,
            StringComparison.Ordinal);
        Assert.Contains("target.Icon = source.Icon;", merge, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string source, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
