using System.Collections.ObjectModel;
using System.Collections.Specialized;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class WidgetItemCollectionReconcilerTests
{
    [Theory]
    [InlineData(WidgetSortMode.Name, false)]
    [InlineData(WidgetSortMode.Name, true)]
    [InlineData(WidgetSortMode.Size, false)]
    [InlineData(WidgetSortMode.Size, true)]
    [InlineData(WidgetSortMode.Type, false)]
    [InlineData(WidgetSortMode.Type, true)]
    [InlineData(WidgetSortMode.DateModified, false)]
    [InlineData(WidgetSortMode.DateModified, true)]
    public void UnchangedSnapshot_PreservesIdentityWithLinearComparisonsAndNoNotifications(
        WidgetSortMode mode,
        bool descending)
    {
        List<WidgetItem> source = Enumerable.Range(0, 5000).Select(CreateItem).ToList();
        Comparison<WidgetItem> comparison = (left, right) =>
            WidgetViewModel.CompareItems(left, right, mode, descending);
        var current = new ObservableCollection<WidgetItem>(
            WidgetItemCollectionReconciler.Sort(source, comparison));
        WidgetItem[] identities = current.ToArray();
        List<WidgetItem> refreshed = source.OrderBy(item => item.Name, StringComparer.Ordinal)
            .Select(CloneSnapshot).ToList();
        int collectionChanges = 0;
        int propertyChanges = 0;
        int comparisons = 0;
        current.CollectionChanged += (_, _) => collectionChanges++;
        foreach (WidgetItem item in current)
        {
            item.PropertyChanged += (_, _) => propertyChanges++;
        }

        List<WidgetItem> merged = WidgetItemCollectionReconciler.MergeSnapshot(current, refreshed, CopySnapshot);
        List<WidgetItem> target = WidgetItemCollectionReconciler.Sort(merged, (left, right) =>
        {
            comparisons++;
            return comparison(left, right);
        });
        WidgetItemCollectionReconciler.Reconcile(current, target);

        Assert.Equal(4999, comparisons);
        Assert.Equal(0, collectionChanges);
        Assert.Equal(0, propertyChanges);
        AssertIdentityOrder(identities, current);
        Assert.True(current.TakeWhile(item => item.IsFolder).Count() == current.Count(item => item.IsFolder));
    }

    [Fact]
    public void ChangedMetadata_MovesTheChangedFirstItemToTheEndOnce()
    {
        WidgetItem first = CreateItem(1);
        WidgetItem second = CreateItem(2);
        WidgetItem third = CreateItem(3);
        first.FileSize = 1;
        second.FileSize = 2;
        third.FileSize = 3;
        first.IsSelected = true;
        first.IsCut = true;
        first.IsShortcutTargetLoaded = true;
        first.TargetPath = @"C:\target.exe";
        var current = new ObservableCollection<WidgetItem> { first, second, third };
        var events = new List<NotifyCollectionChangedEventArgs>();
        current.CollectionChanged += (_, e) => events.Add(e);
        WidgetItem updated = CloneSnapshot(first);
        updated.FileSize = 4;

        List<WidgetItem> merged = WidgetItemCollectionReconciler.MergeSnapshot(
            current, [updated, CloneSnapshot(second), CloneSnapshot(third)], CopySnapshot);
        List<WidgetItem> target = WidgetItemCollectionReconciler.Sort(merged,
            (left, right) => WidgetViewModel.CompareItems(left, right, WidgetSortMode.Size, false));
        WidgetItemCollectionReconciler.Reconcile(current, target);

        AssertIdentityOrder([second, third, first], current);
        NotifyCollectionChangedEventArgs change = Assert.Single(events);
        Assert.Equal(NotifyCollectionChangedAction.Move, change.Action);
        Assert.Same(first, Assert.Single(change.NewItems!.Cast<WidgetItem>()));
        Assert.True(first.IsSelected);
        Assert.True(first.IsCut);
        Assert.True(first.IsShortcutTargetLoaded);
        Assert.Equal(@"C:\target.exe", first.TargetPath);
    }

    [Fact]
    public void AddAndRemove_PublishesOnlyTheFinalOrderWithoutResettingRetainedRows()
    {
        WidgetItem a = CreateItem(1);
        WidgetItem b = CreateItem(2);
        WidgetItem c = CreateItem(3);
        WidgetItem d = CreateItem(4);
        var current = new ObservableCollection<WidgetItem> { a, b, c };
        var actions = new List<NotifyCollectionChangedAction>();
        current.CollectionChanged += (_, e) => actions.Add(e.Action);

        WidgetItemCollectionReconciler.Reconcile(current, [c, d, a]);

        AssertIdentityOrder([c, d, a], current);
        Assert.Equal(1, actions.Count(action => action == NotifyCollectionChangedAction.Remove));
        Assert.Equal(1, actions.Count(action => action == NotifyCollectionChangedAction.Add));
        Assert.Equal(1, actions.Count(action => action == NotifyCollectionChangedAction.Move));
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void DuplicateSnapshotPaths_UseFirstSnapshotAndFirstExistingInstance()
    {
        WidgetItem firstExisting = CreateItem(1);
        WidgetItem duplicateExisting = CloneSnapshot(firstExisting);
        WidgetItem firstSnapshot = CloneSnapshot(firstExisting);
        firstSnapshot.Path = firstSnapshot.Path.ToUpperInvariant();
        firstSnapshot.Name = "first snapshot";
        WidgetItem duplicateSnapshot = CloneSnapshot(firstSnapshot);
        duplicateSnapshot.Name = "ignored duplicate";
        WidgetItem newFile = CreateItem(2);
        var current = new ObservableCollection<WidgetItem> { firstExisting, duplicateExisting };
        int updates = 0;

        List<WidgetItem> merged = WidgetItemCollectionReconciler.MergeSnapshot(
            current,
            [firstSnapshot, duplicateSnapshot, newFile, CloneSnapshot(newFile)],
            (existing, snapshot) =>
            {
                updates++;
                CopySnapshot(existing, snapshot);
            });
        WidgetItemCollectionReconciler.Reconcile(current, merged);

        Assert.Equal(1, updates);
        AssertIdentityOrder([firstExisting, newFile], current);
        Assert.Equal("first snapshot", firstExisting.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ManualOrder_RetainsLiveOrPersistedOrderAndAppendsNewFiles(bool hasLiveOrder)
    {
        WidgetItem a = CreateItem(1);
        WidgetItem b = CreateItem(2);
        WidgetItem added = CreateItem(3);
        var current = new ObservableCollection<WidgetItem>(hasLiveOrder
            ? new[] { b, a }
            : new[] { CreateItem(100) });
        List<WidgetItem> merged = WidgetItemCollectionReconciler.MergeSnapshot(
            current, [CloneSnapshot(a), CloneSnapshot(b), added], CopySnapshot);
        var persisted = new[]
        {
            new WidgetItemConfig { Path = b.Path, SortOrder = 0 },
            new WidgetItemConfig { Path = a.Path, SortOrder = 1 }
        };
        IReadOnlyList<WidgetItem> target = WidgetManualOrderPolicy.Reconcile(
            merged, current.Select(item => item.Path).ToList(), persisted, item => item.Path);

        WidgetItemCollectionReconciler.Reconcile(current, target);

        Assert.Equal(new[] { b.Path, a.Path, added.Path }, current.Select(item => item.Path));
        if (hasLiveOrder)
        {
            AssertIdentityOrder([b, a, added], current);
        }
    }

    [Fact]
    public void Sort_PreservesRelativeOrderForEqualKeys()
    {
        WidgetItem a = CreateItem(1);
        WidgetItem b = CreateItem(2);
        WidgetItem c = CreateItem(3);
        WidgetItem d = CreateItem(4);
        a.FileSize = d.FileSize = 2;
        b.FileSize = c.FileSize = 1;

        List<WidgetItem> sorted = WidgetItemCollectionReconciler.Sort(
            [a, b, c, d], (left, right) => left.FileSize.CompareTo(right.FileSize));

        AssertIdentityOrder([b, c, a, d], sorted);
    }

    [Fact]
    public void RepeatedCurrentInstance_IsRemovedWithoutDuplicatingTheTarget()
    {
        WidgetItem a = CreateItem(1);
        WidgetItem b = CreateItem(2);
        var current = new ObservableCollection<WidgetItem> { a, a, b };

        WidgetItemCollectionReconciler.Reconcile(current, [b, a]);

        AssertIdentityOrder([b, a], current);
    }

    [Fact]
    public void RepeatedTargetInstance_IsRejectedBeforeAnyMutation()
    {
        WidgetItem a = CreateItem(1);
        WidgetItem b = CreateItem(2);
        var current = new ObservableCollection<WidgetItem> { a, b };
        int notifications = 0;
        current.CollectionChanged += (_, _) => notifications++;

        Assert.Throws<ArgumentException>(() => WidgetItemCollectionReconciler.Reconcile(current, [b, b]));

        AssertIdentityOrder([a, b], current);
        Assert.Equal(0, notifications);
    }

    [Fact]
    public void DistinctInstancesWithValueEquality_AreStillReconciledByReference()
    {
        var a = new ValueEqualWidgetItem { Path = @"C:\a" };
        var b = new ValueEqualWidgetItem { Path = @"C:\b" };
        var c = new ValueEqualWidgetItem { Path = @"C:\c" };
        var current = new ObservableCollection<WidgetItem> { a, b };

        WidgetItemCollectionReconciler.Reconcile(current, [b, c, a]);

        AssertIdentityOrder([b, c, a], current);
    }

    [Fact]
    public void AllSmallPermutations_UseTheMinimumMoveCount()
    {
        WidgetItem[] items = Enumerable.Range(0, 6).Select(CreateItem).ToArray();
        foreach (int[] permutation in Permutations(Enumerable.Range(0, items.Length).ToArray(), 0))
        {
            var current = new ObservableCollection<WidgetItem>(items);
            WidgetItem[] target = permutation.Select(index => items[index]).ToArray();
            int moves = 0;
            current.CollectionChanged += (_, e) =>
            {
                Assert.Equal(NotifyCollectionChangedAction.Move, e.Action);
                moves++;
            };

            WidgetItemCollectionReconciler.Reconcile(current, target);

            AssertIdentityOrder(target, current);
            Assert.Equal(items.Length - LongestIncreasingLength(permutation), moves);
        }
    }

    [Fact]
    public void MixedRandomChanges_PreserveEveryRetainedIdentityAndUseMinimumMoves()
    {
        var random = new Random(61006);
        WidgetItem[] pool = Enumerable.Range(0, 40).Select(CreateItem).ToArray();
        for (int iteration = 0; iteration < 100; iteration++)
        {
            WidgetItem[] initial = pool.Take(25).ToArray();
            var current = new ObservableCollection<WidgetItem>(initial);
            WidgetItem[] target = pool.Where(_ => random.Next(3) != 0)
                .OrderBy(_ => random.Next()).ToArray();
            int[] survivingOrder = target.Where(item => Array.IndexOf(initial, item) >= 0)
                .Select(item => Array.IndexOf(initial, item)).ToArray();
            int moves = 0;
            current.CollectionChanged += (_, e) =>
            {
                Assert.NotEqual(NotifyCollectionChangedAction.Reset, e.Action);
                if (e.Action == NotifyCollectionChangedAction.Move)
                {
                    moves++;
                }
            };

            WidgetItemCollectionReconciler.Reconcile(current, target);

            AssertIdentityOrder(target, current);
            Assert.Equal(survivingOrder.Length - LongestIncreasingLength(survivingOrder), moves);
        }
    }

    [Fact]
    public void EmptyOrders_HandleInitialPopulationAndFullRemoval()
    {
        WidgetItem a = CreateItem(1);
        WidgetItem b = CreateItem(2);
        var current = new ObservableCollection<WidgetItem>();
        var actions = new List<NotifyCollectionChangedAction>();
        current.CollectionChanged += (_, e) => actions.Add(e.Action);

        WidgetItemCollectionReconciler.Reconcile(current, [a, b]);
        AssertIdentityOrder([a, b], current);
        WidgetItemCollectionReconciler.Reconcile(current, []);
        Assert.Empty(current);
        WidgetItemCollectionReconciler.Reconcile(current, []);

        Assert.Equal(4, actions.Count);
        Assert.DoesNotContain(NotifyCollectionChangedAction.Reset, actions);
    }

    [Fact]
    public void InitialPopulationAndNewSuffix_AppendInOrderWithoutShiftingEarlierInserts()
    {
        WidgetItem[] items = Enumerable.Range(0, 5000).Select(CreateItem).ToArray();
        var current = new ObservableCollection<WidgetItem>();
        int nextIndex = 0;
        current.CollectionChanged += (_, e) =>
        {
            Assert.Equal(NotifyCollectionChangedAction.Add, e.Action);
            Assert.Equal(nextIndex++, e.NewStartingIndex);
        };

        WidgetItemCollectionReconciler.Reconcile(current, items.Take(100).ToArray());
        WidgetItemCollectionReconciler.Reconcile(current, items);

        AssertIdentityOrder(items, current);
        Assert.Equal(items.Length, nextIndex);
    }

    private static WidgetItem CreateItem(int index) => new()
    {
        Name = $"file {index}",
        Path = $@"C:\folder\file {index}.{(index % 3 == 0 ? "txt" : "png")}",
        FileSize = 5000 - index,
        LastModified = new DateTime(2026, 11, 6).AddSeconds(index % 7),
        IsFolder = index % 17 == 0
    };

    private static WidgetItem CloneSnapshot(WidgetItem item)
    {
        var clone = new WidgetItem();
        CopySnapshot(clone, item);
        return clone;
    }

    private static void CopySnapshot(WidgetItem target, WidgetItem source)
    {
        target.Name = source.Name;
        target.Path = source.Path;
        target.FileSize = source.FileSize;
        target.LastModified = source.LastModified;
        target.IsFolder = source.IsFolder;
    }

    private static void AssertIdentityOrder(IReadOnlyList<WidgetItem> expected, IReadOnlyList<WidgetItem> actual)
    {
        Assert.Equal(expected.Count, actual.Count);
        for (int index = 0; index < expected.Count; index++)
        {
            Assert.Same(expected[index], actual[index]);
        }
    }

    // Independent O(n²) oracle keeps the production binary-search algorithm
    // honest on every six-item permutation and mixed add/remove scenarios.
    private static int LongestIncreasingLength(IReadOnlyList<int> values)
    {
        var lengths = new int[values.Count];
        int longest = 0;
        for (int index = 0; index < values.Count; index++)
        {
            lengths[index] = 1;
            for (int previous = 0; previous < index; previous++)
            {
                if (values[previous] < values[index])
                {
                    lengths[index] = Math.Max(lengths[index], lengths[previous] + 1);
                }
            }

            longest = Math.Max(longest, lengths[index]);
        }

        return longest;
    }

    private static IEnumerable<int[]> Permutations(int[] values, int start)
    {
        if (start == values.Length)
        {
            yield return (int[])values.Clone();
            yield break;
        }

        for (int index = start; index < values.Length; index++)
        {
            (values[start], values[index]) = (values[index], values[start]);
            foreach (int[] permutation in Permutations(values, start + 1))
            {
                yield return permutation;
            }
            (values[start], values[index]) = (values[index], values[start]);
        }
    }

    private sealed class ValueEqualWidgetItem : WidgetItem
    {
        public override bool Equals(object? obj) => obj is ValueEqualWidgetItem;
        public override int GetHashCode() => 1;
    }
}
