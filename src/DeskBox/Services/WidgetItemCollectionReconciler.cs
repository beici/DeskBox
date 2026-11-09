using System.Collections.ObjectModel;
using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Builds one final folder order before publishing collection changes. Retained
/// objects keep their selection and hydrated state; unchanged snapshots publish
/// no collection notifications and never pass through the enumeration order.
/// </summary>
internal static class WidgetItemCollectionReconciler
{
    public static List<WidgetItem> MergeSnapshot(
        IReadOnlyList<WidgetItem> current,
        IReadOnlyList<WidgetItem> refreshed,
        Action<WidgetItem, WidgetItem> updateExisting)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(refreshed);
        ArgumentNullException.ThrowIfNull(updateExisting);

        var remainingByPath = new Dictionary<string, WidgetItem>(
            refreshed.Count,
            StringComparer.OrdinalIgnoreCase);
        foreach (WidgetItem item in refreshed)
        {
            // A repeated path is one logical file. The first snapshot entry
            // wins, matching the manual-order policy and desktop root merge.
            remainingByPath.TryAdd(item.Path, item);
        }

        var merged = new List<WidgetItem>(remainingByPath.Count);
        foreach (WidgetItem item in current)
        {
            if (remainingByPath.Remove(item.Path, out WidgetItem? snapshot))
            {
                updateExisting(item, snapshot);
                merged.Add(item);
            }
        }

        // Existing order is the stable tie-breaker for equal sort keys. New
        // files follow in snapshot order, with duplicate paths consumed once.
        foreach (WidgetItem item in refreshed)
        {
            if (remainingByPath.Remove(item.Path, out WidgetItem? snapshot))
            {
                merged.Add(snapshot);
            }
        }

        return merged;
    }

    public static List<WidgetItem> Sort(
        List<WidgetItem> items,
        Comparison<WidgetItem> comparison)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(comparison);

        for (int index = 1; index < items.Count; index++)
        {
            if (comparison(items[index - 1], items[index]) > 0)
            {
                // Enumerable ordering is stable, unlike List.Sort. Keep the
                // current relative order when the configured keys compare equal.
                return items.OrderBy(item => item, Comparer<WidgetItem>.Create(comparison)).ToList();
            }
        }

        return items;
    }

    public static void Reconcile(
        ObservableCollection<WidgetItem> current,
        IReadOnlyList<WidgetItem> target)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(target);

        var retained = new HashSet<WidgetItem>(ReferenceEqualityComparer.Instance);
        foreach (WidgetItem item in target)
        {
            if (!retained.Add(item))
            {
                throw new ArgumentException("The target order contains a repeated item instance.", nameof(target));
            }
        }

        if (current.Count == target.Count)
        {
            int unchanged = 0;
            while (unchanged < current.Count && ReferenceEquals(current[unchanged], target[unchanged]))
            {
                unchanged++;
            }

            if (unchanged == current.Count)
            {
                return;
            }
        }

        var positions = new Dictionary<WidgetItem, int>(current.Count, ReferenceEqualityComparer.Instance);
        for (int index = 0; index < current.Count; index++)
        {
            positions.TryAdd(current[index], index);
        }

        // Remove backwards so a departing suffix costs no array shifts. Keep
        // the first occurrence if a previous snapshot duplicated an instance.
        for (int index = current.Count - 1; index >= 0; index--)
        {
            WidgetItem item = current[index];
            if (!retained.Contains(item) || positions[item] != index)
            {
                current.RemoveAt(index);
            }
        }

        positions.Clear();
        Reindex(positions, current, 0, current.Count - 1);
        HashSet<WidgetItem> stationary = FindLongestOrderedSubsequence(target, positions);
        for (int targetIndex = target.Count - 1; targetIndex >= 0; targetIndex--)
        {
            WidgetItem item = target[targetIndex];
            if (stationary.Contains(item))
            {
                continue;
            }

            // Place the changing items immediately before the already settled
            // successor. Keeping the longest ordered subsequence stationary
            // minimizes Move events: moving the first item to the end costs
            // one event, not a move of every other row towards the front.
            int insertionIndex = targetIndex + 1 < target.Count
                ? positions[target[targetIndex + 1]]
                : current.Count;
            if (positions.TryGetValue(item, out int currentIndex))
            {
                if (currentIndex < insertionIndex)
                {
                    insertionIndex--;
                }

                if (currentIndex != insertionIndex)
                {
                    current.Move(currentIndex, insertionIndex);
                    Reindex(positions, current, Math.Min(currentIndex, insertionIndex), Math.Max(currentIndex, insertionIndex));
                }
            }
            else
            {
                // Insert a contiguous new run forwards. Reversing each insert
                // at the same anchor would shift the growing run on every
                // iteration, making an empty folder's first population O(n²).
                int firstNewIndex = targetIndex;
                while (firstNewIndex > 0 && !positions.ContainsKey(target[firstNewIndex - 1]))
                {
                    firstNewIndex--;
                }

                for (int newIndex = firstNewIndex; newIndex <= targetIndex; newIndex++)
                {
                    current.Insert(insertionIndex + newIndex - firstNewIndex, target[newIndex]);
                }

                Reindex(positions, current, insertionIndex, current.Count - 1);
                targetIndex = firstNewIndex;
            }
        }
    }

    private static HashSet<WidgetItem> FindLongestOrderedSubsequence(
        IReadOnlyList<WidgetItem> target,
        IReadOnlyDictionary<WidgetItem, int> positions)
    {
        var predecessors = new int[target.Count];
        var tails = new int[target.Count];
        int length = 0;
        for (int index = 0; index < target.Count; index++)
        {
            if (!positions.TryGetValue(target[index], out int position))
            {
                continue;
            }

            int low = 0;
            int high = length;
            while (low < high)
            {
                int middle = low + ((high - low) / 2);
                if (positions[target[tails[middle]]] < position)
                {
                    low = middle + 1;
                }
                else
                {
                    high = middle;
                }
            }

            predecessors[index] = low > 0 ? tails[low - 1] : -1;
            tails[low] = index;
            if (low == length)
            {
                length++;
            }
        }

        var stationary = new HashSet<WidgetItem>(ReferenceEqualityComparer.Instance);
        for (int index = length > 0 ? tails[length - 1] : -1; index >= 0; index = predecessors[index])
        {
            stationary.Add(target[index]);
        }

        return stationary;
    }

    private static void Reindex(
        Dictionary<WidgetItem, int> positions,
        IList<WidgetItem> items,
        int first,
        int last)
    {
        for (int index = first; index <= last; index++)
        {
            positions[items[index]] = index;
        }
    }
}
