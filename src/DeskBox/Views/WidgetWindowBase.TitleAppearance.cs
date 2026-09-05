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

    /// <summary>
    /// Room the margin editor asks for: two side columns with their reference
    /// captions, the mode selector, the hint line and the batch checkbox.
    /// </summary>
    private const double MarginEditorContentWidthDips = 380;
    private const double MarginEditorContentHeightDips = 300;

    /// <summary>
    /// Idle time after the last keystroke before the margin preview moves the
    /// widget. Long enough to swallow a typed multi-digit value, short enough to
    /// still feel like a live preview.
    /// </summary>
    private const int MarginPreviewDebounceMilliseconds = 160;

    private async Task ShowMarginDialogAsync()
    {
        if (RootElement.XamlRoot is null)
        {
            return;
        }

        var localization = App.Current.LocalizationService;
        // The editor gets its own window because the widget cannot host it: a
        // typical widget is ~313x326 physical pixels, and a ContentDialog in that
        // XamlRoot was clipped by the host window — the Bottom/Right row was cut
        // off entirely, which made the entry look Top/Left only. The budget below
        // therefore comes from the monitor work area.
        WidgetDialogViewport viewport = ResolveToolDialogViewport(
            MarginEditorContentWidthDips,
            MarginEditorContentHeightDips);
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
            // Side-by-side choices instead of a stacked pair: the vertical space
            // this frees is what the four side rows need. A host too narrow for
            // two columns falls back to the stacked layout.
            MaxColumns = viewport.PrefersSingleColumn ? 1 : 2,
            SelectedIndex = entryMode == WidgetMarginSettings.ModeUniform ? 0 : 1
        };
        // Wrapping labels: the mode names are long in several languages and a
        // plain string item would be clipped rather than wrapped.
        modeSelection.Items.Add(new TextBlock
        {
            Text = localization.T("Widget.Margin.Uniform"),
            TextWrapping = TextWrapping.Wrap
        });
        modeSelection.Items.Add(new TextBlock
        {
            Text = localization.T("Widget.Margin.PerSide"),
            TextWrapping = TextWrapping.Wrap
        });

        var uniformBox = new TextBox
        {
            Header = localization.T("Widget.Margin.UniformValue"),
            Text = GetNearestEdgeMargin(left, top, right, bottom)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
        };

        // Index order matches GetCurrentReferenceMarginEdges: Left, Top, Right, Bottom.
        string[] sideKeys =
        [
            "Widget.Margin.Left",
            "Widget.Margin.Top",
            "Widget.Margin.Right",
            "Widget.Margin.Bottom"
        ];
        int[] sideDistances = [left, top, right, bottom];
        var sideBoxes = new TextBox[sideKeys.Length];
        var sideCaptions = new TextBlock[sideKeys.Length];
        var sideCells = new StackPanel[sideKeys.Length];
        for (int index = 0; index < sideKeys.Length; index++)
        {
            sideBoxes[index] = new TextBox
            {
                // The side name alone stays on one line at any dialog width; the
                // reference object rides in the caption under the box, so a long
                // reference name can no longer double the row height.
                Header = localization.T(sideKeys[index]),
                Text = sideDistances[index].ToString(System.Globalization.CultureInfo.InvariantCulture),
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            sideCaptions[index] = new TextBlock
            {
                FontSize = 11,
                Opacity = 0.7,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            sideCells[index] = new StackPanel { Spacing = 2 };
            sideCells[index].Children.Add(sideBoxes[index]);
            sideCells[index].Children.Add(sideCaptions[index]);
        }

        TextBox leftBox = sideBoxes[0];
        TextBox topBox = sideBoxes[1];
        TextBox rightBox = sideBoxes[2];
        TextBox bottomBox = sideBoxes[3];

        void ApplySideReferences(WidgetMarginEdge[] sideEdges)
        {
            for (int index = 0; index < sideCells.Length; index++)
            {
                WidgetMarginReferenceKind kind = sideEdges[index].Kind;
                sideCaptions[index].Text = DescribeMarginReference(kind);
                // The full "side · reference" sentence stays on hover, which is
                // what an ellipsized caption in a narrow host falls back to.
                ToolTipService.SetToolTip(
                    sideCells[index],
                    BuildMarginSideHeader(sideKeys[index], kind));
            }
        }

        ApplySideReferences(edges);
        var referenceHint = new TextBlock
        {
            Text = localization.T("Widget.Margin.ReferenceHint"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75,
            // On a short host the four inputs win the space: each side already
            // names its own reference object, and the full sentence stays in the
            // per-side tooltips.
            Visibility = viewport.PrefersCompactText ? Visibility.Collapsed : Visibility.Visible
        };
        // All four sides have to be reachable in one pass: two columns pair them
        // spatially (the vertical pair, then the horizontal pair) and a host too
        // narrow for that stacks them into one scrollable column instead of
        // pushing the trailing column past the window edge.
        bool singleColumn = viewport.PrefersSingleColumn;
        var perSidePanel = new Grid { ColumnSpacing = 12, RowSpacing = 10 };
        int rowCount = singleColumn ? sideCells.Length : 2;
        int columnCount = singleColumn ? 1 : 2;
        for (int row = 0; row < rowCount; row++)
        {
            // Auto rows: a star row would divide the dialog's own height budget
            // and squeeze the second pair into an unreadable sliver.
            perSidePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        }

        for (int column = 0; column < columnCount; column++)
        {
            perSidePanel.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        int[] sidePlacementOrder = [1, 3, 0, 2]; // Top, Bottom, Left, Right
        for (int slot = 0; slot < sidePlacementOrder.Length; slot++)
        {
            StackPanel cell = sideCells[sidePlacementOrder[slot]];
            Grid.SetRow(cell, singleColumn ? slot : slot % 2);
            Grid.SetColumn(cell, singleColumn ? 0 : slot / 2);
            perSidePanel.Children.Add(cell);
        }

        var applyToAll = new CheckBox
        {
            // Wrapping content: the batch label is a full sentence in most
            // languages and would otherwise be cut off in a narrow dialog.
            Content = new TextBlock
            {
                Text = localization.T("Widget.Margin.ApplyToAll"),
                TextWrapping = TextWrapping.Wrap
            }
        };
        var validation = new TextBlock
        {
            Foreground = new SolidColorBrush(Microsoft.UI.Colors.OrangeRed),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var content = new StackPanel { Spacing = 12 };
        content.Children.Add(modeSelection);
        content.Children.Add(uniformBox);
        content.Children.Add(perSidePanel);
        content.Children.Add(referenceHint);
        content.Children.Add(applyToAll);
        content.Children.Add(validation);

        // WinUI raises TextChanged for programmatic writes as well, and the event
        // can arrive after the write returns — a plain "suppress" flag therefore
        // leaks: the live-sync writes came back looking like user edits, marked
        // their side as edited and moved the widget to satisfy a value nobody
        // typed. Every value the editor writes itself is remembered so the echo
        // can be recognised whenever it arrives.
        var programmaticEntries = new Dictionary<TextBox, string>();

        void WriteEntry(TextBox box, string text)
        {
            programmaticEntries[box] = text;
            box.Text = text;
        }

        bool IsProgrammaticEcho(TextBox source)
        {
            if (programmaticEntries.TryGetValue(source, out string? written) &&
                string.Equals(written, source.Text, StringComparison.Ordinal))
            {
                return true;
            }

            programmaticEntries.Remove(source);
            return false;
        }

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
                WriteEntry(
                    uniformBox,
                    GetNearestEdgeMargin(liveLeft, liveTop, liveRight, liveBottom)
                        .ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        UpdateModeVisibility();
        modeSelection.SelectionChanged += (_, _) => UpdateModeVisibility();

        // Programmatic .Text writes (the live-sync below) must not re-run the
        // preview/apply loop: out-of-range live margins (>200px) would surface
        // a bogus validation error and re-entry would fight the user's edits.
        bool suppressMarginPreview = false;
        var editedSides = new HashSet<string>(StringComparer.Ordinal);

        // Typing is one edit, not one per digit. Applying every keystroke moved
        // the widget for the leading "1" of "150", persisted settings each time,
        // and the layout burst could swallow the next keystroke — so the preview
        // runs once the user pauses.
        Microsoft.UI.Dispatching.DispatcherQueueTimer previewTimer = DispatcherQueue.CreateTimer();
        previewTimer.Interval = TimeSpan.FromMilliseconds(MarginPreviewDebounceMilliseconds);
        previewTimer.IsRepeating = false;
        TextBox? pendingPreviewSource = null;
        previewTimer.Tick += (_, _) =>
        {
            TextBox? source = pendingPreviewSource;
            pendingPreviewSource = null;
            if (source is not null)
            {
                ApplyPreview(source);
            }
        };

        void TryPreview(TextBox source)
        {
            if (suppressMarginPreview || IsProgrammaticEcho(source))
            {
                return;
            }

            validation.Visibility = Visibility.Collapsed;
            if (!WidgetMarginSettings.TryParseMargin(source.Text, out _))
            {
                validation.Text = string.Format(
                    localization.T("Widget.Margin.Invalid"),
                    WidgetMarginSettings.MinimumMarginPixels,
                    WidgetMarginSettings.MaximumMarginPixels);
                validation.Visibility = Visibility.Visible;
                previewTimer.Stop();
                pendingPreviewSource = null;
                return;
            }

            // The edited side is recorded immediately: Save must honour it even
            // when the user commits before the debounce elapses.
            if (ReferenceEquals(source, leftBox)) editedSides.Add("Left");
            else if (ReferenceEquals(source, topBox)) editedSides.Add("Top");
            else if (ReferenceEquals(source, rightBox)) editedSides.Add("Right");
            else if (ReferenceEquals(source, bottomBox)) editedSides.Add("Bottom");

            pendingPreviewSource = source;
            previewTimer.Stop();
            previewTimer.Start();
        }

        void ApplyPreview(TextBox source)
        {
            ApplyMarginsFromDialog(
                modeSelection.SelectedIndex == 1,
                editedSides,
                uniformBox.Text,
                leftBox.Text,
                topBox.Text,
                rightBox.Text,
                bottomBox.Text,
                applyToAll.IsChecked == true);

            // The move changes the gap on every other side too, so the
            // untouched boxes and their reference captions are re-read from the
            // live geometry instead of showing pre-move numbers.
            SyncBoxesFromLiveMargins(source);
        }

        /// <summary>
        /// Rewrites the entry boxes from the live geometry. The box the user is
        /// typing in is skipped: a preview move can land on a clamped target, and
        /// rewriting the focused text under the caret would fight the edit.
        /// </summary>
        void SyncBoxesFromLiveMargins(TextBox? focused = null)
        {
            suppressMarginPreview = true;
            try
            {
                WidgetMarginEdge[] liveEdges = GetCurrentReferenceMarginEdges();
                for (int index = 0; index < sideBoxes.Length; index++)
                {
                    if (ReferenceEquals(sideBoxes[index], focused))
                    {
                        continue;
                    }

                    WriteEntry(
                        sideBoxes[index],
                        liveEdges[index].Distance
                            .ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                if (!ReferenceEquals(uniformBox, focused))
                {
                    WriteEntry(
                        uniformBox,
                        GetNearestEdgeMargin(
                                liveEdges[0].Distance,
                                liveEdges[1].Distance,
                                liveEdges[2].Distance,
                                liveEdges[3].Distance)
                            .ToString(System.Globalization.CultureInfo.InvariantCulture));
                }

                // Moving the widget can change which object each side is
                // measured against, so the captions are re-resolved with it.
                ApplySideReferences(liveEdges);
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

        // Preview semantics: every valid edit moves the widget immediately so
        // what the user sees is the final layout. Cancel therefore restores
        // the position captured when the dialog opened (single-widget mode
        // only; the batch mode is applied immediately by design).
        Win32Helper.GetWindowRect(HWnd, out Win32Helper.RECT dialogInitialRect);
        bool cancelledRestorePending = false;

        try
        {
            bool saved = await ShowToolDialogAsync(
                localization.T("Widget.Margin.Configure"),
                content,
                localization.T("Common.Save"),
                localization.T("Common.Cancel"),
                viewport);
            // A debounced preview must never fire after the editor is gone: on
            // cancel it would move the widget again right after the restore.
            previewTimer.Stop();
            pendingPreviewSource = null;
            if (!saved)
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

        var live = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        // Margins describe the resting layout, so the target is resolved for the
        // resting rect and the live window - which may be a hover expansion
        // covering its lower neighbours - moves by exactly the same delta.
        RectInt32 subject = ResolveMarginSubjectBounds(live);
        RectInt32 workArea = ResolveWorkArea(subject);
        if (resolveTarget(subject, workArea) is not RectInt32 target ||
            (target.X == subject.X && target.Y == subject.Y))
        {
            return;
        }

        int nextX = live.X + (target.X - subject.X);
        int nextY = live.Y + (target.Y - subject.Y);
        Win32Helper.SetWindowPos(
            HWnd,
            IntPtr.Zero,
            nextX,
            nextY,
            live.Width,
            live.Height,
            Win32Helper.SWP_NOZORDER | Win32Helper.SWP_NOACTIVATE);
        CapturePositionAnchor(nextX, nextY, live.Width, live.Height);
        UpdateConfigBoundsFromPhysical(nextX, nextY, live.Width, live.Height, persist: true);
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

    /// <summary>
    /// The rectangle margins describe: the widget's resting geometry.
    /// <para>
    /// A capsule has to be hover-expanded before its title bar (and this menu)
    /// exists, and that temporary rectangle covers the capsules below it — so
    /// measuring the live window reported "screen edge" for a side that clearly
    /// has a widget next to it at rest, and applying a value moved the widget
    /// against a boundary the user never saw. The capsule the widget returns to is
    /// therefore the subject whenever the pointer is only peeking.
    /// </para>
    /// </summary>
    private RectInt32 ResolveMarginSubjectBounds(RectInt32 liveBounds)
    {
        if (!RestsCollapsed || IsCompactBoundsStateActive)
        {
            return liveBounds;
        }

        return GetCompactBounds(liveBounds);
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
    /// measured against, derived from the widget's resting rect on every call.
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

        var live = new RectInt32(
            rect.Left,
            rect.Top,
            Math.Max(1, rect.Right - rect.Left),
            Math.Max(1, rect.Bottom - rect.Top));
        RectInt32 bounds = ResolveMarginSubjectBounds(live);
        IReadOnlyList<RectInt32> widgetRects =
            App.Current?.WidgetManager?.GetOtherVisibleWidgetRects(HWnd) ??
                Array.Empty<RectInt32>();
        IReadOnlyList<WidgetMarginNeighbour> neighbours = CollectMarginNeighbours(widgetRects);
        RectInt32 workArea = ResolveWorkArea(bounds);
        int tolerance = ResolveMarginOverlapTolerance();
        WidgetMarginEdge[] edges =
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

        // The reference each side resolved to is the whole point of this entry, so
        // it is traceable: a side reported against the work area is either really
        // free or a sign that the subject rect covered its neighbour.
        App.LogVerbose(
            $"[WidgetMargin] Reference resolve hwnd=0x{HWnd.ToInt64():X} " +
            $"subject={bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height} " +
            $"live={live.X},{live.Y} {live.Width}x{live.Height} " +
            $"widgets={widgetRects.Count} neighbours={neighbours.Count} tolerance={tolerance} " +
            $"left={edges[0].Distance}/{edges[0].Kind} top={edges[1].Distance}/{edges[1].Kind} " +
            $"right={edges[2].Distance}/{edges[2].Kind} bottom={edges[3].Distance}/{edges[3].Kind}");
        return edges;
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
