using DeskBox.Controls.WidgetContents;
using DeskBox.Models;
using DeskBox.Services;
using DeskBox.ViewModels;
using Microsoft.UI.Xaml;

namespace DeskBox.Tests;

public sealed class WidgetStackItemNotificationTests
{
    [Fact]
    public void EquivalentMembersAndPresentation_PublishNoNotifications()
    {
        WidgetItem[] members = CreateMembers(5);
        WidgetStackItem stack = CreateStack(members);
        IReadOnlyList<WidgetItem> publishedMembers = stack.Members;
        List<string?> changes = Observe(stack);

        Update(stack, members.ToList());

        Assert.Empty(changes);
        Assert.Same(publishedMembers, stack.Members);
    }

    [Fact]
    public void ReorderingVisiblePreviews_NotifiesOnlyChangedSlots()
    {
        WidgetItem[] members = CreateMembers(5);
        WidgetStackItem stack = CreateStack(members);
        List<string?> changes = Observe(stack);

        (members[0], members[1]) = (members[1], members[0]);
        Update(stack, members);

        AssertChanges(changes, nameof(WidgetStackItem.Members),
            nameof(WidgetStackItem.PreviewOne), nameof(WidgetStackItem.PreviewTwo));
        Assert.Same(members[0], stack.PreviewOne);
        Assert.Same(members[1], stack.PreviewTwo);
    }

    [Fact]
    public void ReplacingMemberBeyondPreview_DoesNotInvalidatePreviewOrCount()
    {
        WidgetItem[] members = CreateMembers(5);
        WidgetStackItem stack = CreateStack(members);
        List<string?> changes = Observe(stack);

        members[4] = new WidgetItem { Name = "Replacement", Path = "C:\\Replacement.txt" };
        Update(stack, members);

        AssertChanges(changes, nameof(WidgetStackItem.Members));
        Assert.Same(members[4], stack.Members[4]);
    }

    [Fact]
    public void MutableCallerSequence_DoesNotSilentlyChangePublishedMembers()
    {
        WidgetItem[] members = CreateMembers(2);
        WidgetItem previousFirst = members[0];
        WidgetStackItem stack = CreateStack(members);
        List<string?> changes = Observe(stack);

        members[0] = new WidgetItem { Name = "Replacement", Path = "C:\\Replacement.txt" };

        Assert.Same(previousFirst, stack.PreviewOne);
        Assert.Empty(changes);
        Update(stack, members);
        AssertChanges(changes, nameof(WidgetStackItem.Members), nameof(WidgetStackItem.PreviewOne));
    }

    [Theory]
    [InlineData(2, 3, nameof(WidgetStackItem.PreviewThree), nameof(WidgetStackItem.ThirdPreviewVisibility))]
    [InlineData(3, 2, nameof(WidgetStackItem.PreviewThree), nameof(WidgetStackItem.ThirdPreviewVisibility))]
    [InlineData(3, 4, nameof(WidgetStackItem.PreviewFour), nameof(WidgetStackItem.FourthPreviewVisibility))]
    [InlineData(4, 3, nameof(WidgetStackItem.PreviewFour), nameof(WidgetStackItem.FourthPreviewVisibility))]
    public void CrossingPreviewThreshold_NotifiesOnlyAffectedSlotAndVisibility(
        int previousCount, int currentCount, string preview, string visibility)
    {
        WidgetItem[] members = CreateMembers(Math.Max(previousCount, currentCount));
        WidgetStackItem stack = CreateStack(members[..previousCount]);
        List<string?> changes = Observe(stack);

        Update(stack, members[..currentCount]);

        AssertChanges(changes, nameof(WidgetStackItem.Members), nameof(WidgetStackItem.CountText),
            nameof(WidgetStackItem.Summary), preview, visibility);
    }

    [Fact]
    public void OneToTwoMembers_UpdatesFallbackSecondPreview()
    {
        WidgetItem[] members = CreateMembers(2);
        WidgetStackItem stack = CreateStack(members[..1]);
        Assert.Same(members[0], stack.PreviewTwo);
        List<string?> changes = Observe(stack);

        Update(stack, members);

        AssertChanges(changes, nameof(WidgetStackItem.Members), nameof(WidgetStackItem.CountText),
            nameof(WidgetStackItem.Summary), nameof(WidgetStackItem.PreviewTwo));
        Assert.Same(members[1], stack.PreviewTwo);
    }

