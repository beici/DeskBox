namespace DeskBox.Tests;

/// <summary>
/// Pins the round-3 concurrency and resource hardening:
/// the debounced settings save must capture its cancellation token under the lock,
/// the search popup must drop multi-selected rows that a result rebuild removed,
/// and the quick-access Shell verbs must not await a hung STA thread forever.
/// </summary>
public sealed class RoundThreeHardeningContractTests
{
    [Fact]
    public void SaveDebounced_CapturesTheTokenWhileTheLockIsHeld()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Services/SettingsService.cs"));
        int start = source.IndexOf("public void SaveDebounced(", StringComparison.Ordinal);
        Assert.True(start >= 0, "SaveDebounced was not found.");
        int end = source.IndexOf(
            "private void NotifySettingsChangedSafely(",
            start,
            StringComparison.Ordinal);
        Assert.True(end > start, "SaveDebounced end marker was not found.");
        string method = source[start..end];

        Assert.DoesNotContain(
            "CancellationToken token = debounceCts.Token;",
            method,
            StringComparison.Ordinal);
        Assert.Contains("token = debounceCts.Token;", method, StringComparison.Ordinal);

        int lockIndex = method.IndexOf("lock (_debounceLock)", StringComparison.Ordinal);
        int disposeIndex = method.IndexOf("_debounceCts?.Dispose();", StringComparison.Ordinal);
        int captureIndex = method.IndexOf("token = debounceCts.Token;", StringComparison.Ordinal);
        Assert.True(lockIndex >= 0, "the debounce lock was not found.");
        Assert.True(disposeIndex > lockIndex, "the previous source must be disposed inside the lock.");
        Assert.True(
            captureIndex > disposeIndex,
            "the token must be captured from the new source inside the same lock block.");

        int lockOpenBrace = method.IndexOf('{', lockIndex);
        Assert.True(lockOpenBrace > lockIndex, "the debounce lock body was not found.");
        int depth = 0;
        int lockCloseBrace = -1;
        for (int i = lockOpenBrace; i < method.Length; i++)
        {
            if (method[i] == '{')
            {
                depth++;
            }
            else if (method[i] == '}')
            {
                depth--;
                if (depth == 0)
                {
                    lockCloseBrace = i;
                    break;
                }
            }
        }

        Assert.True(lockCloseBrace > lockOpenBrace, "the debounce lock body was not closed.");
        Assert.True(
            captureIndex > lockOpenBrace && captureIndex < lockCloseBrace,
            "the token must be captured inside the lock body, not merely after the dispose call.");

