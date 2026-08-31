using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// Source contracts for the batch-A window-interaction fixes (DEF-014/015/017).
/// WidgetManager and the widget hosts cannot be instantiated headlessly, so
/// wiring is pinned the same way as WidgetBatchMarginPositioningContractTests:
/// string-level assertions against the repository sources, replayed on every
/// test run (the Linux static gate replays them with rg as well).
/// </summary>
public sealed class WindowInteractionSafetyNetContractTests
{
    [Fact]
    public void Watchdog_ForceReset_ExistsOnSessionManager()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/WidgetSessionManager.cs");

        Assert.Contains(
            "public void ForceResetInteractions(string reason)",
            source,
            StringComparison.Ordinal);
        Assert.Contains("_interactionDepth = 0;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Watchdog_BeginEndInteraction_PairWithWatchdogLifecycle()
    {
        string manager = ReadRepositoryFile("src/DeskBox/Services/WidgetManager.cs");
        string zorder = ReadRepositoryFile("src/DeskBox/Services/WidgetManager.ZOrder.cs");

        // Begin arms the watchdog when the depth transitions 0 -> 1...
        int beginStart = IndexOf(manager, "public void BeginWidgetInteraction(string reason)");
        int armGuard = IndexOf(manager, "if (!_sessionManager.IsInteractionActive)");
        int armCall = IndexOf(manager, "StartInteractionLeakWatchdog();");
        Assert.True(beginStart >= 0, "BeginWidgetInteraction must exist");
        Assert.True(armGuard > beginStart, "the arm guard must live inside BeginWidgetInteraction");
        Assert.True(armCall > armGuard, "the watchdog must be armed by BeginWidgetInteraction");

        // ...and End disarms it once the depth returns to zero.
        int endStart = IndexOf(manager, "public void EndWidgetInteraction(string reason)");
        int disarmCall = IndexOf(manager, "StopInteractionLeakWatchdog();");
        Assert.True(endStart >= 0, "EndWidgetInteraction must exist");
        Assert.True(disarmCall > endStart, "the watchdog must be disarmed by EndWidgetInteraction");

        // The watchdog tick uses the documented 10s no-DeskBox-foreground
        // criterion and performs the forced reset through the session manager.
        Assert.Contains(
            "TimeSpan.FromSeconds(10)",
            zorder,
            StringComparison.Ordinal);
        Assert.Contains(
            "_sessionManager.ForceResetInteractions(\"interaction-leak-watchdog\");",
            zorder,
            StringComparison.Ordinal);
        Assert.Contains(
            "IsDeskBoxForegroundWindow(foreground)",
            zorder,
            StringComparison.Ordinal);
        Assert.Contains(
            "[TrayBatch] Interaction watchdog",
            zorder,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SelfHealHook_PartialRegistration_FailsClosedAndRetries()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/WidgetShowDesktopSelfHealService.cs");

        // The idempotency guard must require BOTH hooks (a minimize-only or
        // foreground-only registration must not count as started).
        Assert.Contains(
            "private bool IsFullyRegistered => _minimizeHook != IntPtr.Zero && _foregroundHook != IntPtr.Zero;",
            source,
            StringComparison.Ordinal);
        Assert.Matches(
            new Regex(@"if \(_disposed \|\| IsFullyRegistered\)"),
            source);

        // Failures are logged (non-verbose marker) and a retry timer exists.
        Assert.Contains(
            "[ShowDesktop] Self-heal hook registration FAILED",
            source,
            StringComparison.Ordinal);
        Assert.Contains("StartHookRetryTimer();", source, StringComparison.Ordinal);
        Assert.Contains("HookRetryTimer_Tick", source, StringComparison.Ordinal);

        // Dispose releases the retry timer symmetrically.
        int disposeStart = IndexOf(source, "public void Dispose()");
        int retryStop = IndexOf(source, "_hookRetryTimer.Stop();");
        Assert.True(disposeStart >= 0 && retryStop > disposeStart,
            "Dispose must stop the hook retry timer");
    }

    [Fact]
    public void ContentHost_ActivationFailure_IsLoggedLikeQuickCaptureHost()
    {
        string content = ReadRepositoryFile("src/DeskBox/Views/ContentWidgetWindow.xaml.cs");

        // The return value of SetForegroundWindow must be observed and a
        // failure must produce the diagnostic anchor pitfall #3 of the
        // [重要勿删] manual tells maintainers to look for.
        Assert.Contains(
            "bool foregroundSet = Win32Helper.SetForegroundWindow(HWnd);",
            content,
            StringComparison.Ordinal);
        Assert.Contains(
            "[ZOrder] Content ActivateRaisedFromTrayBatch: SetForegroundWindow FAILED",
            content,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }

    private static int IndexOf(string source, string expected)
    {
        return source.IndexOf(expected, StringComparison.Ordinal);
    }
}