    [Fact]
    public void EmptyMembers_ClearPreviewBindings()
    {
        WidgetStackItem stack = CreateStack(CreateMembers(2));
        List<string?> changes = Observe(stack);

        Update(stack, []);

        AssertChanges(changes, nameof(WidgetStackItem.Members), nameof(WidgetStackItem.CountText),
            nameof(WidgetStackItem.Summary), nameof(WidgetStackItem.PreviewOne), nameof(WidgetStackItem.PreviewTwo));
        Assert.Null(stack.PreviewOne);
        Assert.Null(stack.PreviewTwo);
    }

    [Fact]
    public void ExpansionDelta_DoesNotRepublishMembersOrLayout()
    {
        WidgetStackItem stack = CreateStack(CreateMembers(4));
        List<string?> changes = Observe(stack);

        stack.SetExpanded(true);

        AssertChanges(changes, nameof(WidgetStackItem.IsExpanded), nameof(WidgetStackItem.ChevronGlyph),
            nameof(WidgetStackItem.CollapsedPreviewVisibility), nameof(WidgetStackItem.ExpandedAnchorVisibility));
        changes.Clear();
        stack.SetExpanded(true);
        Assert.Empty(changes);
    }

    [Fact]
    public void ExpansionDelta_UpdatesLocalizedAutomationState()
    {
        WidgetStackItem stack = CreateStack(CreateMembers(4));
        List<string?> changes = Observe(stack);
        stack.PropertyChanged += (_, _) => Assert.True(stack.IsExpanded);

        stack.SetExpanded(true, "Expanded");

        Assert.Equal("Expanded", stack.AutomationState);
        AssertChanges(changes, nameof(WidgetStackItem.AutomationState), nameof(WidgetStackItem.IsExpanded),
            nameof(WidgetStackItem.ChevronGlyph), nameof(WidgetStackItem.CollapsedPreviewVisibility),
            nameof(WidgetStackItem.ExpandedAnchorVisibility));
        changes.Clear();
        stack.SetExpanded(true, "Expanded");
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData(nameof(WidgetStackItem.Summary))]
    [InlineData(nameof(WidgetStackItem.AutomationState))]
    [InlineData(nameof(WidgetStackItem.CollapseText))]
    public void LocalizedTextDelta_NotifiesOnlyItsOwnProperty(string propertyName)
    {
        WidgetItem[] members = CreateMembers(3);
        WidgetStackItem stack = CreateStack(members);
        List<string?> changes = Observe(stack);

        Update(stack, members,
            summary: propertyName == nameof(WidgetStackItem.Summary) ? "3 Dateien" : null,
            automationState: propertyName == nameof(WidgetStackItem.AutomationState) ? "Reduziert" : "Collapsed",
            collapseText: propertyName == nameof(WidgetStackItem.CollapseText) ? "Reduzieren" : "Collapse");

        AssertChanges(changes, propertyName);
    }

    [Theory]
    [InlineData(nameof(WidgetStackItem.TileWidth))]
    [InlineData(nameof(WidgetStackItem.TileHeight))]
    [InlineData(nameof(WidgetStackItem.TileMargin))]
    [InlineData(nameof(WidgetStackItem.TilePadding))]
    [InlineData(nameof(WidgetStackItem.PreviewSize))]
    [InlineData(nameof(WidgetStackItem.PreviewItemSize))]
    [InlineData(nameof(WidgetStackItem.LabelMaxWidth))]
    [InlineData(nameof(WidgetStackItem.LabelFontSize))]
    [InlineData(nameof(WidgetStackItem.ListMargin))]
    [InlineData(nameof(WidgetStackItem.ListPadding))]
    [InlineData(nameof(WidgetStackItem.ListIconSize))]
    public void LayoutDelta_NotifiesOnlyChangedMetric(string propertyName)
    {
        WidgetStackItem stack = CreateStack(CreateMembers(4));
        List<string?> changes = Observe(stack);

        UpdateMetrics(stack, propertyName);

        AssertChanges(changes, propertyName);
        changes.Clear();
        UpdateMetrics(stack, propertyName);
        Assert.Empty(changes);
    }

    [Theory]
    [InlineData(nameof(WidgetStackItem.PreviewSize), false)]
    [InlineData(nameof(WidgetStackItem.PreviewItemSize), false)]
    [InlineData(nameof(WidgetStackItem.ListIconSize), true)]
    public void PreviewGeometryDelta_RefreshesFolderLayoutWithoutRepublishingMembers(
        string propertyName, bool listMode)
    {
        WidgetStackItem stack = CreateStack(CreateMembers(4));
        int previewRefreshes = 0;
        StackFolderPreviewMetrics? refreshed = null;
        StackFolderPreviewMetrics previous = CalculatePreviewMetrics(stack, listMode);
        stack.PropertyChanged += (_, args) =>
        {
            Assert.NotEqual(nameof(WidgetStackItem.Members), args.PropertyName);
            if (FileSurfaceContent.RequiresStackFolderPreviewRefresh(args.PropertyName))
            {
                previewRefreshes++;
                refreshed = CalculatePreviewMetrics(stack, listMode);
            }
        };

        UpdateMetrics(stack, propertyName);

        Assert.Equal(1, previewRefreshes);
        Assert.Equal(CalculatePreviewMetrics(stack, listMode), refreshed);
        Assert.NotEqual(previous, refreshed);
        UpdateMetrics(stack, propertyName);
        Assert.Equal(1, previewRefreshes);
    }

