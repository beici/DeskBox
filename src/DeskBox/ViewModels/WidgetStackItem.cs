using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;

namespace DeskBox.ViewModels;

public sealed partial class WidgetStackItem : WidgetItem
{
    private IReadOnlyList<WidgetItem> _members = [];
    private string _summary = string.Empty;
    private string _automationState = string.Empty;
    private string _collapseText = string.Empty;
    private bool _isExpanded;
    private double _tileWidth;
    private double _tileHeight;
    private Thickness _tileMargin;
    private Thickness _tilePadding;
    private double _previewSize;
    private double _previewItemSize;
    private double _labelMaxWidth;
    private double _labelFontSize;
    private Thickness _listMargin;
    private Thickness _listPadding;
    private double _listIconSize;

    public required WidgetStackCategory Category { get; init; }

    public required string StackKey { get; init; }

    public bool IsManual => StackKey.StartsWith(
        "Manual:",
        StringComparison.Ordinal);

    public IReadOnlyList<WidgetItem> Members => _members;

    public string Summary => _summary;

    public string AutomationState => _automationState;

    public string CollapseText => _collapseText;

    // Preview slots may be evaluated while the group is tearing down and
    // Members is empty; the bindings tolerate a null intermediate, so the
    // getters guard instead of indexing into an empty list.
    public WidgetItem? PreviewOne => Members.Count > 0 ? Members[0] : null;

    public WidgetItem? PreviewTwo => Members.Count > 0
        ? Members[Math.Min(1, Members.Count - 1)]
        : null;

    public WidgetItem? PreviewThree => Members.Count > 2 ? Members[2] : null;

    public WidgetItem? PreviewFour => Members.Count > 3 ? Members[3] : null;

    public Visibility ThirdPreviewVisibility => Members.Count >= 3
        ? Visibility.Visible
        : Visibility.Collapsed;

    public Visibility FourthPreviewVisibility => Members.Count >= 4
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string CountText => Members.Count.ToString();

    public bool IsExpanded => _isExpanded;

    public Visibility CollapsedPreviewVisibility => IsExpanded
        ? Visibility.Collapsed
        : Visibility.Visible;

    public Visibility ExpandedAnchorVisibility => IsExpanded
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string ChevronGlyph => IsExpanded ? "\uE70E" : "\uE70D";

    public double TileWidth
    {
        get => _tileWidth;
        private set => SetProperty(ref _tileWidth, value);
    }

    public double TileHeight
    {
        get => _tileHeight;
        private set => SetProperty(ref _tileHeight, value);
    }

    public Thickness TileMargin
    {
        get => _tileMargin;
        private set => SetProperty(ref _tileMargin, value);
    }

    public Thickness TilePadding
    {
        get => _tilePadding;
        private set => SetProperty(ref _tilePadding, value);
    }

    public double PreviewSize
    {
        get => _previewSize;
        private set => SetProperty(ref _previewSize, value);
    }

    public double PreviewItemSize
    {
        get => _previewItemSize;
        private set => SetProperty(ref _previewItemSize, value);
    }

    public double LabelMaxWidth
    {
        get => _labelMaxWidth;
        private set => SetProperty(ref _labelMaxWidth, value);
    }

    public double LabelFontSize
    {
        get => _labelFontSize;
        private set => SetProperty(ref _labelFontSize, value);
    }

    public Thickness ListMargin
    {
        get => _listMargin;
        private set => SetProperty(ref _listMargin, value);
    }

    public Thickness ListPadding
    {
        get => _listPadding;
        private set => SetProperty(ref _listPadding, value);
    }

    public double ListIconSize
    {
        get => _listIconSize;
        private set => SetProperty(ref _listIconSize, value);
    }

    public void Update(
        IReadOnlyList<WidgetItem> members,
        string name,
        string summary,
        string automationState,
        string collapseText,
        bool isExpanded,
        double tileWidth,
        double tileHeight,
        Thickness tileMargin,
        Thickness tilePadding,
        double previewSize,
        double previewItemSize,
        double labelMaxWidth,
        double labelFontSize,
        Thickness listMargin,
        Thickness listPadding,
        double listIconSize)
    {
        ArgumentNullException.ThrowIfNull(members);
        IReadOnlyList<WidgetItem> previousMembers = _members;
        bool membersChanged = !HaveSameMembers(previousMembers, members);
        string previousSummary = _summary;
        string previousAutomationState = _automationState;
        string previousCollapseText = _collapseText;
        bool previousExpanded = _isExpanded;

        // Own the member sequence so a provider's reused mutable list cannot
        // change the published previews before their notifications arrive.
        if (membersChanged)
        {
            _members = members.ToArray();
        }
        _summary = summary;
        _automationState = automationState;
        _collapseText = collapseText;
        _isExpanded = isExpanded;
        Name = name;
        UpdateLayoutMetrics(
            tileWidth,
            tileHeight,
            tileMargin,
            tilePadding,
            previewSize,
            previewItemSize,
            labelMaxWidth,
            labelFontSize,
            listMargin,
            listPadding,
            listIconSize);

        if (membersChanged)
        {
            NotifyMemberChanges(previousMembers);
        }
        NotifyIfChanged(previousSummary, _summary, nameof(Summary));
        NotifyIfChanged(previousAutomationState, _automationState, nameof(AutomationState));
        NotifyIfChanged(previousCollapseText, _collapseText, nameof(CollapseText));
        if (previousExpanded != _isExpanded)
        {
            NotifyExpansionChanged();
        }
    }

