using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// Source contracts for the batch-C stability fixes (DEF-019/020/021/024/025).
/// App, ThemeService and the auto-organization watcher cannot be instantiated
/// headlessly (or the wiring spans app-lifetime state), so the wiring is
/// pinned with source contracts following the established pattern; the
/// WeatherService copy-on-return semantics are covered by behavior tests in
/// WeatherResilienceTests.
/// </summary>
public sealed class StabilityHardeningContractTests
{
    [Fact]
    public void ThemeService_Broadcast_IsolatesHandlerExceptions()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/ThemeService.cs");

        // The broadcast must go through a snapshot + per-handler isolation
        // (mirroring LocalizationService.RaiseLanguageChanged), not a bare
        // Invoke that lets one throwing subscriber truncate the rest.
        Assert.Contains("GetInvocationList()", source, StringComparison.Ordinal);
        Assert.Contains("RaiseAppearanceChanged();", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AppearanceChanged?.Invoke();", source, StringComparison.Ordinal);

        int snapshot = source.IndexOf("GetInvocationList()", StringComparison.Ordinal);
        int logCall = source.IndexOf("[ThemeService] AppearanceChanged handler", StringComparison.Ordinal);
        Assert.True(snapshot >= 0 && logCall > snapshot,
            "a failing handler must leave a diagnostic trail instead of escaping");
    }

    [Fact]
    public void App_RegistersGlobalExceptionBackstops_AtConstruction()
    {
        string source = ReadRepositoryFile("src/DeskBox/App.xaml.cs");

        // DEF-021: both backstops registered, wired during construction, and
        // the task backstop marks the fault observed.
        Assert.Contains("RegisterGlobalExceptionBackstops();", source, StringComparison.Ordinal);
        Assert.Contains("TaskScheduler.UnobservedTaskException", source, StringComparison.Ordinal);
        Assert.Contains("AppDomain.CurrentDomain.UnhandledException", source, StringComparison.Ordinal);
        Assert.Contains("e.SetObserved();", source, StringComparison.Ordinal);

        int registration = source.IndexOf("RegisterGlobalExceptionBackstops();", StringComparison.Ordinal);
        int ctorEnd = source.IndexOf("private static string GetProcessIntegrityReport()", StringComparison.Ordinal);
        Assert.True(registration > 0 && ctorEnd > registration,
            "backstops must be registered during App construction, before launch completes");
    }

    [Fact]
    public void OnLaunched_NamesFailingPhaseAndNotifiesUser()
    {
        string source = ReadRepositoryFile("src/DeskBox/App.xaml.cs");

        // DEF-020: phase marker lives outside the try (catch must see it),
        // advances to widget restoration, and the catch reports it.
        int marker = source.IndexOf(
            "string startupPhase = \"settings and services\";",
            StringComparison.Ordinal);
        int tryStart = source.IndexOf("try\n        {\n            string? updateInstallOutcome",
            StringComparison.Ordinal);
        int advance = source.IndexOf("startupPhase = \"widget restoration\";", StringComparison.Ordinal);
        int catchBlock = source.IndexOf("catch (Exception ex)\n        {\n            // DEF-020",
            StringComparison.Ordinal);
        Assert.True(marker > 0 && tryStart > marker && advance > tryStart && catchBlock > advance,
            "the phase marker must be declared before the try, advanced mid-launch, and read in the catch");
        Assert.Contains("ShowStartupFailureNotification(startupPhase, ex);", source, StringComparison.Ordinal);
        Assert.Contains("Startup.Failure.Title", source, StringComparison.Ordinal);
        Assert.Contains("Startup.Failure.Body", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StartupFailureNotification_UsesBothChannelsAndNeverThrows()
    {
        string source = ReadRepositoryFile("src/DeskBox/App.xaml.cs");

        int notifyStart = source.IndexOf(
            "private void ShowStartupFailureNotification(string startupPhase, Exception ex)",
            StringComparison.Ordinal);
        Assert.True(notifyStart > 0, "the failure notifier must exist");
        string body = source[notifyStart..];

        // Native toast first, tray balloon fallback, whole method guarded.
        Assert.Contains("_nativeNotificationService?.TryShow(title, body)", body, StringComparison.Ordinal);
        Assert.Contains("_trayIcon?.ShowNotification(", body, StringComparison.Ordinal);
        Assert.Contains("NotificationIcon.Error", body, StringComparison.Ordinal);
        int firstTry = body.IndexOf("try\n        {", StringComparison.Ordinal);
        int finalCatch = body.IndexOf("catch (Exception notifyEx)", StringComparison.Ordinal);
        Assert.True(firstTry >= 0 && finalCatch > firstTry,
            "the notifier must be exception-proof end to end");
    }

    [Fact]
    public void AutoOrganizationWatcher_CycleSwap_CancelsWithoutDispose()
    {
        string source = ReadRepositoryFile("src/DeskBox/Services/DesktopAutoOrganizationWatcher.cs");

        // DEF-024: cycle swap must not dispose the CTS that watcher threads
        // are still reading; only the final Dispose tears the current one down.
        int beginCycle = source.IndexOf("private void BeginEnabledCycle()", StringComparison.Ordinal);
        Assert.True(beginCycle > 0);
        string body = source[beginCycle..];
        string method = body[..body.IndexOf("\n    private void", StringComparison.Ordinal)];
        Assert.DoesNotContain(".Dispose()", method, StringComparison.Ordinal);
        Assert.Contains("_featureCts.Cancel();", method, StringComparison.Ordinal);
        Assert.Contains(
            "CancellationTokenSource.CreateLinkedTokenSource(_lifetimeCts.Token)",
            method,
            StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }
}
