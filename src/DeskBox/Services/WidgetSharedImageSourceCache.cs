using Microsoft.UI.Xaml.Media;

namespace DeskBox.Services;

/// <summary>
/// One decoded source per shared asset file (the global unified
/// background and the panorama) for every widget window in the process —
/// the explicit replacement for the cross-window dedupe the framework's
/// URI-keyed cache used to provide before these sources moved to
/// streams. Entries revalidate against the file's length + last-write
/// stamp, so a re-pick that rewrites the file produces a fresh decode
/// instead of a stale shared surface. The settle callbacks replace
/// per-window event subscriptions on the shared source (no window keeps
/// the cached instance's events pinned): a decode that fails mid-flight
/// is pinned by the file stamp, every registered failure callback runs
/// once so each window degrades to its fallback layer, and re-applies
/// keep degrading until the file content changes; a successful decode
/// notifies the registered success callbacks (the panorama positions
/// its slice then). UI thread only — windows resolve during apply on
/// the dispatcher, and the stream settle callbacks arrive back on the
/// same thread.
/// </summary>
internal static class WidgetSharedImageSourceCache
{
    private sealed class Entry
    {
        public required long Length { get; init; }

        public required DateTime LastWriteUtc { get; init; }

        public required ImageSource Source { get; init; }

        /// <summary>
        /// Monotonic use stamp, so the cap can drop the least recently
        /// resolved background instead of whichever key happens to be first.
        /// </summary>
        public long LastUsed { get; set; }

        public List<Action>? PendingFailureCallbacks { get; set; }

        public List<Action>? PendingSuccessCallbacks { get; set; }
    }

    /// <summary>
    /// A user who keeps picking new background images leaves one entry per
    /// path behind: a re-pick writes a different file, so the length/stamp
    /// revalidation never revisits the old key. Each entry pins a full-width
    /// decoded source (about 3.5 MiB at the 1280 px decode width), so the
    /// table is capped and the oldest settled entries are dropped. Entries
    /// whose fill is still in flight are never evicted - that would strand
    /// the callbacks registered on them.
    /// </summary>
    private const int MaxSettledEntries = 8;

    /// <summary>
    /// How long a failed decode suppresses further attempts for the same
    /// (path, length, stamp). The pin exists to stop a burst of layout passes
    /// from re-decoding the same broken file in one frame sequence; it must not
    /// outlive that burst, because re-picking the same unchanged image is a
    /// deliberate user retry (the view layer clears its own failed-path pin for
    /// exactly that reason) and a permanent pin would degrade the widget for the
    /// rest of the session with no way back short of a restart.
    /// </summary>
    private static readonly TimeSpan FailurePinLifetime = TimeSpan.FromSeconds(5);

    private static readonly Dictionary<string, Entry> s_entries = [];

    private static long s_useCounter;

    private static readonly Dictionary<string, (long Length, DateTime LastWriteUtc, DateTime RecordedUtc)> s_failedStamps =
        [];

