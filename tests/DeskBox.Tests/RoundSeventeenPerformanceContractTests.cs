using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-17 large-collection fixes: item hydration checks membership
/// against a per-step snapshot instead of a linear scan of the live list, and a
/// multi-row tab drop applies its state changes in memory and saves once
/// instead of rewriting the whole document per selected row.
/// </summary>
public sealed class RoundSeventeenPerformanceContractTests
{
    [Fact]
    public void ItemHydration_UsesMembershipSnapshotsInsteadOfLinearScans()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/ViewModels/WidgetViewModel.ItemHydration.cs"));

        // Five hydration steps (icons, folder counts, shortcut targets, shell
        // kinds, icon refresh) build a snapshot per batch or per pass. Each one
        // replaced a `Contains` on the live collection that ran once per
        // candidate and made the step quadratic in the item count.
        Assert.Equal(7, CountOccurrences(source, "new HashSet<WidgetItem>(Items)"));

        // The apply guard deliberately keeps the live check: a snapshot taken
        // before the await could report a removed item as still live.
        string withoutSnapshots = source.Replace(
            "liveItems.Contains(",
            "LIVE_SNAPSHOT(",
            StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(withoutSnapshots, "Items.Contains(item)"));

        int applyGuard = source.IndexOf(
            "private bool CanApplyHydrationResult(",
            StringComparison.Ordinal);
        Assert.True(applyGuard >= 0, "CanApplyHydrationResult was not found");
        Assert.Contains(
            "Items.Contains(item)",
            source[applyGuard..],
            StringComparison.Ordinal);
    }

    [Fact]
    public void BulkTabDrop_AppliesInMemoryAndSavesOnce()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/ViewModels/TodoWidgetViewModel.ItemOperations.cs"));

        Assert.Contains(
            "private bool ApplyCompletedChange(",
            source,
            StringComparison.Ordinal);

        int start = source.IndexOf(
            "public async Task<int> ApplyTabDropAsync(",
            StringComparison.Ordinal);
        int end = source.IndexOf(
            "public async Task<bool> SetColorMarkerAsync(",
            StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, "ApplyTabDropAsync was not found");
        string body = source[start..end];

        // The per-item save was the defect: k selected rows meant k whole-document
        // serializations. The batch path must save at most once.
        Assert.Equal(1, CountOccurrences(body, "await SaveAsync();"));
        Assert.DoesNotContain("await SetCompletedAsync(", body, StringComparison.Ordinal);
        Assert.Contains("if (changedCount > 0)", body, StringComparison.Ordinal);
        Assert.Contains("selected.Contains(item.Id)", body, StringComparison.Ordinal);

        // The extracted transition must keep the recurrence side effects that
        // used to live inline in SetCompletedAsync.
        int helper = source.IndexOf("private bool ApplyCompletedChange(", StringComparison.Ordinal);
        int helperEnd = source.IndexOf(
            "public async Task<bool> SetImportantAsync(string itemId, bool isImportant)",
            StringComparison.Ordinal);
        Assert.True(helper >= 0 && helperEnd > helper, "ApplyCompletedChange was not found");
        string helperBody = source[helper..helperEnd];
        Assert.Contains("Items.Insert(", helperBody, StringComparison.Ordinal);
        Assert.Contains("Items.Remove(generatedOccurrence);", helperBody, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", helperBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BulkTabDrop_StillPersistsEverySelectedRow()
    {
        string widgetsRoot = Path.Combine(
            Path.GetTempPath(),
            "deskbox-round17-" + Guid.NewGuid().ToString("N"));
        var viewModel = new TodoWidgetViewModel(
            new TodoWidgetStore(widgetsRoot, "todo-widget"),
            TestServices.CreateLocalizationService(),
            new WidgetConfig
            {
                Id = "todo-widget",
                Name = "Todo",
                WidgetKind = WidgetKind.Todo
            },
            settingsService: null);

        await viewModel.InitializeAsync();
        var first = await viewModel.AddItemAsync("first");
        var second = await viewModel.AddItemAsync("second");
        var third = await viewModel.AddItemAsync("third");
        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotNull(third);

        Assert.Equal(
            3,
            await viewModel.ApplyTabDropAsync(
                [first.Id, second.Id, third.Id],
                TodoFilter.Completed));
        Assert.All(viewModel.Items, item => Assert.True(item.IsCompleted));

        Assert.Equal(
            3,
            await viewModel.ApplyTabDropAsync(
                [first.Id, second.Id, third.Id],
                TodoFilter.Active));
        Assert.All(viewModel.Items, item => Assert.False(item.IsCompleted));
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            string candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
            {
                return File.ReadAllText(candidate);
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }

    private static string StripCommentLines(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (string line in source.Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
                trimmed.StartsWith("///", StringComparison.Ordinal))
            {
                continue;
            }

            builder.AppendLine(line);
        }

        return builder.ToString();
    }

    private static int CountOccurrences(string source, string needle)
    {
        int count = 0;
        int index = 0;
        while ((index = source.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }

        return count;
    }
}