        // A concurrent call can dispose the source between the capture and the delay
        // registration; that path must stop quietly instead of faulting an unobserved task.
        Assert.Contains("catch (ObjectDisposedException)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void SearchPopup_DropsMultiSelectionWhenTheResultSetIsRebuilt()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs"));

        Assert.Contains(
            "private void PruneMultiSelectionToVisibleResults()",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "_multiSelectedItems.RemoveWhere(item => !_viewModel.CurrentResults.Contains(item));",
            source,
            StringComparison.Ordinal);

        int handlerStart = source.IndexOf(
            "private void ResultFilterComboBox_SelectionChanged(",
            StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "the result filter handler was not found.");
        int handlerEnd = source.IndexOf(
            "private void UpdateSortHeaders()",
            handlerStart,
            StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart, "the result filter handler end marker was not found.");
        string handlerBody = source[handlerStart..handlerEnd];
        Assert.Contains("ClearMultiSelection();", handlerBody, StringComparison.Ordinal);
        Assert.Contains(
            "if (_multiSelectedItems.Count > 0)",
            handlerBody,
            StringComparison.Ordinal);
        Assert.True(
            handlerBody.IndexOf("if (_multiSelectedItems.Count > 0)", StringComparison.Ordinal) <
            handlerBody.IndexOf("ClearMultiSelection();", StringComparison.Ordinal),
            "the filter handler must skip the selection surface while the popup is still initializing.");

        string[] batchEntryPoints =
        [
            "private void UpdateSelectionActions()",
            "private MenuFlyout BuildBatchContextMenu()",
            "private async Task CopySelectedItemsAsync(",
            "private async Task DeleteSelectedItemsAsync()",
        ];

        foreach (string entryPoint in batchEntryPoints)
        {
            int index = source.IndexOf(entryPoint, StringComparison.Ordinal);
            Assert.True(index >= 0, $"{entryPoint} was not found.");
            string slice = source[index..Math.Min(source.Length, index + 400)];
            Assert.Contains(
                "PruneMultiSelectionToVisibleResults();",
                slice,
                StringComparison.Ordinal);
        }

        // A prune that empties the selection must not leave an "open all (0)" menu behind.
        int menuIndex = source.IndexOf(
            "private MenuFlyout BuildBatchContextMenu()",
            StringComparison.Ordinal);
        Assert.True(menuIndex >= 0, "BuildBatchContextMenu was not found.");
        string menuSlice = source[menuIndex..Math.Min(source.Length, menuIndex + 700)];
        Assert.Contains(
            "if (_multiSelectedItems.Count == 0)",
            menuSlice,
            StringComparison.Ordinal);
    }

    [Fact]
    public void QuickAccessShellVerbs_DoNotAwaitTheStaThreadForever()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Helpers/ExplorerQuickAccessHelper.cs"));

        Assert.Contains(
            "private static readonly TimeSpan ShellStaOperationTimeout",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "return completion.Task.WaitAsync(ShellStaOperationTimeout);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("return completion.Task;", source, StringComparison.Ordinal);

        // Coarse guard: a timeout is only safe while the write-path callers keep
        // catching exceptions from the helper. Losing that catch turns the
        // timeout into a crash, so pin it on the calling member itself: a bare
        // file-wide Contains would also match an unrelated try/catch elsewhere.
        foreach (string relativePath in new[]
                 {
                     "src/DeskBox/Views/SettingsWindow.StorageAndUpdates.cs",
                     "src/DeskBox/Views/OnboardingWindow.TaskFlow.cs",
                 })
        {
            string callerSource = StripCommentLines(ReadRepositoryFile(relativePath));
            int checkedCalls = 0;
            foreach (string marker in new[]
                     {
                         "ExplorerQuickAccessHelper.TryPinFolderToQuickAccessAsync(",
                         "ExplorerQuickAccessHelper.TryUnpinFolderFromQuickAccessAsync(",
                     })
            {
                int callIndex = 0;
                while ((callIndex = callerSource.IndexOf(marker, callIndex, StringComparison.Ordinal)) >= 0)
                {
                    int memberStart = LastMemberStart(callerSource, callIndex);
                    string member = SliceToNextMember(callerSource, memberStart);
                    Assert.Contains("catch (Exception", member, StringComparison.Ordinal);
                    checkedCalls++;
                    callIndex += 1;
                }
            }

            Assert.True(checkedCalls > 0, $"{relativePath} must call RunShellStaAsync.");
        }
    }

    private static int LastMemberStart(string source, int index)
    {
        int best = 0;
        foreach (string marker in new[] { "\n        private ", "\n        internal ", "\n        public " })
        {
            int candidate = source.LastIndexOf(marker, index, StringComparison.Ordinal);
            if (candidate > best)
            {
                best = candidate;
            }
        }

        return best;
    }

    private static string SliceToNextMember(string source, int start)
    {
        int end = -1;
        foreach (string marker in new[] { "\n        private ", "\n        internal ", "\n        public " })
        {
            int candidate = source.IndexOf(marker, start + 1, StringComparison.Ordinal);
            if (candidate >= 0 && (end < 0 || candidate < end))
            {
                end = candidate;
            }
        }

        return end < 0 ? source[start..] : source[start..end];
    }

    private static string StripCommentLines(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (string line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "src", "DeskBox")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("DeskBox repository root was not found.");
        }

        return File.ReadAllText(Path.Combine(
            directory.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
