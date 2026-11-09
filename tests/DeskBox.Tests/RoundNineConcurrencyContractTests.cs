using System.Diagnostics;
using DeskBox.Services;

namespace DeskBox.Tests;

/// <summary>
/// Round 09 (concurrency and resource leaks, second pass) contract tests. The
/// widget image-source cache cannot be exercised behaviourally here because
/// its entries hold a WinUI <c>ImageSource</c>, which requires UI-thread
/// affinity; the eviction safety rule is therefore pinned on the source.
/// </summary>
public sealed class RoundNineConcurrencyContractTests
{
    [Fact]
    public async Task CrashCollector_DrainsBothStreamsBeforeWaitingForExit()
    {
        // wevtutil can emit more than a pipe buffer of diagnostics on stderr
        // when a query fails. Reading stdout to EOF first leaves the child
        // blocked writing stderr, so the call never returns. This helper emits
        // ~80 KiB on stderr before a single stdout line: the sequential form
        // deadlocks, the concurrent form completes.
        var startInfo = new ProcessStartInfo("cmd.exe")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add(
            "for /L %i in (1,1,2000) do @echo 0123456789012345678901234567890123456789 1>&2 " +
            "& echo done");

        Task<string> run = WindowsCrashEventCollector.RunProcessAsync(
            startInfo,
            CancellationToken.None);
        Task finished = await Task.WhenAny(run, Task.Delay(TimeSpan.FromSeconds(20)));

        Assert.Same(run, finished);
        Assert.Contains("done", await run, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupRegistration_AwaitsTheNonBlockingVariant()
    {
        string startup = ReadRepositoryFile("src/DeskBox/App.xaml.cs");
        int stepStart = startup.IndexOf(
            "\"desktop-double-click-activation\"",
            StringComparison.Ordinal);
        Assert.True(stepStart >= 0);

        string step = startup[stepStart..Math.Min(startup.Length, stepStart + 1200)];

        Assert.Contains("RefreshRegistrationAsync();", step, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshRegistration();", step, StringComparison.Ordinal);
        Assert.Contains(
            "await RunOptionalStartupStepAsync(\"desktop-double-click-activation\"",
            startup,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AsyncHookStart_TreatsAHandshakeTimeoutAsAFailure()
    {
        string service = ReadRepositoryFile(
            "src/DeskBox/Services/DesktopDoubleClickActivationService.cs");
        int methodStart = service.IndexOf(
            "private async Task<bool> TryStartAsync(",
            StringComparison.Ordinal);
        Assert.True(methodStart >= 0);

        int nextMember = service.IndexOf(
            "private void Stop(",
            methodStart,
            StringComparison.Ordinal);
        Assert.True(nextMember > methodStart);

        string method = service[methodStart..nextMember];

        // WaitAsync(TimeSpan) throws instead of returning false, so the timeout
        // branch only runs when the exception is caught; without the catch the
        // 1460 bookkeeping below it is unreachable and the failure escapes.
        Assert.Contains("catch (TimeoutException)", method, StringComparison.Ordinal);
        Assert.Contains("LastErrorCode = 1460;", method, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (!await ready.Task",
            method,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SharedImageCache_EvictsOnlySettledEntriesBeyondTheCap()
    {
        string cache = ReadRepositoryFile(
            "src/DeskBox/Services/WidgetSharedImageSourceCache.cs");

        Assert.Contains("private const int MaxSettledEntries = 8;", cache, StringComparison.Ordinal);
        Assert.Contains("EvictSettledBeyondCap();", cache, StringComparison.Ordinal);
        Assert.Contains(
            "if (s_entries.Count <= MaxSettledEntries)",
            cache,
            StringComparison.Ordinal);
        // A dropped entry whose fill is still in flight would strand the
        // callbacks registered on it, so only fully settled entries may go.
        Assert.Contains(
            "pair.Value.PendingFailureCallbacks is null &&",
            cache,
            StringComparison.Ordinal);
        Assert.Contains(
            "pair.Value.PendingSuccessCallbacks is null)",
            cache,
            StringComparison.Ordinal);
        Assert.Contains("entry.LastUsed = ++s_useCounter;", cache, StringComparison.Ordinal);
    }

    [Fact]
    public void HidingTheSearchPopup_EndsAnyInFlightRubberBand()
    {
        string window = ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs");

        int hideStart = window.IndexOf("public void HidePopup()", StringComparison.Ordinal);
        Assert.True(hideStart >= 0);
        int hideEnd = window.IndexOf(
            "private void OnPopupHideCompleted",
            hideStart,
            StringComparison.Ordinal);
        Assert.True(hideEnd > hideStart);

        string hideBody = window[hideStart..hideEnd];
        Assert.Contains("EndRubberBand();", hideBody, StringComparison.Ordinal);

        int endStart = window.IndexOf("private void EndRubberBand()", StringComparison.Ordinal);
        Assert.True(endStart >= 0);
        int endEnd = window.IndexOf("private void ResetRubberBandRowBounds()", endStart, StringComparison.Ordinal);
        Assert.True(endEnd > endStart);

        // The 32 ms auto-scroll timer has no other stop site: hiding mid-gesture
        // never delivers pointer-capture-lost, so EndRubberBand must be the
        // teardown that both clears the flag and stops the tick.
        string endBody = window[endStart..endEnd];
        Assert.Contains("_isRubberBanding = false;", endBody, StringComparison.Ordinal);
        Assert.Contains("_rubberBandAutoScrollTimer?.Stop();", endBody, StringComparison.Ordinal);
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
