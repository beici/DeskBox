using System.Collections.ObjectModel;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;

namespace DeskBox.Tests;

public sealed class WidgetStackProjectionInvalidationPolicyTests
{
    private static readonly DateTime Now = new(2026, 11, 6, 12, 0, 0);

    [Theory]
    [InlineData(SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByWidget)]
    [InlineData(SettingsService.FileStackGroupByCustom, SettingsService.FileStackOrderByName)]
    [InlineData(SettingsService.FileStackGroupByDateModified, SettingsService.FileStackOrderByDateModified)]
    [InlineData(SettingsService.FileStackGroupByDateAdded, SettingsService.FileStackOrderByDateAdded)]
    public void IdenticalSnapshot_DoesNotRequireProjectionInspectionOrInvalidation(string groupBy, string orderBy)
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();

        Assert.False(WidgetStackProjectionInvalidationPolicy.MayAffectProjection(current, snapshot, groupBy, orderBy));
        Assert.False(ApplySnapshot(current, snapshot, groupBy, orderBy));
    }

    [Fact]
    public void ModifiedDateChangesCategory_RequestsProjectionWithoutAnyRawCollectionMove()
    {
        WidgetItem current = CreateItem();
        current.LastModified = Now.AddDays(-1);
        WidgetItem snapshot = CreateItem();
        var items = new ObservableCollection<WidgetItem> { current };
        int collectionChanges = 0;
        items.CollectionChanged += (_, _) => collectionChanges++;
        bool projectionChanged = false;

        List<WidgetItem> merged = WidgetItemCollectionReconciler.MergeSnapshot(items, [snapshot], (existing, refreshed) =>
        {
            projectionChanged |= ApplySnapshot(existing, refreshed,
                SettingsService.FileStackGroupByDateModified, SettingsService.FileStackOrderByWidget);
        });
        WidgetItemCollectionReconciler.Reconcile(items, merged);

        Assert.True(projectionChanged);
        Assert.Equal(0, collectionChanges);
        Assert.Same(current, Assert.Single(items));
        Assert.Equal(WidgetStackCategory.Today,
            Assert.Single(WidgetStackGroupingService.Group(items, SettingsService.FileStackGroupByDateModified, Now)).Category);
    }

    [Fact]
    public void ModificationWithinTheSameDateBucket_DoesNotRebuildWithoutDateOrdering()
    {
        WidgetItem current = CreateItem();
        current.LastModified = Now.AddDays(-3);
        WidgetItem snapshot = CreateItem();
        snapshot.LastModified = Now.AddDays(-4);

        Assert.True(WidgetStackProjectionInvalidationPolicy.MayAffectProjection(current, snapshot,
            SettingsService.FileStackGroupByDateModified, SettingsService.FileStackOrderByWidget));
        Assert.False(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByDateModified, SettingsService.FileStackOrderByWidget));
    }

    [Fact]
    public void DateOrderingChangesWithinTheSameDay_StillInvalidatesMemberOrder()
    {
        WidgetItem current = CreateItem();
        current.LastModified = Now.AddHours(-2);
        WidgetItem snapshot = CreateItem();

        Assert.True(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByDateModified));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DirectoryOrShortcutClassificationChange_InvalidatesKindGrouping(bool isDirectory)
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.IsFolder = isDirectory;
        snapshot.IsShortcut = !isDirectory;

        Assert.True(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByWidget));
    }

    [Fact]
    public void NewLoadedShellKind_InvalidatesClassification()
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.ShellKind = "picture";
        snapshot.IsShellKindLoaded = true;

        Assert.True(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByWidget));
        Assert.Equal(WidgetStackCategory.Images,
            WidgetStackGroupingService.ResolveCategory(current, SettingsService.FileStackGroupByKind, Now));
    }

    [Fact]
    public void UnloadedSnapshotShellKind_DoesNotInvalidateThePreservedHydratedClassification()
    {
        WidgetItem current = CreateItem();
        current.ShellKind = "picture";
        current.IsShellKindLoaded = true;
        WidgetItem snapshot = CreateItem();

        Assert.False(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByWidget));
        Assert.Equal("picture", current.ShellKind);
    }

    [Fact]
    public void EquivalentLoadedShellClassification_DoesNotRebuild()
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.ShellKind = "document";
        snapshot.IsShellKindLoaded = true;

        Assert.False(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByWidget));
    }

    [Theory]
    [InlineData("Other name", true)]
    [InlineData("FILE", false)]
    public void NameOrdering_UsesTheSameCaseInsensitiveComparerAsTheProjection(string name, bool expected)
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.Name = name;

        Assert.Equal(expected, ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByName));
    }

    [Fact]
    public void UnrelatedMetadata_DoesNotInvalidateProjection()
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.FileSize = 4096;
        snapshot.CreatedAt = Now.AddDays(-9);
        snapshot.FolderItemCount = 100;
        snapshot.IsSelected = true;
        snapshot.IsCut = true;

        Assert.False(WidgetStackProjectionInvalidationPolicy.MayAffectProjection(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByName));
        Assert.False(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByKind, SettingsService.FileStackOrderByName));
    }

    [Fact]
    public void RetainedAddedAt_IsComparedAfterTheActualRuntimeMerge()
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.AddedAt = current.AddedAt.AddDays(-10);

        Assert.True(WidgetStackProjectionInvalidationPolicy.MayAffectProjection(current, snapshot,
            SettingsService.FileStackGroupByDateAdded, SettingsService.FileStackOrderByDateAdded));
        Assert.False(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByDateAdded, SettingsService.FileStackOrderByDateAdded));
    }

    [Fact]
    public void CustomGrouping_IgnoresPathCasingAndKindMetadata()
    {
        WidgetItem current = CreateItem();
        WidgetItem snapshot = CreateItem();
        snapshot.Path = snapshot.Path.ToUpperInvariant();
        snapshot.IsFolder = true;
        snapshot.ShellKind = "picture";
        snapshot.IsShellKindLoaded = true;

        Assert.False(ApplySnapshot(current, snapshot,
            SettingsService.FileStackGroupByCustom, SettingsService.FileStackOrderByWidget));
    }

    private static bool ApplySnapshot(WidgetItem current, WidgetItem snapshot, string groupBy, string orderBy)
    {
        bool inspect = WidgetStackProjectionInvalidationPolicy.MayAffectProjection(current, snapshot, groupBy, orderBy);
        WidgetStackProjectionInvalidationPolicy.State before = inspect
            ? WidgetStackProjectionInvalidationPolicy.Capture(current, groupBy, orderBy, Now)
            : default;

        // Exercise the production merge: unloaded shell kinds and the existing
        // AddedAt value are retained, so comparing raw snapshots alone is wrong.
        WidgetViewModel.ApplyRuntimeItemData(current, snapshot, preserveExistingIconWhenMissing: true);

        return inspect && WidgetStackProjectionInvalidationPolicy.HasChanged(before,
            WidgetStackProjectionInvalidationPolicy.Capture(current, groupBy, orderBy, Now));
    }

    private static WidgetItem CreateItem() => new()
    {
        Name = "File",
        Path = @"C:\folder\file.txt",
        LastModified = Now,
        AddedAt = new DateTimeOffset(Now),
        CreatedAt = Now.AddDays(-1),
        FileSize = 1024,
        IsFolderItemCountLoaded = false
    };
}
