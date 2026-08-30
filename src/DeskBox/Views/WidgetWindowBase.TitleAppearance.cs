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
        (int left, int top, int right, int bottom) = GetCurrentWorkAreaMargins();

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
        var perSidePanel = new StackPanel { Spacing = 8 };
        perSidePanel.Children.Add(leftBox);
        perSidePanel.Children.Add(topBox);
        perSidePanel.Children.Add(rightBox);
        perSidePanel.Children.Add(bottomBox);

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
                    GetCurrentWorkAreaMargins();
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
                (int newLeft, int newTop, int newRight, int newBottom) = GetCurrentWorkAreaMargins();
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
        if (perSide)
        {
            if (!WidgetMarginSettings.TryParseMargin(leftText, out int left) ||
                !WidgetMarginSettings.TryParseMargin(topText, out int top) ||
                !WidgetMarginSettings.TryParseMargin(rightText, out int right) ||
                !WidgetMarginSettings.TryParseMargin(bottomText, out int bottom))
            {
                return;
            }

            if (applyToAll)
            {
                App.Current?.WidgetManager?.MoveVisibleWidgets((config, bounds) =>
                    ShiftBoundsToMarginsForSides(
                        bounds,
                        editedSides,
                        left,
                        top,
                        right,
                        bottom));
            }
            else
            {
                ApplyOwnMarginDelta(true, editedSides, left, top, right, bottom);
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
                App.Current?.WidgetManager?.MoveVisibleWidgets((_, bounds) =>
                    ShiftBoundsToNearestEdge(bounds, uniform));
            }
            else
            {
                ApplyOwnMarginDelta(false, editedSides, uniform, uniform, uniform, uniform);
            }
        }
    }

    private void ApplyOwnMarginDelta(
        bool perSide,
        HashSet<string> editedSides,
        int left,
        int top,
        int right,
        int bottom)
    {
        // A position-locked widget must not be moved by the margin dialog —
        // the drag path enforces the same lock (CoordinatedMove).
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
        RectInt32? target = perSide
            ? ShiftBoundsToMarginsForSides(current, editedSides, left, top, right, bottom)
            : ShiftBoundsToNearestEdge(current, left);
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
    }

    private (int Left, int Top, int Right, int Bottom) GetCurrentWorkAreaMargins()
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
        var center = new Windows.Graphics.PointInt32(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);
        RectInt32 workArea = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            center,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        return (
            Math.Max(0, bounds.X - workArea.X),
            Math.Max(0, bounds.Y - workArea.Y),
            Math.Max(0, workArea.X + workArea.Width - (bounds.X + bounds.Width)),
            Math.Max(0, workArea.Y + workArea.Height - (bounds.Y + bounds.Height)));
    }

    private static int GetNearestEdgeMargin(int left, int top, int right, int bottom)
    {
        int min = Math.Min(Math.Min(left, top), Math.Min(right, bottom));
        return min > WidgetMarginSettings.MaximumMarginPixels
            ? WidgetMarginSettings.MaximumMarginPixels
            : min;
    }

    /// <summary>
    /// Per-side entry: moves the window so each named side sits at the
    /// requested distance from the work-area edge. Horizontal and vertical
    /// axes are independent, matching how the per-side inputs are presented.
    /// </summary>
    private static RectInt32? ShiftBoundsToMarginsForSides(
        RectInt32 bounds,
        HashSet<string> editedSides,
        int left,
        int top,
        int right,
        int bottom)
    {
        var center = new Windows.Graphics.PointInt32(
            bounds.X + bounds.Width / 2,
            bounds.Y + bounds.Height / 2);
        RectInt32 workArea = Microsoft.UI.Windowing.DisplayArea.GetFromPoint(
            center,
            Microsoft.UI.Windowing.DisplayAreaFallback.Nearest).WorkArea;
        int workRight = workArea.X + workArea.Width;
        int workBottom = workArea.Y + workArea.Height;

        int currentLeft = bounds.X - workArea.X;
        int currentTop = bounds.Y - workArea.Y;
        int currentRight = workRight - (bounds.X + bounds.Width);
        int currentBottom = workBottom - (bounds.Y + bounds.Height);

        // Only sides the user actually edited take effect. Without this gate
        // the near-side arbitration would silently swallow an edit to the far
        // side (the historical defect: typing into "left" when the window is
        // currently nearest the right edge did nothing).
        int newX = bounds.X;
        int newY = bounds.Y;
        if (editedSides.Contains("Left") && left != currentLeft)
        {
            newX = workArea.X + left;
        }
        else if (editedSides.Contains("Right") && right != currentRight)
        {
            newX = workRight - right - bounds.Width;
        }

        if (editedSides.Contains("Top") && top != currentTop)
        {
            newY = workArea.Y + top;
        }
        else if (editedSides.Contains("Bottom") && bottom != currentBottom)
        {
            newY = workBottom - bottom - bounds.Height;
        }

        if (newX == bounds.X && newY == bounds.Y)
        {
            return null;
        }

        return new RectInt32(newX, newY, bounds.Width, bounds.Height);
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
