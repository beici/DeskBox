using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Locks the drag-drop result reporting between the routed WinUI drop and
/// the native CF_HDROP fallback (agogo233 2952c9c1 ported onto the 1.5.9
/// merge).
/// <para>
/// The routed drop used to stamp the host's dedup mask on <i>arrival</i>, so
/// a WinRT payload that materialized empty (GetStorageItemsAsync fails
/// wholesale for Hidden|System shortcuts, 0x8007016C) suppressed the native
/// fallback too - the release silently did nothing. The arrival stamp is now
/// provisional: the surface reports the real outcome, success keeps the
/// stamp and failure clears it so the native fallback imports the release.
/// The fallback defers its decision until the report arrives instead of
/// cancelling at arrival, which also recovers the ordering where the native
/// callback schedules first.
/// </para>
/// </summary>
public sealed class DropResponseMaskContractTests : IDisposable
{
    public void Dispose()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            "drop-response-mask");
        if (Directory.Exists(root))
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private static string LoadSource(string relativePath) =>
        NormalizeNewlines(File.ReadAllText(TestPaths.FromRepository(relativePath)));

    [Fact]
    public void GroupDropDrop_MarksTheReleasePendingInsteadOfFinalSuccess()
    {
        string window = LoadSource(
            "src/DeskBox/Views/ContentWidgetWindow.NativeDragDrop.cs");
        string drop = SliceMethod(
            window,
            "    private void GroupFileDrop_Drop(object sender, DragEventArgs e)",
            "    /// <summary>");

        Assert.Contains("_isXamlDropPending = true;", drop);
        Assert.Contains("_lastXamlFileDropUtc = DateTimeOffset.UtcNow;", drop);
        // The fallback must NOT be cancelled at arrival: cancelling made an
        // empty routed payload unrecoverable whenever the native callback had
        // already scheduled the fallback for the same release.
        Assert.DoesNotContain("CancelPendingNativeFileDropFallback();", drop);
    }

    [Fact]
    public void NotifyXamlDropResult_OnlySuccessKeepsTheDedupStamp()
    {
        string window = LoadSource(
            "src/DeskBox/Views/ContentWidgetWindow.NativeDragDrop.cs");
        string notify = SliceMethod(
            window,
            "    internal void NotifyXamlDropResult(bool success, string widgetId)",
            "    private DateTimeOffset GetLastXamlDropUtc()");

        Assert.Contains("_isXamlDropPending = false;", notify);
        Assert.Contains("? DateTimeOffset.UtcNow", notify);
        Assert.Contains(": DateTimeOffset.MinValue", notify);
    }

    [Fact]
    public void FallbackDefersToThePendingReportBeforeImporting()
    {
        string window = LoadSource(
            "src/DeskBox/Views/ContentWidgetWindow.NativeDragDrop.cs");
        string complete = SliceMethod(
            window,
            "    private async Task CompleteNativeFileDropFallbackAsync(",
            "    private void CancelPendingNativeFileDropFallback()");

        // Bounded wait for the routed report, then stand down if none came.
        Assert.Contains("if (_isXamlDropPending)", complete);
        Assert.Contains("await Task.Delay(300, cancellation.Token)", complete);
        Assert.Contains("_isXamlDropPending)", complete);
        // The success stamp is read through the gate, never the raw field.
        Assert.Contains("GetLastXamlDropUtc()", complete);
        Assert.DoesNotContain("_lastXamlFileDropUtc <", complete);
        // A success report after the wait still cleans the preview index.
        int maskCheck = complete.IndexOf(
            "DateTimeOffset.UtcNow - GetLastXamlDropUtc()",
            StringComparison.Ordinal);
        Assert.True(maskCheck > 0, "The post-wait mask check is gone.");
        Assert.Contains("ClearPendingNativeDropInsertion()",
            complete[maskCheck..]);
    }

    [Fact]
    public void Schedule_KeepsDeferringWhilePendingInsteadOfDeciding()
    {
        string window = LoadSource(
            "src/DeskBox/Views/ContentWidgetWindow.NativeDragDrop.cs");
        string schedule = SliceMethod(
            window,
            "    private void ScheduleNativeFileDropFallback(",
            "    private async Task CompleteNativeFileDropFallbackAsync(");

        int pending = schedule.IndexOf(
            "if (_isXamlDropPending)",
            StringComparison.Ordinal);
        Assert.True(pending > 0, "The pending branch is gone.");
        // The pending branch must defer, not return.
        string pendingBranch = schedule[pending..
            schedule.IndexOf("else if", StringComparison.Ordinal)];
        Assert.DoesNotContain("return;", pendingBranch);
    }

    [Fact]
    public void NewDragGesturesClearAStalePendingReport()
    {
        string window = LoadSource(
            "src/DeskBox/Views/ContentWidgetWindow.NativeDragDrop.cs");
        string begin = SliceMethod(
            window,
            "    private void BeginGroupFileDropTracking(DataPackageView dataView)",
            "    private static bool RequiresGroupManualDropFallback(");
        Assert.Contains("_isXamlDropPending = false;", begin);

        string enter = SliceMethod(
            window,
            "    private void NativeFileDropTarget_DragEnterEvent(",
            "    private void NativeFileDropTarget_DragOverEvent(");
        Assert.Contains("_isXamlDropPending = false;", enter);
    }

    [Fact]
    public void RootDrop_ReportsOutcomeOnEveryReturnAndCatchPath()
    {
        string surface = LoadSource(
            "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs");
        string drop = SliceMethod(
            surface,
            "    private async void Root_Drop(object sender, DragEventArgs e)",
            "    internal void HandleHostEdgeDrop(DragEventArgs e)");

        // Five early returns + the payload report + two catches.
        int reports = CountOccurrences(drop, "NotifyHostXamlDropResult(");
        Assert.True(reports >= 7,
            $"Expected at least 7 drop reports, found {reports}.");
        // Only the materialized payload decides by count; the guard paths
        // resolved the release themselves and keep the native path quiet.
        Assert.Contains(
            "NotifyHostXamlDropResult(droppedFiles.Count > 0);", drop);
        Assert.Contains("if (!xamlDropNotified)", drop);
        // A blocking conflict resolution must not hand the release to the
        // native importer: it does not re-check transfer conflicts.
        string guard = drop[..drop.IndexOf(
            "if (payload.IsStackPopoverMemberDrag)",
            StringComparison.Ordinal)];
        Assert.Contains("NotifyHostXamlDropResult(success: true);", guard);
    }

    [Fact]
    public void HostResolution_UsesTheWindowHandleForGroupWindows()
    {
        string surface = LoadSource(
            "src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs");
        string notify = SliceMethod(
            surface,
            "    private void NotifyHostXamlDropResult(bool success)",
            "    private void SetImportBusy(bool isBusy)");

        Assert.Contains("window.WindowHandle == _hostWindowHandle", notify);
        Assert.Contains("NotifyXamlDropResult(success, WidgetId)", notify);
    }

    [Fact]
    public async Task DroppedFileBatch_DisposeIsIdempotentUnderTwoOwners()
    {
        string root = Path.Combine(
            Path.GetTempPath(),
            "DeskBox.Tests",
            "drop-response-mask",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string file = Path.Combine(root, "payload.txt");
        await File.WriteAllTextAsync(file, "x");

        var files = new[]
        {
            new DroppedFilePath(file, "payload.txt", ForceManagedCopy: false)
        };
        var batch = new DroppedFileBatch(files, root, skippedCount: 0);

        batch.Dispose();
        batch.Dispose();

        Assert.False(Directory.Exists(root));
    }

    private static string SliceMethod(
        string source,
        string startMarker,
        string endMarker)
    {
        int start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker not found: {startMarker}");
        int end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found after {startMarker}");
        return source[start..end];
    }

    private static string NormalizeNewlines(string source) =>
        source.Replace("\r\n", "\n").Replace("\r", "\n");

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
