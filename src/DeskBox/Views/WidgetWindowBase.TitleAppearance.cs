using DeskBox.Helpers;
using DeskBox.Models;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics;

namespace DeskBox.Views;

public abstract partial class WidgetWindowBase
{
    /// <summary>
    /// Applies the title alignment and the custom icon image resolved from
    /// the widget's effective preferences. Called from the title bar layout
    /// pass of both hosts, so theme and appearance refreshes re-evaluate it.
    /// </summary>
    protected void ApplyTitleAppearance()
    {
        string alignment = WidgetTitleAppearanceSettings.ResolveAlignment(
            Config,
            SettingsService.Settings);
        WidgetShellControl.SetTitleAlignment(alignment switch
        {
            WidgetTitleAppearanceSettings.AlignCenter => HorizontalAlignment.Center,
            WidgetTitleAppearanceSettings.AlignRight => HorizontalAlignment.Right,
            _ => HorizontalAlignment.Left
        });

        string? iconPath = WidgetTitleAppearanceSettings.GetCustomIconPath(Config);
        if (string.IsNullOrWhiteSpace(iconPath))
        {
            WidgetShellControl.SetTitleCustomIcon(null);
            return;
        }

        try
        {
            // BitmapImage decodes asynchronously and scales the bitmap to the
            // icon surface; a missing or unreadable file simply keeps the
            // previous icon instead of failing the layout pass.
            var image = new BitmapImage(new Uri(iconPath));
            WidgetShellControl.SetTitleCustomIcon(image);
        }
        catch (Exception ex)
        {
            App.Log($"[TitleAppearance] Custom icon load failed path={iconPath}: {ex.Message}");
            WidgetShellControl.SetTitleCustomIcon(null);
        }
    }

    /// <summary>
    /// Builds the "标题与图标" menu: alignment choices (with batch apply),
    /// a local-image icon picker, and icon reset (single widget or all).
    /// </summary>
    protected MenuFlyoutSubItem CreateTitleAppearanceMenu(Action hideFlyout)
    {
        var localization = App.Current.LocalizationService;
        var menu = new MenuFlyoutSubItem
        {
            Text = localization.T("Widget.TitleAppearance.Menu"),
            Icon = new FontIcon { Glyph = "\uE771" }
        };

        string currentAlignment = WidgetTitleAppearanceSettings.ResolveAlignment(
            Config,
            SettingsService.Settings);
        foreach ((string value, string key, string glyph) in new[]
                 {
                     (WidgetTitleAppearanceSettings.AlignLeft, "Widget.TitleAppearance.AlignLeft", "\uE8AB"),
                     (WidgetTitleAppearanceSettings.AlignCenter, "Widget.TitleAppearance.AlignCenter", "\uE8B2"),
                     (WidgetTitleAppearanceSettings.AlignRight, "Widget.TitleAppearance.AlignRight", "\uE8AC")
                 })
        {
            var item = new ToggleMenuFlyoutItem
            {
                Text = localization.T(key),
                IsChecked = string.Equals(currentAlignment, value, StringComparison.Ordinal)
            };
            item.Click += (_, _) =>
            {
                WidgetTitleAppearanceSettings.SetAlignmentOverride(Config, value);
                SettingsService.UpdateWidget(Config);
                ApplyTitleAppearance();
            };
            menu.Items.Add(item);
        }

        var applyAlignmentToAll = new MenuFlyoutItem
        {
            Text = localization.T("Widget.TitleAppearance.AlignAll")
        };
        applyAlignmentToAll.Click += (_, _) =>
        {
            SettingsService.Settings.WidgetTitleAlignment = WidgetTitleAppearanceSettings.NormalizeAlignment(
                WidgetTitleAppearanceSettings.ResolveAlignment(Config, SettingsService.Settings));
            foreach (WidgetConfig widget in SettingsService.Settings.Widgets)
            {
                WidgetTitleAppearanceSettings.SetAlignmentOverride(widget, null);
            }

            SettingsService.UpdateWidgetsBatch(SettingsService.Settings.Widgets);
            RefreshTitleAppearanceOnVisibleWidgets();
        };
        menu.Items.Add(applyAlignmentToAll);

        menu.Items.Add(new MenuFlyoutSeparator());

        var pickIcon = new MenuFlyoutItem
        {
            Text = localization.T("Widget.TitleAppearance.CustomIcon"),
            Icon = new FontIcon { Glyph = "\uEB9F" }
        };
        pickIcon.Click += async (_, _) =>
        {
            hideFlyout();
            await PickCustomIconAsync();
        };
        menu.Items.Add(pickIcon);

        var resetIcon = new MenuFlyoutItem
        {
            Text = localization.T("Widget.TitleAppearance.ResetIcon"),
            Icon = new FontIcon { Glyph = "\uE777" }
        };
        resetIcon.Click += (_, _) =>
        {
            WidgetTitleAppearanceSettings.SetCustomIconPath(Config, null);
            SettingsService.UpdateWidget(Config);
            ApplyTitleAppearance();
        };
        menu.Items.Add(resetIcon);

        var resetAllIcons = new MenuFlyoutItem
        {
            Text = localization.T("Widget.TitleAppearance.ResetAllIcons")
        };
        resetAllIcons.Click += (_, _) =>
        {
            foreach (WidgetConfig widget in SettingsService.Settings.Widgets)
            {
                WidgetTitleAppearanceSettings.SetCustomIconPath(widget, null);
            }

            SettingsService.UpdateWidgetsBatch(SettingsService.Settings.Widgets);
            RefreshTitleAppearanceOnVisibleWidgets();
        };
        menu.Items.Add(resetAllIcons);

        return menu;
    }