    [Fact]
    public void ForcedPresentationRefresh_RemainsAvailableForViewReuse()
    {
        WidgetStackItem stack = CreateStack(CreateMembers(4));
        List<string?> changes = Observe(stack);

        stack.RefreshPresentationState();

        Assert.Contains(nameof(WidgetStackItem.Members), changes);
        Assert.Contains(nameof(WidgetStackItem.PreviewFour), changes);
        Assert.Contains(nameof(WidgetStackItem.AutomationState), changes);
        Assert.Equal(15, changes.Count);
        Assert.Equal(changes.Count, changes.Distinct().Count());
    }

    private static WidgetStackItem CreateStack(IReadOnlyList<WidgetItem> members)
    {
        var stack = new WidgetStackItem
        {
            Category = WidgetStackCategory.Documents,
            StackKey = "Kind:Documents"
        };
        Update(stack, members);
        return stack;
    }

    private static WidgetItem[] CreateMembers(int count) => Enumerable.Range(0, count)
        .Select(index => new WidgetItem { Name = $"Item {index}", Path = $"C:\\Item{index}.txt" })
        .ToArray();

    private static List<string?> Observe(WidgetStackItem stack)
    {
        List<string?> changes = [];
        stack.PropertyChanged += (_, args) => changes.Add(args.PropertyName);
        return changes;
    }

    private static StackFolderPreviewMetrics CalculatePreviewMetrics(WidgetStackItem stack, bool listMode) =>
        StackFolderPreviewMetricsCalculator.Calculate(
            listMode ? stack.ListIconSize : stack.PreviewSize,
            listMode ? stack.ListIconSize : stack.PreviewItemSize,
            listMode,
            SettingsService.WidgetCornerPreferenceSquare);

    private static void AssertChanges(IEnumerable<string?> actual, params string[] expected) =>
        Assert.Equal(expected.OrderBy(name => name, StringComparer.Ordinal),
            actual.OrderBy(name => name, StringComparer.Ordinal));

    private static void Update(
        WidgetStackItem stack,
        IReadOnlyList<WidgetItem> members,
        string? summary = null,
        string automationState = "Collapsed",
        string collapseText = "Collapse") =>
        stack.Update(members, "Documents", summary ?? $"{members.Count} items", automationState,
            collapseText, isExpanded: false, tileWidth: 80, tileHeight: 80,
            tileMargin: new Thickness(1), tilePadding: new Thickness(2),
            previewSize: 48, previewItemSize: 40, labelMaxWidth: 72, labelFontSize: 12,
            listMargin: new Thickness(3), listPadding: new Thickness(4), listIconSize: 32);

    private static void UpdateMetrics(WidgetStackItem stack, string changedProperty) =>
        stack.UpdateLayoutMetrics(
            tileWidth: changedProperty == nameof(WidgetStackItem.TileWidth) ? 90 : 80,
            tileHeight: changedProperty == nameof(WidgetStackItem.TileHeight) ? 90 : 80,
            tileMargin: new Thickness(changedProperty == nameof(WidgetStackItem.TileMargin) ? 2 : 1),
            tilePadding: new Thickness(changedProperty == nameof(WidgetStackItem.TilePadding) ? 3 : 2),
            previewSize: changedProperty == nameof(WidgetStackItem.PreviewSize) ? 56 : 48,
            previewItemSize: changedProperty == nameof(WidgetStackItem.PreviewItemSize) ? 48 : 40,
            labelMaxWidth: changedProperty == nameof(WidgetStackItem.LabelMaxWidth) ? 80 : 72,
            labelFontSize: changedProperty == nameof(WidgetStackItem.LabelFontSize) ? 14 : 12,
            listMargin: new Thickness(changedProperty == nameof(WidgetStackItem.ListMargin) ? 4 : 3),
            listPadding: new Thickness(changedProperty == nameof(WidgetStackItem.ListPadding) ? 5 : 4),
            listIconSize: changedProperty == nameof(WidgetStackItem.ListIconSize) ? 40 : 32);
}
