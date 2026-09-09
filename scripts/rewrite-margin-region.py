import pathlib

p = pathlib.Path("src/DeskBox/Views/WidgetWindowBase.TitleAppearance.cs")
s = p.read_text(encoding="utf-8")
lines = s.splitlines(keepends=True)

start_line = 436  # 1-based: ApplyMarginsFromDialog
start = sum(len(l) for l in lines[: start_line - 1])

tail = s[start:]
marker = "return new RectInt32(newX, newY, bounds.Width, bounds.Height);\n    }"
idx = tail.rfind(marker)
assert idx != -1, "marker not found"
end = start + idx + len(marker)

new_block = """
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

        return new RectInt32(newX, newY, bounds.Width, bounds.Height);
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

        return new RectInt32(x, y, bounds.Width, bounds.Height);
    }"""

s = s[:start] + new_block + s[end:]
p.write_text(s, encoding="utf-8", newline="\n")
print("rewritten margin region")
