using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Round 10 (performance and large data sets, second pass) contract tests.
/// Three hot paths are pinned on their source shape because the UI parts need a
/// live XAML tree: the entry filter, the marquee hit cache, and the per-row
/// completion refresh. The filter additionally has a behavioural test — it is
/// pure filesystem code.
/// </summary>
public sealed class RoundTenPerformanceContractTests
{
    [Fact]
    public void DirectoryEntryFilter_ReadsAttributesOnce()
    {
        // Every path reaching this filter came from a directory enumeration, so
        // the entry exists. Probing File.Exists + Directory.Exists first spent
        // two extra metadata round-trips per item; on a network share a folder
        // count of 2000 entries went from 2000 syscalls to 6000.
        string service = ReadRepositoryFile("src/DeskBox/Services/FileService.cs");
        string body = SliceMethod(service, "public static bool ShouldDisplayEntry(string path)");

        Assert.DoesNotContain("File.Exists(", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Directory.Exists(", body, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(body, "File.GetAttributes("));
        Assert.Contains("catch (FileNotFoundException)", body, StringComparison.Ordinal);
        Assert.Contains("catch (DirectoryNotFoundException)", body, StringComparison.Ordinal);
    }

    [Fact]
    public void DirectoryEntryFilter_MatchesExistenceHiddenAndDesktopIni()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "deskbox-r10-entry-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        try
        {
            string visible = Path.Combine(root, "visible.txt");
            File.WriteAllText(visible, "x");

            string hidden = Path.Combine(root, "hidden.txt");
            File.WriteAllText(hidden, "x");
            File.SetAttributes(hidden, FileAttributes.Hidden);

            string desktopIni = Path.Combine(root, "desktop.ini");
            File.WriteAllText(desktopIni, "x");

            string folder = Path.Combine(root, "folder");
            Directory.CreateDirectory(folder);

            Assert.True(FileService.ShouldDisplayEntry(visible));
            Assert.True(FileService.ShouldDisplayEntry(folder));
            Assert.False(FileService.ShouldDisplayEntry(hidden));
            Assert.False(FileService.ShouldDisplayEntry(desktopIni));
            Assert.False(FileService.ShouldDisplayEntry(Path.Combine(root, "missing.txt")));
            Assert.False(FileService.ShouldDisplayEntry(string.Empty));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void MarqueeStart_WalksRealizedContainersOnly()
    {
        // ContainerFromItem is an item-to-container map walk per call; asking it
        // for every item in a thousand-item folder dominated the start of a
        // marquee. Only realized containers can be hit-tested, so the panel's
        // children are the correct enumeration.
        string surface = ReadRepositoryFile(
            "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.SelectionAndMenus.cs");
        string body = SliceMethod(surface, "private void CacheSelectionHits(");

        Assert.Contains("listView.ItemsPanelRoot is not Panel itemsPanel", body, StringComparison.Ordinal);
        Assert.Contains("listView.ItemFromContainer(container) is not WidgetItem item", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ContainerFromItem(", body, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoCompletionRefresh_TouchesOnlyTheChangedRow()
    {
        // "Complete all" raises one property change per row. Scanning
        // TodoListView.Items for the sender made that O(n²); the sender is the
        // changed item, so its container is one lookup away.
        string interaction = ReadRepositoryFile(
            "src/DeskBox/Controls/WidgetContents/TodoWidgetContent.ListInteraction.cs");
        string body = SliceMethod(
            interaction,
            "private void TodoItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)");

        Assert.Contains("TodoListView.ContainerFromItem(item) is not FrameworkElement container", body, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var visibleItem in TodoListView.Items)", body, StringComparison.Ordinal);
        Assert.DoesNotContain("ReferenceEquals(visibleItem, item)", body, StringComparison.Ordinal);
    }

    private static string SliceMethod(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Signature not found: {signature}");

        int end = source.Length;
        foreach (string marker in new[] { "\n    private ", "\n    public ", "\n    internal ", "\n    protected " })
        {
            int candidate = source.IndexOf(marker, start + signature.Length, StringComparison.Ordinal);
            if (candidate >= 0 && candidate < end)
            {
                end = candidate;
            }
        }

        Assert.True(end > start, $"Could not bound the member starting at {start}: {signature}");
        return source[start..end];
    }

    private static int CountOccurrences(string text, string value)
    {
        int count = 0;
        int index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
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
}