    public static ImageSource? TryGet(
        string? filePath,
        Func<string, ImageSource?> sourceFactory,
        Action? loadFailed = null,
        Action? loadSucceeded = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            if (!File.Exists(filePath))
            {
                return null;
            }

            var info = new FileInfo(filePath);
            (long length, DateTime stamp) = (info.Length, info.LastWriteTimeUtc);

            if (s_failedStamps.TryGetValue(
                    filePath,
                    out (long Length, DateTime LastWriteUtc, DateTime RecordedUtc) failed) &&
                failed.Length == length &&
                failed.LastWriteUtc == stamp &&
                DateTime.UtcNow - failed.RecordedUtc < FailurePinLifetime)
            {
                // Pinned failure: suppress the retry burst for this exact content.
                return null;
            }

            s_failedStamps.Remove(filePath);

            if (s_entries.TryGetValue(filePath, out Entry? entry) &&
                entry.Length == length &&
                entry.LastWriteUtc == stamp)
            {
                entry.LastUsed = ++s_useCounter;
                if (loadFailed is not null)
                {
                    // Pending loads take more callbacks; settled entries
                    // cannot fail anymore (null list skips).
                    entry.PendingFailureCallbacks?.Add(loadFailed);
                }

                if (loadSucceeded is not null)
                {
                    entry.PendingSuccessCallbacks?.Add(loadSucceeded);
                }

                return entry.Source;
            }

            // Content changed (or first resolve): the stale decode drops
            // with the old entry and this resolves decode fresh.
            s_entries.Remove(filePath);
            entry = new Entry
            {
                Length = length,
                LastWriteUtc = stamp,
                Source = sourceFactory(filePath) ??
                    throw new InvalidOperationException(
                        $"The source factory refused '{filePath}'."),
                LastUsed = ++s_useCounter,
                PendingFailureCallbacks = loadFailed is null ? [] : [loadFailed],
                PendingSuccessCallbacks = loadSucceeded is null ? [] : [loadSucceeded],
            };
            s_entries[filePath] = entry;
            EvictSettledBeyondCap();
            WidgetImageStreamLoader.Fill(
                entry.Source,
                filePath,
                settled => Settle(filePath, entry, settled));
            return entry.Source;
        }
        catch (Exception ex)
        {
            App.Log($"[WidgetSharedImageSourceCache] Resolve failed for '{filePath}': {ex.Message}");
            return null;
        }
    }

    private static void EvictSettledBeyondCap()
    {
        if (s_entries.Count <= MaxSettledEntries)
        {
            return;
        }

        var evictable = new List<KeyValuePair<string, Entry>>(s_entries.Count);
        foreach (KeyValuePair<string, Entry> pair in s_entries)
        {
            if (pair.Value.PendingFailureCallbacks is null &&
                pair.Value.PendingSuccessCallbacks is null)
            {
                evictable.Add(pair);
            }
        }

        evictable.Sort((left, right) => left.Value.LastUsed.CompareTo(right.Value.LastUsed));
        int excess = s_entries.Count - MaxSettledEntries;
        for (int index = 0; index < evictable.Count && excess > 0; index++, excess--)
        {
            s_entries.Remove(evictable[index].Key);
        }
    }

    /// <summary>Test seam: the cap is the only thing the assertions need.</summary>
    internal static int CountForTests => s_entries.Count;

    /// <summary>Test seam: lets a test start from a known-empty table.</summary>
    internal static void ResetForTests()
    {
        s_entries.Clear();
        s_failedStamps.Clear();
    }

    private static void Settle(string filePath, Entry entry, bool succeeded)
    {
        List<Action>? callbacks = succeeded
            ? entry.PendingSuccessCallbacks
            : entry.PendingFailureCallbacks;
        if (callbacks is null)
        {
            return;
        }

        // Detach before notifying: a callback can resolve another image or
        // re-enter settlement, but this completion owns its subscribers once.
        entry.PendingFailureCallbacks = null;
        entry.PendingSuccessCallbacks = null;
        if (!succeeded &&
            s_entries.TryGetValue(filePath, out Entry? current) &&
            ReferenceEquals(current, entry))
        {
            // A newer decode (a re-pick racing this load) must not be
            // evicted by an older fill's failure.
            s_entries.Remove(filePath);
            s_failedStamps[filePath] = (entry.Length, entry.LastWriteUtc, DateTime.UtcNow);
        }

        try
        {
            foreach (Action callback in callbacks)
            {
                try
                {
                    callback();
                }
                catch (Exception ex)
                {
                    App.Log($"[WidgetSharedImageSourceCache] Settle callback threw: {ex.Message}");
                }
            }
        }
        finally
        {
            // A burst can exceed the cap while every source is pending.
            // Recheck the live table after callbacks, which may replace this
            // entry or start more fills; pending replacements stay protected.
            EvictSettledBeyondCap();
        }
    }
}