    private async Task PickCustomIconAsync()
    {
        try
        {
            IReadOnlyList<string> files = await FileOpenPickerService.PickFilesAsync(HWnd);
            string? selected = files.FirstOrDefault(path =>
                WidgetTitleAppearanceSettings.IsSupportedImageExtension(path));
            if (selected is null)
            {
                return;
            }

            WidgetTitleAppearanceSettings.SetCustomIconPath(Config, selected);
            SettingsService.UpdateWidget(Config);
            ApplyTitleAppearance();
        }
        catch (Exception ex)
        {
            App.Log($"[TitleAppearance] Icon picker failed: {ex.Message}");
        }
    }

    internal void RefreshTitleAppearanceOnVisibleWidgets()
    {
        App.Current?.WidgetManager?.ApplyTitleAppearanceToVisibleWidgets();
    }

    /// <summary>
    /// Builds the "边距" menu entry and opens the margin entry dialog. The
    /// current per-side distances are derived from the live window bounds on
    /// every open, which is what keeps typed values and drag operations in
    /// sync without storing a second copy of the position.
    /// </summary>
    protected MenuFlyoutItem CreateMarginMenuEntry(Action hideFlyout)
    {
        var localization = App.Current.LocalizationService;
        var item = new MenuFlyoutItem
        {
            Text = localization.T("Widget.Margin.Configure"),
            Icon = new FontIcon { Glyph = "\uE78A" }
        };
        item.Click += async (_, _) =>
        {
            hideFlyout();
            await ShowMarginDialogAsync();
        };
        return item;
    }

