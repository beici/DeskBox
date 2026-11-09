using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskBox.Tests;

/// <summary>
/// Pins the round-6 accessibility fixes: IME-safe rename commit, keyboard reachability of
/// todo rows, the selection pattern for search result rows, work-area fitting for the
/// onboarding window and search popup, and the dial contrast of the pomodoro widget.
/// </summary>
public sealed class RoundSixAccessibilityContractTests
{
    [Fact]
    public void RenameEnter_WaitsForImeComposition()
    {
        string tracker = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Helpers/TextCompositionTracker.cs"));
        Assert.Contains("TextCompositionStarted", tracker, StringComparison.Ordinal);
        Assert.Contains("TextCompositionEnded", tracker, StringComparison.Ordinal);
        Assert.Contains("public static bool IsComposing(TextBox? textBox)", tracker, StringComparison.Ordinal);

        string surface = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.xaml.cs"));
        Assert.Contains("TextCompositionTracker.Attach(ItemRenameTextBox);", surface, StringComparison.Ordinal);
        Assert.Contains(
            "if (TextCompositionTracker.IsComposing(ItemRenameTextBox))",
            surface,
            StringComparison.Ordinal);

        string stackPopover = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/FileSurfaceContent.StackPopoverRename.cs"));
        Assert.Contains("TextCompositionTracker.Attach(editor);", stackPopover, StringComparison.Ordinal);
        Assert.Contains("TextCompositionTracker.IsComposing(sender as TextBox)", stackPopover, StringComparison.Ordinal);

        string window = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Views/ContentWidgetWindow.Commands.cs"));
        Assert.Contains("TextCompositionTracker.Attach(editor);", window, StringComparison.Ordinal);
        Assert.Contains("TextCompositionTracker.IsComposing(sender as TextBox)", window, StringComparison.Ordinal);
    }

    [Fact]
    public void TodoNewStep_WaitsForImeComposition()
    {
        string source = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.DetailNotesAndSteps.cs"));
        Assert.Contains(
            "TextCompositionTracker.Attach(DetailNewStepTextBox);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "if (TextCompositionTracker.IsComposing(DetailNewStepTextBox))",
            source,
            StringComparison.Ordinal);

        string title = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml.cs"));
        Assert.Contains("TextCompositionTracker.Attach(DetailTitleTextBox);", title, StringComparison.Ordinal);
        Assert.Contains(
            "if (TextCompositionTracker.IsComposing(DetailTitleTextBox))",
            title,
            StringComparison.Ordinal);
    }

