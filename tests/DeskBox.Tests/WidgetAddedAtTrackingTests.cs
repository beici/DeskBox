using DeskBox.ViewModels;

namespace DeskBox.Tests;

/// <summary>
/// D3-01: the persisted "added at" dictionary describes the mapped root. The
/// folder loader used to prune entries against whatever folder it had just
/// enumerated, so an embedded navigation into a subfolder deleted every
/// root-level record and reseeded it to "now" on return.
/// </summary>
public sealed class WidgetAddedAtTrackingTests
{
    [Fact]
    public void SubfolderEnumerationNeverPrunesRootAddedTimes()
    {
        Assert.False(WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(
            @"C:\Users\user\Desktop\Projects",
            @"C:\Users\user\Desktop",
            mappedTraversalPath: null));
    }

    [Fact]
    public void RootEnumerationPrunesRegardlessOfCaseOrTrailingSeparator()
    {
        Assert.True(WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(
            @"C:\Users\user\Desktop\",
            @"C:\Users\user\Desktop",
            mappedTraversalPath: null));
        Assert.True(WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(
            @"c:\users\USER\desktop",
            @"C:\Users\user\Desktop",
            mappedTraversalPath: null));
    }

    [Fact]
    public void JunctionTraversalPathIsTreatedAsTheMappedRoot()
    {
        Assert.True(WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(
            @"C:\Users\user\Desktop",
            @"C:\Users\user\OneDrive\Desktop",
            mappedTraversalPath: @"C:\Users\user\Desktop"));
    }

    [Fact]
    public void UnknownMappedRootNeverPrunes()
    {
        Assert.False(WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(
            @"C:\Temp",
            mappedFolderPath: null,
            mappedTraversalPath: null));
    }

    [Fact]
    public void PruningLoopStaysInsideThePolicyGuard()
    {
        // The policy unit tests above would all stay green if the loader went
        // back to pruning unconditionally; this pins the wiring.
        string addedAt = File.ReadAllText(TestPaths.FromRepository(
            "src/DeskBox/ViewModels/WidgetViewModel.AddedAt.cs"))
            .Replace("\r\n", "\n");

        Assert.Contains(
            "private void ApplyPersistedAddedTimes(\n        IReadOnlyList<WidgetItem> items,\n        string enumeratedFolderPath)",
            addedAt,
            StringComparison.Ordinal);

        int guard = addedAt.IndexOf(
            "if (WidgetAddedAtPruningPolicy.ShouldPruneStaleEntries(",
            StringComparison.Ordinal);
        int prune = addedAt.IndexOf(
            "foreach (string stalePath in Config.FileAddedAtByPath.Keys",
            StringComparison.Ordinal);
        int remove = addedAt.IndexOf(
            "Config.FileAddedAtByPath.Remove(stalePath);",
            StringComparison.Ordinal);
        Assert.True(guard > 0, "the pruning loop must be guarded by the policy");
        Assert.True(prune > guard, "the pruning loop must run inside the guard");
        Assert.True(remove > prune, "the stale entries must be removed inside the guard");
    }
}