    private async Task ShowMarginDialogAsync()
    {
        if (RootElement.XamlRoot is null)
        {
            return;
        }

        var localization = App.Current.LocalizationService;
        (int left, int top, int right, int bottom) = GetCurrentReferenceMargins();

        string entryMode = WidgetMarginSettings.GetModeOverride(Config) ??
            WidgetMarginSettings.ModeUniform;

        var modeSelection = new RadioButtons
        {
            SelectedIndex = entryMode == WidgetMarginSettings.ModePerSide ? 1 : 0
        };
        modeSelection.Items.Add(localization.T("Widget.Margin.Uniform"));
        modeSelection.Items.Add(localization.T("Widget.Margin.PerSide"));

        var uniformBox = new TextBox
        {
            Header = localization.T("Widget.Margin.UniformValue"),
            Text = GetNearestEdgeMargin(left, top, right, bottom)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var leftBox = new TextBox { Header = localization.T("Widget.Margin.Left"), Text = left.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var topBox = new TextBox { Header = localization.T("Widget.Margin.Top"), Text = top.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var rightBox = new TextBox { Header = localization.T("Widget.Margin.Right"), Text = right.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var bottomBox = new TextBox { Header = localization.T("Widget.Margin.Bottom"), Text = bottom.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        // 2x2 grid: row 0 = Top / Bottom, row 1 = Left / Right, so the four
        // independent margins read spatially the way they apply.
        var perSidePanel = new Grid { ColumnSpacing = 10, RowSpacing = 8 };
        perSidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        perSidePanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        perSidePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        perSidePanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void PlaceInGrid(TextBox box, int row, int column)
        {
            box.Header = box.Header;
            Grid.SetRow(box, row);
            Grid.SetColumn(box, column);
            perSidePanel.Children.Add(box);
        }

        PlaceInGrid(topBox, 0, 0);
        PlaceInGrid(bottomBox, 1, 0);
        PlaceInGrid(leftBox, 0, 1);
        PlaceInGrid(rightBox, 1, 1);

        var applyToAll = new CheckBox { Content = localization.T("Widget.Margin.ApplyToAll") };
        var validation = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var content = new StackPanel { Spacing = 12, MinWidth = 280 };
        content.Children.Add(modeSelection);
        content.Children.Add(uniformBox);
        content.Children.Add(perSidePanel);
        content.Children.Add(applyToAll);
        content.Children.Add(validation);

        void UpdateModeVisibility()
        {
            bool perSide = modeSelection.SelectedIndex == 1;
            uniformBox.Visibility = perSide ? Visibility.Collapsed : Visibility.Visible;
            perSidePanel.Visibility = perSide ? Visibility.Visible : Visibility.Collapsed;
            if (!perSide)
            {
                // The uniform entry must always describe the live position —
                // a per-side preview moves the window, so a value captured at
                // dialog-open time would be stale and Save would re-apply it.
                (int liveLeft, int liveTop, int liveRight, int liveBottom) =
                    GetCurrentReferenceMargins();
                uniformBox.Text = GetNearestEdgeMargin(liveLeft, liveTop, liveRight, liveBottom)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }

        UpdateModeVisibility();
        modeSelection.SelectionChanged += (_, _) => UpdateModeVisibility();

        // Programmatic .Text writes (the live-sync below) must not re-run the
        // preview/apply loop: out-of-range live margins (>200px) would surface
        // a bogus validation error and re-entry would fight the user's edits.
        bool suppressMarginPreview = false;
        var editedSides = new HashSet<string>(StringComparer.Ordinal);

        void TryPreview(TextBox source)
        {
            if (suppressMarginPreview)
            {
                return;
            }

            validation.Visibility = Visibility.Collapsed;
            if (!WidgetMarginSettings.TryParseMargin(source.Text, out int value))
            {
                validation.Text = string.Format(
                    localization.T("Widget.Margin.Invalid"),
                    WidgetMarginSettings.MinimumMarginPixels,
                    WidgetMarginSettings.MaximumMarginPixels);
                validation.Visibility = Visibility.Visible;
                return;
            }

            if (ReferenceEquals(source, leftBox)) editedSides.Add("Left");
            else if (ReferenceEquals(source, topBox)) editedSides.Add("Top");
            else if (ReferenceEquals(source, rightBox)) editedSides.Add("Right");
            else if (ReferenceEquals(source, bottomBox)) editedSides.Add("Bottom");

            ApplyMarginsFromDialog(
                modeSelection.SelectedIndex == 1,
                editedSides,
                uniformBox.Text,
                leftBox.Text,
                topBox.Text,
                rightBox.Text,
                bottomBox.Text,
                applyToAll.IsChecked == true);
        }

        void SyncBoxesFromLiveMargins()
        {
            suppressMarginPreview = true;
            try
            {
                (int newLeft, int newTop, int newRight, int newBottom) = GetCurrentReferenceMargins();
                leftBox.Text = newLeft.ToString(System.Globalization.CultureInfo.InvariantCulture);
                topBox.Text = newTop.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rightBox.Text = newRight.ToString(System.Globalization.CultureInfo.InvariantCulture);
                bottomBox.Text = newBottom.ToString(System.Globalization.CultureInfo.InvariantCulture);
                uniformBox.Text = GetNearestEdgeMargin(newLeft, newTop, newRight, newBottom)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
            finally
            {
                suppressMarginPreview = false;
            }
        }

        uniformBox.TextChanged += (_, _) => TryPreview(uniformBox);
        leftBox.TextChanged += (_, _) => TryPreview(leftBox);
        topBox.TextChanged += (_, _) => TryPreview(topBox);
        rightBox.TextChanged += (_, _) => TryPreview(rightBox);
        bottomBox.TextChanged += (_, _) => TryPreview(bottomBox);

        var dialog = new ContentDialog
        {
            XamlRoot = RootElement.XamlRoot,
            Title = localization.T("Widget.Margin.Configure"),
            Content = content,
            PrimaryButtonText = localization.T("Common.Save"),
            CloseButtonText = localization.T("Common.Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };

        // Preview semantics: every valid edit moves the widget immediately so
        // what the user sees is the final layout. Cancel therefore restores
        // the position captured when the dialog opened (single-widget mode
        // only; the batch mode is applied immediately by design).
        Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT dialogInitialRect);
        bool cancelledRestorePending = false;

        try
        {
            ContentDialogResult result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
            {
                cancelledRestorePending = true;
            }

            if (cancelledRestorePending)
            {
                Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT currentRect);
                if (currentRect.Left != dialogInitialRect.Left ||
                    currentRect.Top != dialogInitialRect.Top ||
                    currentRect.Right - currentRect.Left != dialogInitialRect.Right - dialogInitialRect.Left ||
                    currentRect.Bottom - currentRect.Top != dialogInitialRect.Bottom - dialogInitialRect.Top)
                {
                    int width = dialogInitialRect.Right - dialogInitialRect.Left;
                    int height = dialogInitialRect.Bottom - dialogInitialRect.Top;
                    Win32Helper.SetWindowPos(
                        HWnd,
                        IntPtr.Zero,
                        dialogInitialRect.Left,
                        dialogInitialRect.Top,
                        width,
                        height,
                        Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE);
                    CapturePositionAnchor(dialogInitialRect.Left, dialogInitialRect.Top, width, height);
                    UpdateConfigBoundsFromPhysical(
                        dialogInitialRect.Left,
                        dialogInitialRect.Top,
                        width,
                        height,
                        persist: true);
                    RefreshCompactPlacementAfterBoundsMove();
                }

                return;
            }

            bool perSide = modeSelection.SelectedIndex == 1;
            bool valid = perSide
                ? WidgetMarginSettings.TryParseMargin(leftBox.Text, out _) &&
                    WidgetMarginSettings.TryParseMargin(topBox.Text, out _) &&
                    WidgetMarginSettings.TryParseMargin(rightBox.Text, out _) &&
                    WidgetMarginSettings.TryParseMargin(bottomBox.Text, out _)
                : WidgetMarginSettings.TryParseMargin(uniformBox.Text, out _);
            if (!valid)
            {
                // Live-derived values can exceed the 0–200 entry range (a
                // window 350px from an edge is legitimate). Never fail
                // silently: show the same inline error the preview shows.
                validation.Text = string.Format(
                    localization.T("Widget.Margin.Invalid"),
                    WidgetMarginSettings.MinimumMarginPixels,
                    WidgetMarginSettings.MaximumMarginPixels);
                validation.Visibility = Visibility.Visible;
                return;
            }

            WidgetMarginSettings.SetModeOverride(
                Config,
                perSide ? WidgetMarginSettings.ModePerSide : WidgetMarginSettings.ModeUniform);
            SettingsService.UpdateWidget(Config);
            ApplyMarginsFromDialog(
                perSide,
                editedSides,
                uniformBox.Text,
                leftBox.Text,
                topBox.Text,
                rightBox.Text,
                bottomBox.Text,
                applyToAll.IsChecked == true);
            SyncBoxesFromLiveMargins();
        }
        catch (Exception ex)
        {
            App.Log($"[WidgetMargin] Margin dialog failed: {ex.Message}");
        }
    }


    private void ApplyMarginsFromDialog(
        bool perSide,
        HashSet<string> editedSides,
        string uniformText,
        string leftText,
        string topText,
        string rightText,
        string bottomText,
        bool applyToAll)
    {
        IReadOnlyList<RectInt32> others = App.Current?.WidgetManager
            ?.GetOtherVisibleWidgetRects(HWnd) ?? Array.Empty<RectInt32>();

        if (perSide)
        {
            var sideValues = new (string Side, string Text)[]
            {
                ("Left", leftText),
                ("Top", topText),
                ("Right", rightText),
                ("Bottom", bottomText),
            };

            foreach ((string side, string text) in sideValues)
            {
                if (!editedSides.Contains(side) ||
                    !WidgetMarginSettings.TryParseMargin(text, out int margin))
                {
                    continue;
                }

                if (applyToAll)
                {
                    App.Current?.WidgetManager?.MoveVisibleWidgets((_, bounds, othersForWidget) =>
                        ShiftSideToMargin(bounds, side, margin, othersForWidget, ResolveWorkAreaStatic(bounds)));
                }
                else
                {
                    ApplyOwnMarginToSide(side, margin, others);
                }
            }
        }
        else
        {
            if (!WidgetMarginSettings.TryParseMargin(uniformText, out int uniform))
            {
                return;
            }

            if (applyToAll)
            {
                App.Current?.WidgetManager?.MoveVisibleWidgets((_, bounds, othersForWidget) =>
                    ShiftBoundsToNearestEdge(bounds, uniform, othersForWidget, ResolveWorkAreaStatic(bounds)));
            }
            else
            {
                ApplyOwnMarginToSide("Nearest", uniform, others);
            }
        }
    }

    private void ApplyOwnMarginToSide(string side, int margin, IReadOnlyList<RectInt32> others)
    {
        if (Config.IsPositionLocked ||
            !Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT rect))
        {
            return;
        }

        var current = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        var workArea = ResolveWorkArea(current);
        RectInt32? target = side switch
        {
            "Nearest" => ShiftBoundsToNearestEdge(current, margin, others, workArea),
            "Left" => ShiftSideToMargin(current, "Left", margin, others, workArea),
            "Top" => ShiftSideToMargin(current, "Top", margin, others, workArea),
            "Right" => ShiftSideToMargin(current, "Right", margin, others, workArea),
            _ => ShiftSideToMargin(current, "Bottom", margin, others, workArea),
        };
        if (target is not RectInt32 next || (next.X == current.X && next.Y == current.Y))
        {
            return;
        }

        Win32Helper.SetWindowPos(
            HWnd,
            IntPtr.Zero,
            next.X,
            next.Y,
            next.Width,
            next.Height,
            Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE);
        CapturePositionAnchor(next.X, next.Y, next.Width, next.Height);
        UpdateConfigBoundsFromPhysical(next.X, next.Y, next.Width, next.Height, persist: true);
        RefreshCompactPlacementAfterBoundsMove();
    }

    private RectInt32 ResolveWorkArea(RectInt32 bounds)
    {
        var center = new Windows.Graphics.PointInt32(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);
        return Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            center,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
    }

    private static RectInt32 ResolveWorkAreaStatic(RectInt32 bounds)
    {
        var center = new Windows.Graphics.PointInt32(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);
        return Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            center,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
    }

    /// <summary>
    /// Per-side distances shown in the dialog: the gap to the nearest other
    /// widget edge strictly on that side, or to the work-area edge when no
    /// widget is present there. Same reference geometry as the apply path,
    /// so displayed and applied values agree.
    /// </summary>
    private (int Left, int Top, int Right, int Bottom) GetCurrentReferenceMargins()
    {
        if (!Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT rect))
        {
            return (0, 0, 0, 0);
        }

        var bounds = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        IReadOnlyList<RectInt32> others = App.Current?.WidgetManager
            ?.GetOtherVisibleWidgetRects(HWnd) ?? Array.Empty<RectInt32>();
        var workArea = ResolveWorkArea(bounds);
        return (
            Math.Max(0, bounds.X - ResolveSideBoundary(bounds, "Left", others, workArea)),
            Math.Max(0, bounds.Y - ResolveSideBoundary(bounds, "Top", others, workArea)),
            Math.Max(0, ResolveSideBoundary(bounds, "Right", others, workArea) - (bounds.X + bounds.Width)),
            Math.Max(0, ResolveSideBoundary(bounds, "Bottom", others, workArea) - (bounds.Y + bounds.Height)));
    }

    /// <summary>
    /// The reference boundary for one side: the closest other-widget edge
    /// strictly on that side (within a 8px parallel-overlap tolerance), or
    /// the work-area edge when no widget borders this window there.
    /// </summary>
    private static int ResolveSideBoundary(
        RectInt32 bounds,
        string side,
        IReadOnlyList<RectInt32> others,
        RectInt32 workArea)
    {
        int workLeft = workArea.X;
        int workTop = workArea.Y;
        int workRight = workArea.X + workArea.Width;
        int workBottom = workArea.Y + workArea.Height;
        int boundary = side switch
        {
            "Left" => workLeft,
            "Top" => workTop,
            "Right" => workRight,
            _ => workBottom,
        };

        const int parallelOverlapTolerance = 8;
        foreach (RectInt32 other in others)
        {
            int otherRight = other.X + other.Width;
            int otherBottom = other.Y + other.Height;
            switch (side)
            {
                case "Left" when otherRight <= bounds.X + parallelOverlapTolerance &&
                    other.Y < bounds.Y + bounds.Height - parallelOverlapTolerance &&
                    otherBottom > bounds.Y + parallelOverlapTolerance:
                    boundary = Math.Max(boundary, otherRight);
                    break;
                case "Right" when other.X >= bounds.X + bounds.Width - parallelOverlapTolerance &&
                    other.Y < bounds.Y + bounds.Height - parallelOverlapTolerance &&
                    otherBottom > bounds.Y + parallelOverlapTolerance:
                    boundary = Math.Min(boundary, other.X);
                    break;
                case "Top" when otherBottom <= bounds.Y + parallelOverlapTolerance &&
                    other.X < bounds.X + bounds.Width - parallelOverlapTolerance &&
                    otherRight > bounds.X + parallelOverlapTolerance:
                    boundary = Math.Max(boundary, otherBottom);
                    break;
                case "Bottom" when other.Y >= bounds.Y + bounds.Height - parallelOverlapTolerance &&
                    other.X < bounds.X + bounds.Width - parallelOverlapTolerance &&
                    otherRight > bounds.X + parallelOverlapTolerance:
                    boundary = Math.Min(boundary, other.Y);
                    break;
            }
        }

        return boundary;
    }

    private static int GetNearestEdgeMargin(int left, int top, int right, int bottom)
    {
        int min = Math.Min(Math.Min(left, top), Math.Min(right, bottom));
        return min > WidgetMarginSettings.MaximumMarginPixels
            ? WidgetMarginSettings.MaximumMarginPixels
            : min;
    }

    /// <summary>
    /// Moves the window so the requested side sits at the given distance from
    /// its nearest reference boundary: the closest other-widget edge strictly
    /// on that side, or the work-area edge when no widget borders it there.
    /// </summary>
    private static RectInt32? ShiftSideToMargin(
        RectInt32 bounds,
        string side,
        int margin,
        IReadOnlyList<RectInt32> others,
        RectInt32 workArea)
    {
        int boundary = ResolveSideBoundary(bounds, side, others, workArea);
        int newX = bounds.X;
        int newY = bounds.Y;
        switch (side)
        {
            case "Left":
                newX = boundary + margin;
                break;
            case "Top":
                newY = boundary + margin;
                break;
            case "Right":
                newX = boundary - margin - bounds.Width;
                break;
            default:
                newY = boundary - margin - bounds.Height;
                break;
        }

        if (newX == bounds.X && newY == bounds.Y)
        {
            return null;
        }

        // The margin entry is the only placement path that computes a target
        // without clamping it. Oversized widgets plus large margins can push
        // the raw target outside the work area, which the restart chain would
        // then pull back — a second, silent drift. Apply the same work-area
        // clamp the restore path uses so the applied position is final.
        return WidgetPositioningService.EnsureVisible(
            new RectInt32(newX, newY, bounds.Width, bounds.Height),
            workArea);
    }

    /// <summary>
    /// Uniform entry: snaps the window along its nearest reference boundary
    /// (other widget edge or work-area edge) so that boundary sits exactly at
    /// the requested distance.
    /// </summary>
    private static RectInt32? ShiftBoundsToNearestEdge(
        RectInt32 bounds,
        int margin,
        IReadOnlyList<RectInt32> others,
        RectInt32 workArea)
    {
        int leftBoundary = ResolveSideBoundary(bounds, "Left", others, workArea);
        int topBoundary = ResolveSideBoundary(bounds, "Top", others, workArea);
        int rightBoundary = ResolveSideBoundary(bounds, "Right", others, workArea);
        int bottomBoundary = ResolveSideBoundary(bounds, "Bottom", others, workArea);
        int left = bounds.X - leftBoundary;
        int top = bounds.Y - topBoundary;
        int right = rightBoundary - (bounds.X + bounds.Width);
        int bottom = bottomBoundary - (bounds.Y + bounds.Height);

        int min = Math.Min(Math.Min(left, top), Math.Min(right, bottom));
        int clamped = Math.Clamp(margin, WidgetMarginSettings.MinimumMarginPixels, WidgetMarginSettings.MaximumMarginPixels);
        int x = bounds.X;
        int y = bounds.Y;
        if (min == left)
        {
            x = leftBoundary + clamped;
        }
        else if (min == top)
        {
            y = topBoundary + clamped;
        }
        else if (min == right)
        {
            x = rightBoundary - clamped - bounds.Width;
        }
        else
        {
            y = bottomBoundary - clamped - bounds.Height;
        }

        if (x == bounds.X && y == bounds.Y)
        {
            return null;
        }

        // Same work-area clamp as the per-side shift above: the uniform
        // entry must not hand back a target the restart chain would move
        // again.
        return WidgetPositioningService.EnsureVisible(
            new RectInt32(x, y, bounds.Width, bounds.Height),
            workArea);
    }

    /// <summary>
    /// Unified entry: snaps the window along its nearest work-area edge so
    /// that edge sits exactly at the requested distance.
    /// </summary>
    private static RectInt32? ShiftBoundsToNearestEdge(RectInt32 bounds, int margin)
    {
        var center = new Windows.Graphics.PointInt32(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);
        RectInt32 workArea = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            center,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        int workRight = workArea.X + workArea.Width;
        int workBottom = workArea.Y + workArea.Height;

        int left = bounds.X - workArea.X;
        int top = bounds.Y - workArea.Y;
        int right = workRight - (bounds.X + bounds.Width);
        int bottom = workBottom - (bounds.Y + bounds.Height);

        int min = Math.Min(Math.Min(left, top), Math.Min(right, bottom));
        int clamped = Math.Clamp(margin, WidgetMarginSettings.MinimumMarginPixels, WidgetMarginSettings.MaximumMarginPixels);
        int x = bounds.X;
        int y = bounds.Y;
        if (min == left)
        {
            x = workArea.X + clamped;
        }
        else if (min == top)
        {
            y = workArea.Y + clamped;
        }
        else if (min == right)
        {
            x = workRight - clamped - bounds.Width;
        }
        else
        {
            y = workBottom - clamped - bounds.Height;
        }

        if (x == bounds.X && y == bounds.Y)
        {
            return null;
        }

        return new RectInt32(x, y, bounds.Width, bounds.Height);
    }
}