    [Fact]
    public void TodoRows_KeepKeyboardFocusVisible()
    {
        string xaml = ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.xaml");
        Assert.Contains(
            "<Setter Property=\"UseSystemFocusVisuals\" Value=\"True\" />",
            xaml,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "<Setter Property=\"UseSystemFocusVisuals\" Value=\"False\" />",
            xaml,
            StringComparison.Ordinal);

        string interaction = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/TodoWidgetContent.ListInteraction.cs"));
        Assert.Contains("GetFocusedTodoItem()", interaction, StringComparison.Ordinal);
        Assert.Contains("MoveTodoKeyboardFocus(", interaction, StringComparison.Ordinal);
        Assert.Contains("e.Key == VirtualKey.Space && GetFocusedTodoItem()", interaction, StringComparison.Ordinal);
        Assert.Contains("e.Key is VirtualKey.Up or VirtualKey.Down", interaction, StringComparison.Ordinal);
        Assert.Contains("container.Focus(FocusState.Keyboard)", interaction, StringComparison.Ordinal);
        // Rows must stay focusable: WinUI only lets an element take focus when IsTabStop is
        // true, so pinning it to false made container.Focus a no-op and left every keyboard
        // path dead. The list itself still exposes a single tab stop via TabNavigation=Once.
        // Scope this to the row style: other controls in this template legitimately opt out
        // of the tab order (the completion CheckBox defers to the row's keyboard stop), and
        // a whole-file guard turned those into false positives.
        int rowStyleStart = xaml.IndexOf("TargetType=\"ListViewItem\"", StringComparison.Ordinal);
        Assert.True(rowStyleStart >= 0, "the ListViewItem row style was not found");
        int rowStyleEnd = xaml.IndexOf("</Style>", rowStyleStart, StringComparison.Ordinal);
        Assert.True(rowStyleEnd > rowStyleStart, "the row style block was not closed");
        // Normalize whitespace so a reformatted setter cannot slip past the guard.
        string normalizedRowStyle = string.Join(
            ' ',
            xaml[rowStyleStart..rowStyleEnd].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        Assert.DoesNotContain(
            "Property=\"IsTabStop\" Value=\"False\"",
            normalizedRowStyle,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SearchResultRows_ExposeSelectionItemPattern()
    {
        string peer = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/SearchResultRowControl.Automation.cs"));
        Assert.Contains("ISelectionItemProvider", peer, StringComparison.Ordinal);
        Assert.Contains("AutomationControlType.ListItem", peer, StringComparison.Ordinal);
        Assert.Contains("public void Select() => _row.RequestAutomationSelection();", peer, StringComparison.Ordinal);
        Assert.Contains("PatternInterface.SelectionItem", peer, StringComparison.Ordinal);

        string row = StripCommentLines(
            ReadRepositoryFile("src/DeskBox/Controls/SearchResultRowControl.xaml.cs"));
        Assert.Contains("new SearchResultRowAutomationPeer(this)", row, StringComparison.Ordinal);
        Assert.Contains("SelectionItemPatternIdentifiers.IsSelectedProperty", row, StringComparison.Ordinal);
        Assert.Contains("AutomationSelectionRequested", row, StringComparison.Ordinal);

        string window = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs"));
        Assert.Contains("row.AutomationSelectionRequested += OnRowAutomationSelectionRequested;", window, StringComparison.Ordinal);
        int handlerStart = window.IndexOf("private void OnRowAutomationSelectionRequested(", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "the automation selection handler was not found.");
        int handlerEnd = window.IndexOf("private void OnResultsElementPrepared(", handlerStart, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart, "the automation selection handler end marker was not found.");
        string handler = window[handlerStart..handlerEnd];
        int clearIndex = handler.IndexOf("ClearMultiSelection();", StringComparison.Ordinal);
        int selectIndex = handler.IndexOf("SelectResultItem(item, row);", StringComparison.Ordinal);
        Assert.True(clearIndex >= 0, "automation selection must drop the stale multi-selection.");
        Assert.True(
            selectIndex > clearIndex,
            "automation selection must clear the multi-selection before it selects the announced row.");
    }

    [Fact]
    public void SearchBox_WaitsForImeComposition()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs"));
        Assert.Contains("TextCompositionTracker.Attach(SearchTextBox);", source, StringComparison.Ordinal);

        int handlerStart = source.IndexOf("private void SearchTextBox_KeyDown(", StringComparison.Ordinal);
        Assert.True(handlerStart >= 0, "SearchTextBox_KeyDown was not found.");
        int handlerEnd = source.IndexOf("private ", handlerStart + 10, StringComparison.Ordinal);
        Assert.True(handlerEnd > handlerStart, "the SearchTextBox_KeyDown end marker was not found.");
        string handler = source[handlerStart..handlerEnd];
        int guardIndex = handler.IndexOf("TextCompositionTracker.IsComposing(SearchTextBox)", StringComparison.Ordinal);
        int switchIndex = handler.IndexOf("switch (e.Key)", StringComparison.Ordinal);
        Assert.True(guardIndex >= 0, "the search box must ignore keys while an IME composes.");
        Assert.True(switchIndex > guardIndex, "the IME guard must run before the key switch.");
    }

    [Fact]
    public void OnboardingWindow_FitsInsideTheWorkArea()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/OnboardingWindow.xaml.cs"));

        Assert.Contains("int maxHeight = Math.Max(1, workArea.Height - workAreaMargin);", source, StringComparison.Ordinal);
        Assert.Contains(
            "int height = Math.Clamp(desiredHeight, Math.Min(minHeight, maxHeight), maxHeight);",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "minMaxInfo.MinTrackSize.Y = Math.Clamp(ToPhysicalPixels(MinWindowHeight, scale), 1, maxHeight);",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SearchPopup_KeepsFooterInsideTheWorkArea()
    {
        string source = StripCommentLines(ReadRepositoryFile("src/DeskBox/Views/SearchPopupWindow.xaml.cs"));
        int start = source.IndexOf("private void PositionOnScreen()", StringComparison.Ordinal);
        Assert.True(start >= 0, "PositionOnScreen was not found.");
        int end = source.IndexOf("private void SetupBindings()", start, StringComparison.Ordinal);
        Assert.True(end > start, "the PositionOnScreen end marker was not found.");
        string method = source[start..end];

        Assert.Contains("x = Math.Clamp(x, workLeft,", method, StringComparison.Ordinal);
        Assert.Contains("y = Math.Clamp(y, workTop,", method, StringComparison.Ordinal);
        Assert.Contains("workTop + workHeight - popupSize.Height", method, StringComparison.Ordinal);
    }

    [Fact]
    public void PomodoroDialText_MeetsContrastOnTheDisc()
    {
        string xaml = ReadRepositoryFile("src/DeskBox/Controls/WidgetContents/PomodoroWidgetContent.xaml");
        foreach (string theme in new[] { "Light", "Dark" })
        {
            string block = ReadThemeDictionary(xaml, theme);
            string disc = ReadBrushColor(block, "PomodoroDialSurfaceBrush");
            string ink = ReadBrushColor(block, "PomodoroDialTextBrush");

            double ratio = ContrastRatio(ink, disc);
            Assert.True(
                ratio >= 4.5,
                $"{theme}: dial text {ink} on disc {disc} is {ratio:0.00}:1, below 4.5:1.");
        }
    }

    private static string ReadThemeDictionary(string xaml, string theme)
    {
        int start = xaml.IndexOf($"<ResourceDictionary x:Key=\"{theme}\">", StringComparison.Ordinal);
        Assert.True(start >= 0, $"the {theme} theme dictionary was not found.");
        int end = xaml.IndexOf("</ResourceDictionary>", start, StringComparison.Ordinal);
        Assert.True(end > start, $"the {theme} theme dictionary end was not found.");
        return xaml[start..end];
    }

    private static string ReadBrushColor(string block, string key)
    {
        Match match = Regex.Match(
            block,
            $"x:Key=\"{Regex.Escape(key)}\"\\s+Color=\"(#[0-9A-Fa-f]{{6,8}})\"");
        Assert.True(match.Success, $"brush {key} was not found in the theme dictionary.");
        return match.Groups[1].Value;
    }

    private static double ContrastRatio(string foreground, string background)
    {
        double first = RelativeLuminance(foreground);
        double second = RelativeLuminance(background);
        double lighter = Math.Max(first, second);
        double darker = Math.Min(first, second);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static double RelativeLuminance(string color)
    {
        string value = color.TrimStart('#');
        if (value.Length == 8)
        {
            value = value[2..];
        }

        int red = int.Parse(value[..2], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int green = int.Parse(value.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int blue = int.Parse(value.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        return (0.2126 * ToLinear(red)) + (0.7152 * ToLinear(green)) + (0.0722 * ToLinear(blue));

        static double ToLinear(int channel)
        {
            double scaled = channel / 255.0;
            return scaled <= 0.03928
                ? scaled / 12.92
                : Math.Pow((scaled + 0.055) / 1.055, 2.4);
        }
    }

    private static string StripCommentLines(string source)
    {
        var builder = new System.Text.StringBuilder(source.Length);
        foreach (string line in source.Split('\n'))
        {
            if (line.TrimStart().StartsWith("//", StringComparison.Ordinal))
            {
                continue;
            }

            builder.Append(line).Append('\n');
        }

        return builder.ToString();
    }

    private static string ReadRepositoryFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !Directory.Exists(Path.Combine(directory.FullName, "src", "DeskBox")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new DirectoryNotFoundException("DeskBox repository root was not found.");
        }

        return File.ReadAllText(Path.Combine(
            directory.FullName,
            relativePath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
