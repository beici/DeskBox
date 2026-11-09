namespace DeskBox.Tests;

/// <summary>
/// Pins the round-4 state-machine fixes:
/// the search home tab must publish recommendations that arrive after the tab
/// was created, and quick-capture detail editing must keep its subject when a
/// save changes the item's view/search match.
/// </summary>
public sealed class RoundFourStateMachineContractTests
{
    [Fact]
    public void SearchHomeTab_RebuildsVisibleRowsWhenOnlyThePoolChanged()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/ViewModels/SearchPopupViewModel.cs"));
        int start = source.IndexOf("private void RebuildEmptyStateItems()", StringComparison.Ordinal);
        Assert.True(start >= 0, "RebuildEmptyStateItems was not found.");
        int end = source.IndexOf(
            "private static SearchResultItem ToResultItem(",
            start,
            StringComparison.Ordinal);
        Assert.True(end > start, "RebuildEmptyStateItems end marker was not found.");
        string method = source[start..end];

        int tabsIndex = method.IndexOf("RebuildTabs();", StringComparison.Ordinal);
        int rebuildIndex = method.IndexOf(
            "RebuildCurrentResults(preserveSelection: true);",
            StringComparison.Ordinal);
        Assert.True(tabsIndex >= 0, "the tab rebuild call was not found.");
        Assert.True(
            rebuildIndex > tabsIndex,
            "the home tab must rebuild its visible rows after the tab structure is refreshed.");
    }

    [Fact]
    public void QuickCaptureDetail_KeepsItsSubjectWhenTheFilterHidesTheItem()
    {
        string viewModel = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/ViewModels/QuickCaptureWidgetViewModel.ItemSync.cs"));
        int finderStart = viewModel.IndexOf(
            "internal QuickCaptureItemViewModel? FindItemViewModelById(string id)",
            StringComparison.Ordinal);
        Assert.True(finderStart >= 0, "FindItemViewModelById was not found.");
        string finder = viewModel[finderStart..];
        int cacheIndex = finder.IndexOf("_itemViewModelCache.TryGetValue(id", StringComparison.Ordinal);
        int fallbackIndex = finder.IndexOf("_cachedData?.Items", StringComparison.Ordinal);
        Assert.True(cacheIndex >= 0, "the item view model cache lookup was not found.");
        Assert.True(
            fallbackIndex >= 0,
            "the id lookup must fall back to the cached store data when the cache misses.");

        // A cached view model can predate the save that just changed the body,
        // so the lookup has to refresh it from the store model before handing it
        // out; otherwise the editor re-seeds itself with the previous text.
        int updateIndex = finder.IndexOf("cached.Update(model);", StringComparison.Ordinal);
        Assert.True(
            updateIndex > cacheIndex,
            "a cached item view model must be refreshed from the store model before it is returned.");
        Assert.Contains("_itemViewModelCache.Remove(id);", finder, StringComparison.Ordinal);

        string surface = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/QuickCaptureSurfaceContent.xaml.cs"));
        int resolverStart = surface.IndexOf(
            "private async Task<QuickCaptureItemViewModel?> ResolveDetailItemAsync(string id)",
            StringComparison.Ordinal);
        Assert.True(resolverStart >= 0, "ResolveDetailItemAsync was not found.");
        int resolverEnd = surface.IndexOf(
            "private void RenewPendingDetailCreationIntent()",
            resolverStart,
            StringComparison.Ordinal);
        Assert.True(resolverEnd > resolverStart, "ResolveDetailItemAsync end marker was not found.");
        string resolver = surface[resolverStart..resolverEnd];
        Assert.Contains(
            "QuickCaptureItemViewModel? resolved = ViewModel.FindItemViewModelById(id);",
            resolver,
            StringComparison.Ordinal);
        Assert.Contains("await ViewModel.RefreshItemsAsync();", resolver, StringComparison.Ordinal);
        Assert.DoesNotContain("ViewModel.Items", resolver, StringComparison.Ordinal);
        Assert.Contains("_detailItem = await ResolveDetailItemAsync(created.Id);", surface, StringComparison.Ordinal);
        Assert.Contains("_detailItem = await ResolveDetailItemAsync(item.Id);", surface, StringComparison.Ordinal);
        Assert.Contains(
            "QuickCaptureItemViewModel? refreshed = await ResolveDetailItemAsync(item.Id);",
            surface,
            StringComparison.Ordinal);
        Assert.DoesNotContain("ResolveDetailItem(", surface, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ViewModel.Items.FirstOrDefault(entry => entry.Id == item.Id)",
            surface,
            StringComparison.Ordinal);

        // A row deleted from another surface must drop the detail subject
        // instead of letting the auto-save retry against a row that is gone.
        int missingRowIndex = surface.IndexOf(
            "if (ViewModel.FindItemViewModelById(item.Id) is null)",
            StringComparison.Ordinal);
        Assert.True(missingRowIndex >= 0, "the deleted-row branch was not found.");
        string missingRowPath = surface[missingRowIndex..];
        int dropSubjectIndex = missingRowPath.IndexOf("_detailItem = null;", StringComparison.Ordinal);
        int clearDirtyIndex = missingRowPath.IndexOf("_detailHasUnsavedChanges = false;", StringComparison.Ordinal);
        int abortSaveIndex = missingRowPath.IndexOf("return false;", StringComparison.Ordinal);
        Assert.True(dropSubjectIndex >= 0, "the deleted-row branch must drop the detail subject.");
        Assert.True(
            clearDirtyIndex > dropSubjectIndex,
            "the deleted-row branch must clear the unsaved flag after dropping the subject.");
        Assert.True(
            abortSaveIndex > clearDirtyIndex,
            "the deleted-row branch must abort the save after clearing the unsaved flag.");

        // The auto-save must not re-arm itself for a detail that has no subject;
        // that loop retried forever without ever persisting the edit.
        Assert.Contains(
            "_detailItem is not null && _detailHasUnsavedChanges",
            surface,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OnboardingCompleteFailure_ReArmsTheFinalStepToggles()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/OnboardingWindow.xaml.cs"));
        int start = source.IndexOf("private async Task CompleteOnboardingAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0, "CompleteOnboardingAsync was not found.");
        int end = source.IndexOf(
            "private async Task ShowOnboardingCompleteFailedDialogAsync()",
            start,
            StringComparison.Ordinal);
        Assert.True(end > start, "CompleteOnboardingAsync end marker was not found.");
        string method = source[start..end];

        int catchIndex = method.IndexOf("catch (Exception ex)", StringComparison.Ordinal);
        Assert.True(catchIndex >= 0, "the completion failure handler was not found.");
        string failurePath = method[catchIndex..];
        foreach (string toggle in new[]
                 {
                     "TaskStep5TodoToggle",
                     "TaskStep5QuickCaptureToggle",
                     "TaskStep5SearchToggle",
                     "TaskStep5WeatherToggle",
                     "TaskStep5MusicToggle",
                     "TaskStep5GlanceToggle",
                 })
        {
            Assert.Contains(
                $"{toggle}.IsEnabled = true;",
                failurePath,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void OnboardingStorageToggles_InvalidateInFlightRefreshes()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/OnboardingWindow.TaskFlow.cs"));
        foreach (string handler in new[]
                 {
                     "private async void TaskStep3QuickAccessToggle_Toggled(",
                     "private async void TaskStep3DesktopShortcutToggle_Toggled(",
                 })
        {
            int start = source.IndexOf(handler, StringComparison.Ordinal);
            Assert.True(start >= 0, $"{handler} was not found.");
            string body = SliceToNextMember(source, start);
            int generationIndex = body.IndexOf(
                "_storageEntryStateRefreshGeneration++;",
                StringComparison.Ordinal);
            int disableIndex = body.IndexOf("toggle.IsEnabled = false;", StringComparison.Ordinal);
            Assert.True(generationIndex >= 0, $"{handler} must invalidate in-flight refreshes.");
            Assert.True(disableIndex >= 0, $"{handler} must disable the toggle while refreshing.");
            Assert.True(
                generationIndex < disableIndex,
                $"{handler} must bump the refresh generation before it starts the gesture.");
        }
    }

    [Fact]
    public void ImportTerminalState_DoesNotTearDownASupersededSession()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ImportProgress.cs"));
        int start = source.IndexOf(
            "CancellationTokenSource? completedSession = _activeImportCancellation;",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "the terminal import session must be captured before the delay.");
        string tail = source[start..];
        int guardIndex = tail.IndexOf(
            "if (!ReferenceEquals(_activeImportCancellation, completedSession))",
            StringComparison.Ordinal);
        int teardownIndex = tail.IndexOf("CancelAndResetTrackedImport();", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0, "the teardown must check that the session is still current.");
        Assert.True(
            guardIndex < teardownIndex,
            "the session check must run before the tracked import is torn down.");
    }

    [Fact]
    public void ImportCallSites_ReleaseOnlyTheSessionTheyStarted()
    {
        foreach (string relativePath in new[]
                 {
                     "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ItemVisuals.cs",
                     "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs",
                 })
        {
            string source = StripCommentLines(ReadRepositoryFile(relativePath));
            int captures = CountOccurrences(
                source,
                "CancellationTokenSource importSession = BeginTrackedImport();");
            int releases = CountOccurrences(source, "CancelAndResetTrackedImport(importSession);");
            Assert.True(captures >= 2, $"{relativePath} must capture the import session it starts.");
            Assert.True(releases >= captures, $"{relativePath} must release every import session it captures.");
            // A release that ends an import must name the session it started. The
            // parameterless overload is only legal for the same-session early exits
            // in ItemVisuals (there are none left) and for the shutdown path in the
            // surface window, so pin both counts explicitly: a stray parameterless
            // release would cancel whichever import replaced this one mid-flight.
            int parameterlessReleases = CountOccurrences(source, "CancelAndResetTrackedImport();");
            Assert.Equal(
                relativePath.EndsWith("FileSurfaceContent.xaml.cs", StringComparison.Ordinal) ? 1 : 0,
                parameterlessReleases);
            // The remaining parameterless release (shutdown) must not sit in a finally.
            int teardownIndex = 0;
            while ((teardownIndex = source.IndexOf(
                       "CancelAndResetTrackedImport();",
                       teardownIndex,
                       StringComparison.Ordinal)) >= 0)
            {
                int windowStart = Math.Max(0, teardownIndex - 240);
                string window = source[windowStart..teardownIndex];
                Assert.DoesNotContain("finally", window, StringComparison.Ordinal);
                teardownIndex += 1;
            }
        }

        string progress = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ImportProgress.cs"));
        Assert.Contains(
            "private void CancelAndResetTrackedImport(CancellationTokenSource? expectedSession)",
            progress,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (expectedSession is not null &&",
            progress,
            StringComparison.Ordinal);
        Assert.Contains(
            "!ReferenceEquals(_activeImportCancellation, expectedSession)",
            progress,
            StringComparison.Ordinal);
    }

    [Fact]
    public void OnboardingStaleRefresh_ReEnablesBothStorageToggles()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/OnboardingWindow.TaskFlow.cs"));
        int start = source.IndexOf(
            "private async Task RefreshTaskStep3StorageEntryStateAsync(string storagePath)",
            StringComparison.Ordinal);
        Assert.True(start >= 0, "RefreshTaskStep3StorageEntryStateAsync was not found.");
        int end = source.IndexOf("private async void TaskStep3QuickAccessToggle_Toggled(", start, StringComparison.Ordinal);
        Assert.True(end > start, "the storage-entry refresh end marker was not found.");
        string method = source[start..end];

        int staleIndex = method.IndexOf("generation != _storageEntryStateRefreshGeneration", StringComparison.Ordinal);
        Assert.True(staleIndex >= 0, "the stale-refresh guard was not found.");
        // Stay inside the stale guard: the normal path later in the same method
        // re-enables the toggles too, so a slice that ran to the end of the method
        // would still pass after the stale branch lost its lines.
        int staleReturn = method.IndexOf("return;", staleIndex, StringComparison.Ordinal);
        Assert.True(staleReturn > staleIndex, "the stale-refresh guard must return early.");
        string stalePath = method[staleIndex..staleReturn];
        Assert.Contains("TaskStep3QuickAccessToggle.IsEnabled = true;", stalePath, StringComparison.Ordinal);
        Assert.Contains("TaskStep3DesktopShortcutToggle.IsEnabled = true;", stalePath, StringComparison.Ordinal);
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
