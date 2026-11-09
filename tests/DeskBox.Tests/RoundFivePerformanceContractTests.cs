using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-5 performance fixes. Each case is a shape contract on the hot
/// path that was measured: the fix is not a behaviour change the user can see,
/// so the regression it guards against is a silent return of the old pattern
/// (per-item forced dispatch, per-move visual-tree walks, UI-thread backup IO,
/// full-frame thumbnail decode, serialised thumbnail batches).
/// </summary>
public sealed class RoundFivePerformanceContractTests
{
    [Fact]
    public void TransferCompletion_KeepsReportingEachCompletedItem()
    {
        // Rejected optimisation: throttling this report looked like a free win
        // for large batches, but callers use CompletedItems to advance per-item
        // progress and to cancel between items (see
        // FileServiceTests.ExecuteTransferPlanAsync_CancelAfterFirstItemKeepsCompletedItems).
        // A throttled report hides completed items for up to the throttle window
        // and lets the next item run after a cancel. The dispatcher cost is the
        // price of that contract, so the forced report must stay.
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/FileService.TransferProgress.cs"));
        int start = source.IndexOf("public void CompleteItem(string? itemName)", StringComparison.Ordinal);
        Assert.True(start >= 0, "CompleteItem was not found.");
        int end = source.IndexOf("public void Report(FileTransferPhase phase, bool force)", start, StringComparison.Ordinal);
        Assert.True(end > start, "the CompleteItem end marker was not found.");
        string method = source[start..end];

        Assert.Contains("_completedItems = Math.Min(totalItems, _completedItems + 1);", method, StringComparison.Ordinal);
        Assert.Contains("Report(FileTransferPhase.Transferring, force: true);", method, StringComparison.Ordinal);
        Assert.DoesNotContain("force: false", method, StringComparison.Ordinal);
    }

    [Fact]
    public void FolderCountHydration_DoesNotProbeExistenceOnTheUiThread()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/ViewModels/WidgetViewModel.ItemHydration.cs"));
        int start = source.IndexOf("private async Task HydrateFolderItemCountsAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "HydrateFolderItemCountsAsync was not found.");
        int end = source.IndexOf("private async Task HydrateShortcutTargetsThenShellKindsAsync(", start, StringComparison.Ordinal);
        Assert.True(end > start, "the hydration end marker was not found.");
        string method = source[start..end];

        Assert.DoesNotContain("Directory.Exists(item.Path)", method, StringComparison.Ordinal);
        // Existence still has to be validated, just off the dispatcher.
        Assert.Contains("CountVisibleChildrenAsync(path)", method, StringComparison.Ordinal);
        Assert.Contains("MarkFolderItemCountUnavailable(item, path, generation, ex);", method, StringComparison.Ordinal);
    }

