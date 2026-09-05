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
        WidgetMarginEdge[] edges = GetCurrentReferenceMarginEdges();
        int left = edges[0].Distance;
        int top = edges[1].Distance;
        int right = edges[2].Distance;
        int bottom = edges[3].Distance;

        // Per-side is the default now. The four distances are what users
        // actually reason about, and the reference for each one is shown next to
        // it because it can legitimately differ per side (an icon on the left, a
        // widget above, nothing on the right).
        string entryMode = WidgetMarginSettings.GetModeOverride(Config) ??
            WidgetMarginSettings.ModePerSide;

        var modeSelection = new RadioButtons
        {
            SelectedIndex = entryMode == WidgetMarginSettings.ModeUniform ? 0 : 1
        };
        modeSelection.Items.Add(localization.T("Widget.Margin.Uniform"));
        modeSelection.Items.Add(localization.T("Widget.Margin.PerSide"));

        var uniformBox = new TextBox
        {
            Header = localization.T("Widget.Margin.UniformValue"),
            Text = GetNearestEdgeMargin(left, top, right, bottom)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        var leftBox = new TextBox { Text = left.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var topBox = new TextBox { Text = top.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var rightBox = new TextBox { Text = right.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        var bottomBox = new TextBox { Text = bottom.ToString(System.Globalization.CultureInfo.InvariantCulture) };
        void ApplySideHeaders(WidgetMarginEdge[] sideEdges)
        {
            leftBox.Header = BuildMarginSideHeader("Widget.Margin.Left", sideEdges[0].Kind);
            topBox.Header = BuildMarginSideHeader("Widget.Margin.Top", sideEdges[1].Kind);
            rightBox.Header = BuildMarginSideHeader("Widget.Margin.Right", sideEdges[2].Kind);
            bottomBox.Header = BuildMarginSideHeader("Widget.Margin.Bottom", sideEdges[3].Kind);
        }

        ApplySideHeaders(edges);
        var referenceHint = new TextBlock
        {
            Text = localization.T("Widget.Margin.ReferenceHint"),
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75
        };
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

        var content = new StackPanel { Spacing = 12, MinWidth = 300 };
        content.Children.Add(modeSelection);
        content.Children.Add(uniformBox);
        content.Children.Add(perSidePanel);
        content.Children.Add(referenceHint);
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
                WidgetMarginEdge[] liveEdges = GetCurrentReferenceMarginEdges();
                int newLeft = liveEdges[0].Distance;
                int newTop = liveEdges[1].Distance;
                int newRight = liveEdges[2].Distance;
                int newBottom = liveEdges[3].Distance;
                leftBox.Text = newLeft.ToString(System.Globalization.CultureInfo.InvariantCulture);
                topBox.Text = newTop.ToString(System.Globalization.CultureInfo.InvariantCulture);
                rightBox.Text = newRight.ToString(System.Globalization.CultureInfo.InvariantCulture);
                bottomBox.Text = newBottom.ToString(System.Globalization.CultureInfo.InvariantCulture);
                uniformBox.Text = GetNearestEdgeMargin(newLeft, newTop, newRight, newBottom)
                    .ToString(System.Globalization.CultureInfo.InvariantCulture);
                // Moving the widget can change which object each side is
                // measured against, so the headers are re-resolved with it.
                ApplySideHeaders(liveEdges);
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
        IReadOnlyList<WidgetMarginNeighbour> neighbours = CollectMarginNeighbours(
            App.Current?.WidgetManager?.GetOtherVisibleWidgetRects(HWnd) ??
                Array.Empty<RectInt32>());
        int tolerance = ResolveMarginOverlapTolerance();

        if (perSide)
        {
            var sideValues = new (WidgetMarginSide Side, string Key, string Text)[]
            {
                (WidgetMarginSide.Left, "Left", leftText),
                (WidgetMarginSide.Top, "Top", topText),
                (WidgetMarginSide.Right, "Right", rightText),
                (WidgetMarginSide.Bottom, "Bottom", bottomText),
            };

            foreach ((WidgetMarginSide side, string key, string text) in sideValues)
            {
                if (!editedSides.Contains(key) ||
                    !WidgetMarginSettings.TryParseMargin(text, out int margin))
                {
                    continue;
                }

                if (applyToAll)
                {
                    App.Current?.WidgetManager?.MoveVisibleWidgets((_, bounds, othersForWidget) =>
                        ShiftSideToMargin(
                            bounds,
                            side,
                            margin,
                            CollectMarginNeighbours(othersForWidget),
                            ResolveWorkAreaStatic(bounds),
                            tolerance));
                }
                else
                {
                    ApplyOwnMarginToSide(side, margin, neighbours, tolerance);
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
                    ShiftBoundsToNearestEdge(
                        bounds,
                        uniform,
                        CollectMarginNeighbours(othersForWidget),
                        ResolveWorkAreaStatic(bounds),
                        tolerance));
            }
            else
            {
                ApplyOwnNearestMargin(uniform, neighbours, tolerance);
            }
        }
    }

    private void ApplyOwnMarginToSide(
        WidgetMarginSide side,
        int margin,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        int parallelOverlapTolerance)
    {
        ApplyOwnMarginTarget((current, workArea) => ShiftSideToMargin(
            current,
            side,
            margin,
            neighbours,
            workArea,
            parallelOverlapTolerance));
    }

    private void ApplyOwnNearestMargin(
        int margin,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        int parallelOverlapTolerance)
    {
        ApplyOwnMarginTarget((current, workArea) => ShiftBoundsToNearestEdge(
            current,
            margin,
            neighbours,
            workArea,
            parallelOverlapTolerance));
    }

    private void ApplyOwnMarginTarget(
        Func<RectInt32, RectInt32, RectInt32?> resolveTarget)
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
        RectInt32 workArea = ResolveWorkArea(current);
        if (resolveTarget(current, workArea) is not RectInt32 next ||
            (next.X == current.X && next.Y == current.Y))
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
    /// <summary>
    /// Every candidate a margin can be measured against, in physical pixels:
    /// the other widgets plus the desktop icons Explorer is showing. The user
    /// judges the gap to whatever is actually beside the widget, so an icon or
    /// folder is as valid a reference as another widget; the work area is only
    /// the fallback for a side with nothing next to it.
    /// </summary>
    private static IReadOnlyList<WidgetMarginNeighbour> CollectMarginNeighbours(
        IReadOnlyList<RectInt32> widgetRects)
    {
        IReadOnlyList<RectInt32> iconRects = DesktopIconGeometryService.GetIconRects();
        var neighbours = new List<WidgetMarginNeighbour>(
            widgetRects.Count + iconRects.Count);
        foreach (RectInt32 rect in widgetRects)
        {
            neighbours.Add(new WidgetMarginNeighbour(
                rect,
                WidgetMarginReferenceKind.Widget));
        }

        foreach (RectInt32 rect in iconRects)
        {
            neighbours.Add(new WidgetMarginNeighbour(
                rect,
                WidgetMarginReferenceKind.DesktopIcon));
        }

        return neighbours;
    }

    private int ResolveMarginOverlapTolerance()
    {
        return WidgetMarginReferenceCalculator.ResolveParallelOverlapTolerance(
            Win32Helper.GetDpiScaleForWindow(HWnd, RootElement.XamlRoot));
    }

    private (int Left, int Top, int Right, int Bottom) GetCurrentReferenceMargins()
    {
        WidgetMarginEdge[] edges = GetCurrentReferenceMarginEdges();
        return (
            edges[0].Distance,
            edges[1].Distance,
            edges[2].Distance,
            edges[3].Distance);
    }

    /// <summary>
    /// Left, Top, Right, Bottom distances together with what each one is
    /// measured against, derived from the live window rect on every call.
    /// </summary>
    private WidgetMarginEdge[] GetCurrentReferenceMarginEdges()
    {
        if (!Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT rect))
        {
            return
            [
                new WidgetMarginEdge(0, WidgetMarginReferenceKind.WorkArea),
                new WidgetMarginEdge(0, WidgetMarginReferenceKind.WorkArea),
                new WidgetMarginEdge(0, WidgetMarginReferenceKind.WorkArea),
                new WidgetMarginEdge(0, WidgetMarginReferenceKind.WorkArea)
            ];
        }

        var bounds = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        IReadOnlyList<WidgetMarginNeighbour> neighbours = CollectMarginNeighbours(
            App.Current?.WidgetManager?.GetOtherVisibleWidgetRects(HWnd) ??
                Array.Empty<RectInt32>());
        RectInt32 workArea = ResolveWorkArea(bounds);
        int tolerance = ResolveMarginOverlapTolerance();
        return
        [
            WidgetMarginReferenceCalculator.ResolveMargin(
                bounds, WidgetMarginSide.Left, neighbours, workArea, tolerance),
            WidgetMarginReferenceCalculator.ResolveMargin(
                bounds, WidgetMarginSide.Top, neighbours, workArea, tolerance),
            WidgetMarginReferenceCalculator.ResolveMargin(
                bounds, WidgetMarginSide.Right, neighbours, workArea, tolerance),
            WidgetMarginReferenceCalculator.ResolveMargin(
                bounds, WidgetMarginSide.Bottom, neighbours, workArea, tolerance)
        ];
    }

    private static string DescribeMarginReference(WidgetMarginReferenceKind kind)
    {
        var localization = App.Current.LocalizationService;
        return kind switch
        {
            WidgetMarginReferenceKind.Widget =>
                localization.T("Widget.Margin.Reference.Widget"),
            WidgetMarginReferenceKind.DesktopIcon =>
                localization.T("Widget.Margin.Reference.Icon"),
            _ => localization.T("Widget.Margin.Reference.Screen")
        };
    }

    private static string BuildMarginSideHeader(
        string sideKey,
        WidgetMarginReferenceKind kind)
    {
        var localization = App.Current.LocalizationService;
        return string.Format(
            localization.T("Widget.Margin.ReferenceFormat"),
            localization.T(sideKey),
            DescribeMarginReference(kind));
    }

    /// <summary>
    /// The reference boundary for one side, delegated to the shared pure
    /// calculator so the dialog, the single-widget apply path and the batch
    /// apply path cannot drift apart.
    /// </summary>
    private static int ResolveSideBoundary(
        RectInt32 bounds,
        WidgetMarginSide side,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        RectInt32 workArea,
        int parallelOverlapTolerance)
    {
        return WidgetMarginReferenceCalculator.ResolveSide(
            bounds,
            side,
            neighbours,
            workArea,
            parallelOverlapTolerance).Boundary;
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
    /// its nearest reference boundary: the closest widget or desktop icon edge
    /// on that side, or the work-area edge when nothing borders it there.
    /// </summary>
    private static RectInt32? ShiftSideToMargin(
        RectInt32 bounds,
        WidgetMarginSide side,
        int margin,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        RectInt32 workArea,
        int parallelOverlapTolerance)
    {
        PointInt32? origin = WidgetMarginReferenceCalculator.ResolveShiftedOrigin(
            bounds,
            side,
            margin,
            neighbours,
            workArea,
            parallelOverlapTolerance);
        if (origin is not PointInt32 next)
        {
            return null;
        }

        // The margin entry is the only placement path that computes a target
        // without clamping it. Oversized widgets plus large margins can push
        // the raw target outside the work area, which the restart chain would
        // then pull back — a second, silent drift. Apply the same work-area
        // clamp the restore path uses so the applied position is final.
        return WidgetPositioningService.EnsureVisible(
            new RectInt32(next.X, next.Y, bounds.Width, bounds.Height),
            workArea);
    }

    /// <summary>
    /// Uniform entry: snaps the window along whichever side is currently
    /// closest to its reference so that gap becomes the requested distance.
    /// </summary>
    private static RectInt32? ShiftBoundsToNearestEdge(
        RectInt32 bounds,
        int margin,
        IReadOnlyList<WidgetMarginNeighbour> neighbours,
        RectInt32 workArea,
        int parallelOverlapTolerance)
    {
        WidgetMarginSide nearestSide = WidgetMarginSide.Left;
        int nearestDistance = int.MaxValue;
        foreach (WidgetMarginSide side in MarginSides)
        {
            int distance = WidgetMarginReferenceCalculator.ResolveMargin(
                bounds,
                side,
                neighbours,
                workArea,
                parallelOverlapTolerance).Distance;
            if (distance < nearestDistance)
            {
                nearestDistance = distance;
                nearestSide = side;
            }
        }

        int clamped = Math.Clamp(
            margin,
            WidgetMarginSettings.MinimumMarginPixels,
            WidgetMarginSettings.MaximumMarginPixels);
        return ShiftSideToMargin(
            bounds,
            nearestSide,
            clamped,
            neighbours,
            workArea,
            parallelOverlapTolerance);
    }

    private static readonly WidgetMarginSide[] MarginSides =
    [
        WidgetMarginSide.Left,
        WidgetMarginSide.Top,
        WidgetMarginSide.Right,
        WidgetMarginSide.Bottom
    ];
}
