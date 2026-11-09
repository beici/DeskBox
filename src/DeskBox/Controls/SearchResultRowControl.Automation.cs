using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;

namespace DeskBox.Controls;

/// <summary>
/// Exposes a search result row to UI Automation as a selectable list item. The row is a
/// plain <see cref="UserControl"/> with pointer-driven selection, so without an explicit
/// peer a screen reader can read the file name but never learns whether the row is the
/// selected one - while Delete and Ctrl+C act on that selection in bulk.
/// </summary>
internal sealed partial class SearchResultRowAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider
{
    private readonly SearchResultRowControl _row;

    public SearchResultRowAutomationPeer(SearchResultRowControl row)
        : base(row)
    {
        _row = row;
    }

    public bool IsSelected => _row.IsSelected || _row.IsMultiSelected;

    public IRawElementProviderSimple? SelectionContainer => null;

    public void AddToSelection() => _row.RequestAutomationSelection();

    public void RemoveFromSelection()
    {
        // The popup owns the single-selection model; clearing from automation would leave
        // the window's _selectedRow out of sync, so removal is intentionally a no-op.
    }

    public void Select() => _row.RequestAutomationSelection();

    protected override AutomationControlType GetAutomationControlTypeCore()
    {
        return AutomationControlType.ListItem;
    }

    protected override string GetClassNameCore()
    {
        return nameof(SearchResultRowControl);
    }

    protected override string GetNameCore()
    {
        string name = base.GetNameCore();
        return string.IsNullOrWhiteSpace(name)
            ? _row.Item?.Title ?? string.Empty
            : name;
    }

    protected override object? GetPatternCore(PatternInterface patternInterface)
    {
        return patternInterface == PatternInterface.SelectionItem
            ? this
            : base.GetPatternCore(patternInterface);
    }
}