    [Fact]
    public void BackupWork_RunsOffTheCallingThread()
    {
        // The heavy chain is offloaded by the production backend adapter, not by
        // the scheduler: BackupRuntime must keep starting the lane synchronously.
        string runtime = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Features/Backup/BackupRuntime.cs"));
        int start = runtime.IndexOf("async Task ExecuteAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0, "ExecuteAsync was not found.");
        string body = runtime[start..];

        Assert.Contains("result = await work(request.Token);", body, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run", body, StringComparison.Ordinal);

        string backend = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/BackupBackend.cs"));
        Assert.Contains(
            "Task.Run(() => local.CreateAutomaticSnapshotResultAsync(force, cancellationToken), cancellationToken);",
            backend,
            StringComparison.Ordinal);
        Assert.Contains(
            "Task.Run(() => local.ExportBackupAsync(destinationDirectory, cancellationToken), cancellationToken);",
            backend,
            StringComparison.Ordinal);
        Assert.Contains("Task.Run(", backend, StringComparison.Ordinal);
    }

    [Fact]
    public void MarqueeSelection_RefreshesOnlyTheChangedSurfaces()
    {
        string menus = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.SelectionAndMenus.cs"));
        int start = menus.IndexOf("private void ApplySelectionPreview(ListViewBase listView)", StringComparison.Ordinal);
        Assert.True(start >= 0, "ApplySelectionPreview was not found.");
        int end = menus.IndexOf("private void UpdateSelectionRectangle(ListViewBase listView)", start, StringComparison.Ordinal);
        Assert.True(end > start, "the ApplySelectionPreview end marker was not found.");
        string method = menus[start..end];

        Assert.Contains("RefreshItemSelectionVisuals(changed);", method, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshItemSelectionVisuals();", method, StringComparison.Ordinal);

        string visuals = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.ItemVisuals.cs"));
        Assert.Contains(
            "private void RefreshItemSelectionVisuals(\n        IReadOnlyCollection<WidgetItem> changedItems)",
            visuals.Replace("\r\n", "\n"),
            StringComparison.Ordinal);
    }

    [Fact]
    public void SearchRubberBand_CachesRowBoundsForTheGesture()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs"));

        Assert.Contains("_rubberBandRowBounds.TryGetValue(i, out Rect elementRect)", source, StringComparison.Ordinal);
        Assert.Contains("_rubberBandRowBounds[i] = elementRect;", source, StringComparison.Ordinal);
        // The cache must be dropped when a gesture starts and ends, otherwise a
        // later list rebuild would select against stale rectangles.
        Assert.True(
            CountOccurrences(source, "ResetRubberBandRowBounds();") >= 3,
            "the per-gesture row-bounds cache must be reset on start, end and capture loss.");
    }

    [Fact]
    public void ThumbnailLoadConcurrency_MatchesTheProxyBatchSize()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Helpers/IconHelper.cs"));
        Assert.Contains(
            "s_thumbLoadSemaphore = new(8, 8);",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain("s_thumbLoadSemaphore = new(2, 2);", source, StringComparison.Ordinal);

        // The media path reads whole files while holding a permit, so it must
        // keep the old width instead of riding the eight-wide shell permit.
        Assert.Contains("s_mediaThumbLoadSemaphore = new(2, 2);", source, StringComparison.Ordinal);
        int mediaStart = source.IndexOf("private static async Task<BitmapImage?> CreateMediaThumbnailAsync(", StringComparison.Ordinal);
        Assert.True(mediaStart >= 0, "CreateMediaThumbnailAsync was not found.");
        int mediaEnd = source.IndexOf("private static async Task<BitmapImage?> CreateShellThumbnailAsync(", mediaStart, StringComparison.Ordinal);
        Assert.True(mediaEnd > mediaStart, "the media method end marker was not found.");
        string media = source[mediaStart..mediaEnd];
        Assert.Contains("await s_mediaThumbLoadSemaphore.WaitAsync();", media, StringComparison.Ordinal);
        Assert.Contains("s_mediaThumbLoadSemaphore.Release();", media, StringComparison.Ordinal);
        Assert.DoesNotContain("s_thumbLoadSemaphore", media, StringComparison.Ordinal);
    }

    [Fact]
    public void ThumbnailCache_DecodesAtTargetSizeBeforeFallingBackToGdi()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/ThumbnailDiskCache.cs"));
        int start = source.IndexOf("private static async Task<byte[]?> CreateThumbnailBytesAsync(", StringComparison.Ordinal);
        Assert.True(start >= 0, "CreateThumbnailBytesAsync was not found.");
        int end = source.IndexOf("private static byte[]? CreateThumbnailBytesWithGdi(", start, StringComparison.Ordinal);
        Assert.True(end > start, "the WIC decode helper was not found before the GDI+ fallback.");
        string method = source[start..end];

        int wicIndex = method.IndexOf("WicBuiltInThumbnailDecoder", StringComparison.Ordinal);
        Assert.True(wicIndex >= 0, "the WIC decode-at-size path must be tried first.");
        int gdiFallbackIndex = method.IndexOf("CreateThumbnailBytesWithGdi(sourcePath, width)", StringComparison.Ordinal);
        Assert.True(gdiFallbackIndex > wicIndex, "the GDI+ fallback must run after the WIC attempt, not before it.");
        Assert.Contains("Image.FromFile(sourcePath, useEmbeddedColorManagement: false)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DropIndexComputation_WalksRealizedContainersOnly()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/ReorderDropIndexCalculator.cs"));
        int start = source.IndexOf("public static int Compute(", StringComparison.Ordinal);
        Assert.True(start >= 0, "Compute was not found.");
        int end = source.IndexOf("public static bool IsPointerOverRealizedItem(", start, StringComparison.Ordinal);
        Assert.True(end > start, "the Compute end marker was not found.");
        string method = source[start..end];

        Assert.DoesNotContain("ContainerFromIndex", method, StringComparison.Ordinal);
        Assert.DoesNotContain("for (int index = 0; index < list.Items.Count; index++)", method, StringComparison.Ordinal);
        Assert.Contains("list.ItemsPanelRoot is not { } realizedPanel", method, StringComparison.Ordinal);
        Assert.Contains("list.IndexFromContainer(container)", method, StringComparison.Ordinal);
    }

    [Fact]
    public void ThumbnailCleanup_IsRateLimitedAcrossWrites()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Services/ThumbnailDiskCache.cs"));
        Assert.Contains("private static long s_lastCleanupTicks;", source, StringComparison.Ordinal);
        Assert.Contains("private static readonly TimeSpan CleanupInterval", source, StringComparison.Ordinal);
        Assert.Contains(
            "if (!force && lastCleanup != 0 && now - lastCleanup < CleanupInterval.Ticks)",
            source,
            StringComparison.Ordinal);
        // Initialization still has to sweep: it is a one-shot event, so the
        // interval only guards the per-write triggers.
        Assert.Contains("QueueCleanup(force: true);", source, StringComparison.Ordinal);
        Assert.Contains("Interlocked.Exchange(ref s_lastCleanupTicks, now);", source, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThumbnailCache_StillProducesAPngForASmallImage()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "deskbox-thumbnail-cache-" + Guid.NewGuid().ToString("N"));
        string sourcePath = Path.Combine(directory, "sample.png");
        Directory.CreateDirectory(directory);
        try
        {
            File.WriteAllBytes(sourcePath, TinyPng);
            ThumbnailDiskCache.DirectoryPathOverrideForTests = Path.Combine(directory, "cache");

            byte[]? first = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 96);
            Assert.NotNull(first);
            Assert.True(first!.Length > 8);
            // PNG layout: 8-byte signature, then the IHDR chunk (length, type).
            Assert.Equal(0x89, first[0]);
            Assert.Equal((byte)'P', first[1]);
            Assert.Equal((byte)'N', first[2]);
            Assert.Equal((byte)'G', first[3]);
            Assert.Equal(0x0D, first[4]);
            Assert.Equal(0x0A, first[5]);
            Assert.Equal(0x1A, first[6]);
            Assert.Equal(0x0A, first[7]);
            Assert.Equal((byte)'I', first[12]);
            Assert.Equal((byte)'H', first[13]);
            Assert.Equal((byte)'D', first[14]);
            Assert.Equal((byte)'R', first[15]);

            byte[]? second = await ThumbnailDiskCache.GetOrCreateAsync(sourcePath, 96);
            Assert.NotNull(second);
            Assert.Equal(first.Length, second!.Length);
        }
        finally
        {
            ThumbnailDiskCache.DirectoryPathOverrideForTests = null;
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    // 1x1 transparent PNG.
    private static readonly byte[] TinyPng = Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==");

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
