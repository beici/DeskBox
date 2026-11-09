using System.Collections;
using System.Reflection;
using DeskBox.Services;

namespace DeskBox.Tests;

public sealed class WidgetSharedImageSourceCacheTests : IDisposable
{
    private const BindingFlags PrivateStatic = BindingFlags.NonPublic | BindingFlags.Static;
    private static readonly Type EntryType = typeof(WidgetSharedImageSourceCache)
        .GetNestedType("Entry", BindingFlags.NonPublic)!;
    private static readonly MethodInfo SettleMethod = typeof(WidgetSharedImageSourceCache)
        .GetMethod("Settle", PrivateStatic)!;
    private static readonly IDictionary Entries = (IDictionary)typeof(WidgetSharedImageSourceCache)
        .GetField("s_entries", PrivateStatic)!.GetValue(null)!;
    private static readonly IDictionary FailedStamps = (IDictionary)typeof(WidgetSharedImageSourceCache)
        .GetField("s_failedStamps", PrivateStatic)!.GetValue(null)!;

    public WidgetSharedImageSourceCacheTests() => WidgetSharedImageSourceCache.ResetForTests();

    public void Dispose() => WidgetSharedImageSourceCache.ResetForTests();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PendingBurst_ReturnsToCapWhenLoadsFinish_WithoutAnotherResolve(bool subscribe)
    {
        const int burstSize = 12;
        int[] successes = new int[burstSize];
        int failures = 0;
        object[] pending = new object[burstSize];
        for (int index = 0; index < burstSize; index++)
        {
            int callbackIndex = index;
            pending[index] = AddPending(
                $"image-{index}",
                index,
                subscribe ? () => successes[callbackIndex]++ : null,
                subscribe ? () => failures++ : null);
        }

        // All inserts in an asynchronous burst can precede its first decode.
        // The insertion-time sweep cannot evict any of these pending entries.
        typeof(WidgetSharedImageSourceCache).GetMethod("EvictSettledBeyondCap", PrivateStatic)!
            .Invoke(null, null);
        Assert.Equal(burstSize, WidgetSharedImageSourceCache.CountForTests);

        for (int index = 0; index < burstSize; index++)
        {
            Settle($"image-{index}", pending[index], succeeded: true);
            for (int inFlight = index + 1; inFlight < burstSize; inFlight++)
            {
                Assert.Same(pending[inFlight], Entries[$"image-{inFlight}"]);
            }
        }

        Assert.Equal(8, WidgetSharedImageSourceCache.CountForTests);
        Assert.Equal(Enumerable.Range(4, 8).Select(index => $"image-{index}"),
            Entries.Keys.Cast<string>().OrderBy(key => int.Parse(key[6..])));
        Assert.All(successes, count => Assert.Equal(subscribe ? 1 : 0, count));
        Assert.Equal(0, failures);
    }

    [Fact]
    public void Settle_CallbackReplacementPreservesPendingEntry_AndNotifiesEverySubscriberOnce()
    {
        AddSettledEntries(8);
        object? replacement = null;
        int callbacks = 0;
        object original = AddPending("shared", 9, () =>
        {
            callbacks++;
            replacement = AddPending("shared", 10);
        });
        GetCallbacks(original, "PendingSuccessCallbacks").Add(() => callbacks++);

        Settle("shared", original, succeeded: true);
        Settle("shared", original, succeeded: false);

        Assert.Equal(2, callbacks);
        Assert.Same(replacement, Entries["shared"]);
        Assert.False(FailedStamps.Contains("shared"));
        Assert.Equal(8, WidgetSharedImageSourceCache.CountForTests);
        Assert.False(Entries.Contains("image-0"));
    }

    [Fact]
    public void Settle_ReentrantCompletionCannotFailAnAlreadySuccessfulEntry()
    {
        int successes = 0;
        int failures = 0;
        object? entry = null;
        entry = AddPending("shared", 1, () =>
        {
            successes++;
            Settle("shared", entry!, succeeded: false);
        }, () => failures++);

        Settle("shared", entry, succeeded: true);

        Assert.Same(entry, Entries["shared"]);
        Assert.False(FailedStamps.Contains("shared"));
        Assert.Equal(1, successes);
        Assert.Equal(0, failures);
    }

    [Fact]
    public void Settle_StaleFailureDoesNotRemoveOrPinNewerDecode()
    {
        int failures = 0;
        object original = AddPending("shared", 1, failure: () => failures++);
        object replacement = AddPending("shared", 2);

        Settle("shared", original, succeeded: false);

        Assert.Same(replacement, Entries["shared"]);
        Assert.False(FailedStamps.Contains("shared"));
        Assert.Equal(1, failures);
    }

    [Fact]
    public void Settle_ThrowingSubscriberDoesNotSkipOtherSubscribersOrEviction()
    {
        AddSettledEntries(8);
        int successes = 0;
        object entry = AddPending("shared", 9, () => throw new InvalidOperationException("Subscriber failed."));
        GetCallbacks(entry, "PendingSuccessCallbacks").Add(() => successes++);

        Settle("shared", entry, succeeded: true);

        Assert.Equal(1, successes);
        Assert.Same(entry, Entries["shared"]);
        Assert.Equal(8, WidgetSharedImageSourceCache.CountForTests);
        Assert.False(Entries.Contains("image-0"));
    }

    private static void AddSettledEntries(int count)
    {
        for (int index = 0; index < count; index++)
        {
            string key = $"image-{index}";
            object entry = AddPending(key, index);
            Settle(key, entry, succeeded: true);
        }
    }

    private static object AddPending(string key, long lastUsed, Action? success = null, Action? failure = null)
    {
        // Settlement only reads entry metadata. Construct it without a WinUI
        // source so these tests execute the real cache logic without a UI host.
        object entry = Activator.CreateInstance(EntryType, nonPublic: true)!;
        EntryType.GetProperty("LastUsed")!.SetValue(entry, lastUsed);
        EntryType.GetProperty("PendingSuccessCallbacks")!.SetValue(
            entry, success is null ? new List<Action>() : new List<Action> { success });
        EntryType.GetProperty("PendingFailureCallbacks")!.SetValue(
            entry, failure is null ? new List<Action>() : new List<Action> { failure });
        Entries[key] = entry;
        return entry;
    }

    private static List<Action> GetCallbacks(object entry, string propertyName) =>
        (List<Action>)EntryType.GetProperty(propertyName)!.GetValue(entry)!;

    private static void Settle(string key, object entry, bool succeeded) =>
        SettleMethod.Invoke(null, [key, entry, succeeded]);
}
