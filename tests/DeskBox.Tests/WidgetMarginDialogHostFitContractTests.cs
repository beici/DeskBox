using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// The margin editor must stay reachable whatever the widget's size. The report
/// was a dialog whose second field row (Bottom / Right) was clipped away, so the
/// entry looked like it only supported Top and Left: it was a ContentDialog in a
/// ~313x326 widget XamlRoot. WidgetWindowBase cannot be instantiated headlessly,
/// so the host wiring is pinned with source contracts (the pattern the other
/// margin dialog tests already use) while the sizing and placement math is
/// covered by WidgetDialogLayoutTests.
/// </summary>
public sealed class WidgetMarginDialogHostFitContractTests
{
    private const string MarginEditorSource = "src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs";
    private const string ToolDialogHostSource = "src/DeskBox/Views/WidgetToolDialogWindow.cs";

    [Fact]
    public void MarginEditor_RunsInTheToolWindow_NotInTheWidgetsOwnXamlRoot()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        Assert.Contains(
            "WidgetDialogViewport viewport = ResolveToolDialogViewport(",
            source,
            StringComparison.Ordinal);
        Assert.Contains("bool saved = await ShowToolDialogAsync(", source, StringComparison.Ordinal);

        // A ContentDialog is bounded by the widget window, which is exactly the
        // clipping this defect was about.
        Assert.DoesNotContain("new ContentDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ContentDialogResult", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ToolDialogHost_IsARealWindow_SizedFromTheWorkArea()
    {
        string host = ReadRepositoryFile(ToolDialogHostSource);
        string wiring = ReadRepositoryFile("src/DeskBox/Views/WidgetWindowBase.ToolDialog.cs");

        // A windowed XAML popup does not take Win32 focus (the same reason the
        // stack popover rename editor is a window), so text entry needs a Window.
        Assert.Contains(
            "internal sealed class WidgetToolDialogWindow : Window",
            host,
            StringComparison.Ordinal);
        Assert.Contains("presenter.IsAlwaysOnTop = true;", host, StringComparison.Ordinal);
        Assert.Contains("_appWindow.IsShownInSwitchers = false;", host, StringComparison.Ordinal);
        Assert.Contains("TaskCompletionSource<bool>", host, StringComparison.Ordinal);
        Assert.Contains(
            "VerticalScrollBarVisibility = ScrollBarVisibility.Auto",
            host,
            StringComparison.Ordinal);

        // The budget and the placement both come from the widget's monitor, and
        // the DIP sizes are converted with the shared scale helper.
        Assert.Contains("RectInt32 workArea = ResolveWorkArea(ownerBounds);", wiring, StringComparison.Ordinal);
        Assert.Contains("WidgetDialogLayout.ResolveViewport(", wiring, StringComparison.Ordinal);
        Assert.Contains("WidgetDialogLayout.ResolveWindowBounds(", wiring, StringComparison.Ordinal);
        Assert.Contains(
            "WidgetPositioningService.ToPhysicalPixels(viewport.WindowWidth, scale)",
            wiring,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MarginEditor_LaysOutAllFourSides_InAutoSizedRows()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        foreach (string sideKey in new[]
                 {
                     "Widget.Margin.Left",
                     "Widget.Margin.Top",
                     "Widget.Margin.Right",
                     "Widget.Margin.Bottom"
                 })
        {
            Assert.Contains(sideKey, source, StringComparison.Ordinal);
        }

        // Four cells, placed as Top / Bottom / Left / Right.
        Assert.Contains(
            "int[] sidePlacementOrder = [1, 3, 0, 2];",
            source,
            StringComparison.Ordinal);
        Assert.Contains("perSidePanel.Children.Add(cell);", source, StringComparison.Ordinal);

        // Auto rows only: star rows split the available height and squeezed the
        // trailing pair into an unreadable sliver before it was clipped outright.
        Assert.Contains(
            "perSidePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "perSidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MarginEditor_KeepsSideLabelsSingleLine_AndNamesTheReferenceBeside()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        // The reference object moved out of the TextBox header (where a long name
        // wrapped and doubled every row) into a caption plus a tooltip.
        Assert.DoesNotContain("BuildMarginSideHeader(\"Widget.Margin.Left\"", source, StringComparison.Ordinal);
        Assert.Contains(
            "sideCaptions[index].Text = DescribeMarginReference(kind);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("ToolTipService.SetToolTip(", source, StringComparison.Ordinal);

        // A move can change which object a side is measured against, so the
        // captions are re-resolved from the live geometry, not just at open.
        int applyAtOpen = source.IndexOf("ApplySideReferences(edges);", StringComparison.Ordinal);
        int applyAfterMove = source.IndexOf("ApplySideReferences(liveEdges);", StringComparison.Ordinal);
        Assert.True(applyAtOpen > 0, "the captions must be filled when the editor opens");
        Assert.True(applyAfterMove > applyAtOpen, "the captions must be refreshed after a preview move");
    }

    [Fact]
    public void NarrowBudget_StacksTheSidesInsteadOfClippingAColumn()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        Assert.Contains(
            "bool singleColumn = viewport.PrefersSingleColumn;",
            source,
            StringComparison.Ordinal);
        Assert.Contains("int rowCount = singleColumn ? sideCells.Length : 2;", source, StringComparison.Ordinal);
        Assert.Contains("int columnCount = singleColumn ? 1 : 2;", source, StringComparison.Ordinal);

        // Long labels wrap rather than being cut off; several locales are far
        // more verbose than zh-CN here.
        int wrappingLabels = Regex.Matches(source, Regex.Escape("TextWrapping = TextWrapping.Wrap")).Count;
        Assert.True(
            wrappingLabels >= 4,
            $"expected the mode, hint and batch labels to wrap, found {wrappingLabels}");
    }

    [Fact]
    public void WidgetColorPicker_UsesTheSameToolWindowHost()
    {
        string source = ReadRepositoryFile("src/DeskBox/Views/WidgetWindowBase.Foreground.cs");

        // Same defect class: a colour picker is taller than most widgets, so a
        // widget-hosted ContentDialog clipped it.
        Assert.Contains(
            "WidgetDialogViewport viewport = ResolveToolDialogViewport(",
            source,
            StringComparison.Ordinal);
        Assert.Contains("bool saved = await ShowToolDialogAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("new ContentDialog", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MinWidth = 340", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MarginPreview_IsDebounced_AndCannotFireAfterTheEditorCloses()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        // Applying every keystroke moved the widget for the leading digit of a
        // typed value, persisted settings each time, and the layout burst could
        // swallow the next keystroke.
        Assert.Contains(
            "previewTimer.Interval = TimeSpan.FromMilliseconds(MarginPreviewDebounceMilliseconds);",
            source,
            StringComparison.Ordinal);
        Assert.Contains("previewTimer.IsRepeating = false;", source, StringComparison.Ordinal);
        Assert.Contains("previewTimer.Start();", source, StringComparison.Ordinal);

        // Save must honour a side that was typed but not yet previewed, so the
        // edited side is recorded before the timer is armed.
        int recordsEditedSide = source.IndexOf("editedSides.Add(\"Left\")", StringComparison.Ordinal);
        int armsTimer = source.IndexOf("previewTimer.Start();", StringComparison.Ordinal);
        Assert.True(recordsEditedSide > 0 && recordsEditedSide < armsTimer);

        // A pending tick after cancel would move the widget again right after the
        // position was restored.
        int closes = source.IndexOf("bool saved = await ShowToolDialogAsync(", StringComparison.Ordinal);
        int stopsAfterClose = source.IndexOf("previewTimer.Stop();", closes, StringComparison.Ordinal);
        Assert.True(stopsAfterClose > closes, "the debounce must be stopped once the editor closes");
    }

    [Fact]
    public void MarginPreview_RefreshesTheOtherSides_WithoutTouchingTheFocusedBox()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        // Moving the widget changes the gap on every side, so the untouched
        // boxes are re-read; the focused box is skipped because a clamped target
        // would otherwise rewrite text under the caret.
        Assert.Contains("SyncBoxesFromLiveMargins(source);", source, StringComparison.Ordinal);
        Assert.Contains(
            "void SyncBoxesFromLiveMargins(TextBox? focused = null)",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (ReferenceEquals(sideBoxes[index], focused))",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void LiveSyncWrites_AreNotMistakenForUserEdits()
    {
        string source = ReadRepositoryFile(MarginEditorSource);

        // WinUI raises TextChanged for programmatic writes too, and it can arrive
        // after the write returns, so the suppress flag alone leaked: the sync's
        // own writes were treated as edits, marked their side edited and moved the
        // widget to satisfy a value nobody typed (a synced bottom of 0 dragged the
        // widget down onto its neighbour).
        Assert.Contains(
            "var programmaticEntries = new Dictionary<TextBox, string>();",
            source,
            StringComparison.Ordinal);
        Assert.Contains("void WriteEntry(TextBox box, string text)", source, StringComparison.Ordinal);
        Assert.Contains(
            "if (suppressMarginPreview || IsProgrammaticEcho(source))",
            source,
            StringComparison.Ordinal);

        // Every editor-owned write goes through WriteEntry, so no raw assignment
        // to an entry box may remain.
        Assert.DoesNotContain("uniformBox.Text = ", source, StringComparison.Ordinal);
        Assert.DoesNotContain("sideBoxes[index].Text = ", source, StringComparison.Ordinal);
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        return File.ReadAllText(TestPaths.FromRepository(relativePath));
    }
}