    public void UpdateLayoutMetrics(
        double tileWidth,
        double tileHeight,
        Thickness tileMargin,
        Thickness tilePadding,
        double previewSize,
        double previewItemSize,
        double labelMaxWidth,
        double labelFontSize,
        Thickness listMargin,
        Thickness listPadding,
        double listIconSize)
    {
        TileWidth = tileWidth;
        TileHeight = tileHeight;
        TileMargin = tileMargin;
        TilePadding = tilePadding;
        PreviewSize = previewSize;
        PreviewItemSize = previewItemSize;
        LabelMaxWidth = labelMaxWidth;
        LabelFontSize = labelFontSize;
        ListMargin = listMargin;
        ListPadding = listPadding;
        ListIconSize = listIconSize;
    }

    /// <summary>
    /// Flips only the expansion state for the incremental projection path
    /// (expand/collapse toggle without a membership or order change). The
    /// full rebuild keeps ownership of <see cref="Update"/> for real changes.
    /// </summary>
    internal void SetExpanded(bool expanded, string? automationState = null)
    {
        string previousAutomationState = _automationState;
        bool expandedChanged = _isExpanded != expanded;
        _automationState = automationState ?? _automationState;
        _isExpanded = expanded;
        NotifyIfChanged(previousAutomationState, _automationState, nameof(AutomationState));
        if (expandedChanged)
        {
            NotifyExpansionChanged();
        }
    }

    private static bool HaveSameMembers(
        IReadOnlyList<WidgetItem> previous,
        IReadOnlyList<WidgetItem> current)
    {
        if (previous.Count != current.Count)
        {
            return false;
        }

        for (int index = 0; index < previous.Count; index++)
        {
            if (!ReferenceEquals(previous[index], current[index]))
            {
                return false;
            }
        }

        return true;
    }

    private void NotifyMemberChanges(IReadOnlyList<WidgetItem> previous)
    {
        OnPropertyChanged(nameof(Members));
        NotifyPreviewChanged(previous.Count > 0 ? previous[0] : null, PreviewOne, nameof(PreviewOne));
        NotifyPreviewChanged(previous.Count > 0 ? previous[Math.Min(1, previous.Count - 1)] : null, PreviewTwo, nameof(PreviewTwo));
        NotifyPreviewChanged(previous.Count > 2 ? previous[2] : null, PreviewThree, nameof(PreviewThree));
        NotifyPreviewChanged(previous.Count > 3 ? previous[3] : null, PreviewFour, nameof(PreviewFour));
        if (previous.Count != Members.Count)
        {
            OnPropertyChanged(nameof(CountText));
        }
        if ((previous.Count >= 3) != (Members.Count >= 3))
        {
            OnPropertyChanged(nameof(ThirdPreviewVisibility));
        }
        if ((previous.Count >= 4) != (Members.Count >= 4))
        {
            OnPropertyChanged(nameof(FourthPreviewVisibility));
        }
    }

    private void NotifyPreviewChanged(WidgetItem? previous, WidgetItem? current, string propertyName)
    {
        if (!ReferenceEquals(previous, current))
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void NotifyIfChanged(string previous, string current, string propertyName)
    {
        if (!string.Equals(previous, current, StringComparison.Ordinal))
        {
            OnPropertyChanged(propertyName);
        }
    }

    private void NotifyExpansionChanged()
    {
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(CollapsedPreviewVisibility));
        OnPropertyChanged(nameof(ExpandedAnchorVisibility));
        OnPropertyChanged(nameof(ChevronGlyph));
    }

    public void RefreshPresentationState()
    {
        OnPropertyChanged(nameof(Members));
        OnPropertyChanged(nameof(Summary));
        OnPropertyChanged(nameof(AutomationState));
        OnPropertyChanged(nameof(CollapseText));
        OnPropertyChanged(nameof(PreviewOne));
        OnPropertyChanged(nameof(PreviewTwo));
        OnPropertyChanged(nameof(PreviewThree));
        OnPropertyChanged(nameof(PreviewFour));
        OnPropertyChanged(nameof(ThirdPreviewVisibility));
        OnPropertyChanged(nameof(FourthPreviewVisibility));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(IsExpanded));
        OnPropertyChanged(nameof(CollapsedPreviewVisibility));
        OnPropertyChanged(nameof(ExpandedAnchorVisibility));
        OnPropertyChanged(nameof(ChevronGlyph));
    }
}
