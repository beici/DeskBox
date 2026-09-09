using System.Text.RegularExpressions;

namespace DeskBox.Tests;
/// <summary>
/// Source-anchored contract tests for the F9 remediation batch (DEF-043
/// through DEF-046). These pin the structural elements that make the fixes
/// work so a future refactor cannot silently reintroduce the defects.
/// All patterns are newline-agnostic because CI checkouts may be CRLF.
/// </summary>
public sealed class F9RemediationContractTests
{
    private static string Source(string relativePath) =>
        File.ReadAllText(TestPaths.FromRepository(relativePath));

    [Fact]
    public void TodoStore_HasPathKeyedGateAroundLoadAndSave()
    {
        string source = Source("src/DeskBox/Services/TodoWidgetStore.cs");
        // The path-keyed gate exists and wraps the public Load/Save surface.
        Assert.Matches(
            new Regex(@"s_pathGates\s*=\s*new\(\s*StringComparer\.OrdinalIgnoreCase\s*\)"),
            source);
        Assert.Matches(
            new Regex(@"GetOrAdd\(_storePath,\s*static _ => new SemaphoreSlim\(1,\s*1\)\)"),
            source);
        // Load/Save/Mutate each enter the gate; the unsafe loader is private.
        Assert.Equal(3, Regex.Matches(source, @"await _gate\.WaitAsync\(\)").Count);
        Assert.Matches(new Regex(@"private async Task<TodoWidgetData> LoadUnsafeAsync"), source);
        Assert.DoesNotMatch(new Regex(@"public async Task<TodoWidgetData> LoadUnsafeAsync"), source);
    }

    [Fact]
    public void ReminderService_WritesOnlyThroughMutateAndNotifiesWidgets()
    {
        string source = Source("src/DeskBox/Services/TodoReminderService.cs");
        // Every write path goes through MutateAsync (gate held across
        // load/modify/save); direct SaveAsync usage would reopen DEF-043.
        Assert.Equal(3, Regex.Matches(source, @"store\.MutateAsync").Count);
        Assert.DoesNotMatch(new Regex(@"store\.SaveAsync\("), source);
        Assert.DoesNotMatch(
            new Regex(@"await store\.LoadAsync\(\);\s*(?!.*Mutate)"),
            source.Replace("\r", string.Empty));
        // Widgets are told about persisted changes so they can merge them.
        Assert.Matches(
            new Regex(@"event Action<string,\s*TodoItem\?,\s*TodoItem\?>\? TodoStoreChanged"),
            source);
        Assert.Equal(3, Regex.Matches(source, @"PublishStoreChanged\(widget\.Id").Count);
    }

    [Fact]
    public void App_RelaySubscriptionIsSymmetric()
    {
        string source = Source("src/DeskBox/App.xaml.cs");
        Assert.Matches(new Regex(@"TodoStoreChanged \+= RelayTodoStoreChangedByReminder"), source);
        Assert.Matches(new Regex(@"TodoStoreChanged -= RelayTodoStoreChangedByReminder"), source);
    }

    [Fact]
    public void EverythingQuery_HasTimeoutAndSingleReleaseOwner()
    {
        string source = Source("src/DeskBox/Services/EverythingSearchService.cs");
        // The query races a timeout budget.
        Assert.Matches(new Regex(@"Task\.WhenAny\(\s*inFlightQuery,\s*Task\.Delay\(NativeQueryTimeout"), source);
        // The in-flight delegate owns the release; the caller only releases
        // when the delegate never started.
        Assert.Matches(
            new Regex(@"finally\s*\{[^}]*_nativeGate\.Release\(\);[^}]*\}\s*},\s*CancellationToken\.None\)"),
            source.SingleLine());
        Assert.Matches(new Regex(@"if \(inFlightQuery is null\)\s*\{\s*_nativeGate\.Release\(\);"), source);
        // The disabled fast path is TTL-gated (DEF-045).
        Assert.Matches(new Regex(@"DisabledPathTtlMilliseconds"), source);
        Assert.Matches(new Regex(@"_lastDisabledFastPathTick"), source);
    }

    [Fact]
    public void WindDirection_GoesThroughDefensiveMapper()
    {
        string vm = Source("src/DeskBox/ViewModels/WeatherWidgetViewModel.DataProcessing.cs");
        Assert.Matches(new Regex(@"WeatherWindDirectionMapper\.ResolveIndex\(direction\)"), vm);
        // The old unchecked indexing must be gone.
        Assert.DoesNotMatch(new Regex(@"Math\.Round\(direction / 45\) % 8"), vm);
    }

    [Fact]
    public void TodoRelaySubscription_LivesOnTheViewNotTheAdapter()
    {
        // The relay must live on TodoWidgetContent's Loaded/Unloaded: the
        // adapter is constructed by unit tests without a WinUI Application,
        // so any App.Current touch there performs COM activation and throws
        // REGDB_E_CLASSNOTREG in the test host.
        string adapter = Source("src/DeskBox/Controls/WidgetContents/TodoWidgetContentAdapter.cs");
        Assert.DoesNotContain("TodoStoreChangedByReminder", adapter, StringComparison.Ordinal);

        string view = Source("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs");
        Assert.Equal(
            1,
            Regex.Matches(view, @"TodoStoreChangedByReminder \+= OnTodoStoreChangedByReminder").Count);
        // The Unloaded handler unsubscribes, and the Loaded handler uses the
        // unsubscribe-then-subscribe idiom shared by the other relayed events
        // there, so two minus-subscriptions are expected in total.
        Assert.Equal(
            2,
            Regex.Matches(view, @"TodoStoreChangedByReminder -= OnTodoStoreChangedByReminder").Count);
        // Subscription happens inside the Loaded handler.
        int loadedIndex = view.IndexOf("private void TodoWidgetContent_Loaded", StringComparison.Ordinal);
        int unloadedIndex = view.IndexOf("private void TodoWidgetContent_Unloaded", StringComparison.Ordinal);
        string loadedBody = view[loadedIndex..unloadedIndex];
        Assert.Contains("TodoStoreChangedByReminder += OnTodoStoreChangedByReminder", loadedBody, StringComparison.Ordinal);
        // The merge handler bails when no view model is attached.
        Assert.Matches(new Regex(@"OnTodoStoreChangedByReminder[\s\S]*?if \(ViewModel is null\)"), view);
    }
}

file static class StringExtensions
{
    public static string SingleLine(this string value) => value.Replace("\r", string.Empty).Replace("\n", string.Empty);
}
