using DeskBox.Helpers;
using DeskBox.Platform;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.UI;
using WinRT.Interop;

namespace DeskBox.Views;

/// <summary>
/// A click-through, non-activating placement silhouette used while a member is
/// dragged out of a widget group. Native positioning keeps it responsive while
/// OLE owns the UI thread's modal drag loop.
/// </summary>
internal sealed class WidgetDetachPlacementPreviewWindow : IDisposable
{
    private const byte TrackingOpacity = 220;
    private const byte CommittedOpacity = 238;
    private readonly object _gate = new();
    private readonly Window _window;
    private readonly AppWindow _appWindow;
    private readonly IntPtr _hWnd;
    private readonly Border _surfaceBorder;
    private readonly Border _badgeBorder;
    private readonly TextBlock _captionTextBlock;
    private RectInt32 _lastBounds;
    private byte _opacity = TrackingOpacity;
    private int _animationGeneration;
    private bool _hasBounds;
    private bool _visible;
    private bool _closed;

    public WidgetDetachPlacementPreviewWindow(string caption, double cornerRadius)
    {
        // The placement silhouette reports where the widget would land, which
        // is a drag state, so its outline stays neutral instead of the accent.
        Color tone = ResolveNeutralTone();
        double surfaceRadius = Math.Clamp(cornerRadius, 0, 32);
        var root = new Grid();
        _surfaceBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(
                0x18,
                tone.R,
                tone.G,
                tone.B)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0xD8,
                tone.R,
                tone.G,
                tone.B)),
            // Keep a quiet landing surface and one bottom edge only. The
            // previous full 2px outline made the detached preview read like a
            // warning/error state and competed with the corner badge.
            BorderThickness = new Thickness(0, 0, 0, 2),
            CornerRadius = new CornerRadius(surfaceRadius)
        };
        _captionTextBlock = new TextBlock
        {
            Text = caption,
            MaxWidth = 260,
            Foreground = new SolidColorBrush(Colors.White),
            FontSize = 12,
            FontWeight = Microsoft.UI.Text.FontWeights.Normal,
            MaxLines = 1,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center
        };
        _badgeBorder = new Border
        {
            Margin = new Thickness(10, 0, 10, 8),
            Padding = new Thickness(8, 3, 8, 4),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Background = new SolidColorBrush(Color.FromArgb(0xE8, 28, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(
                0xE8,
                tone.R,
                tone.G,
                tone.B)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(Math.Clamp(surfaceRadius * 0.45, 2, 4)),
            Child = _captionTextBlock
        };
        root.Children.Add(_surfaceBorder);
        root.Children.Add(_badgeBorder);
        _window = new Window
        {
            Content = root
        };

        _hWnd = WindowNative.GetWindowHandle(_window);
        Microsoft.UI.WindowId windowId =
            Win32Interop.GetWindowIdFromWindow(_hWnd);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        WindowShellState.TryHideFromSwitchers(_appWindow);
        WindowShellState.TryApplyBorderlessOverlappedPresenter(_appWindow);

        int extendedStyle = Win32Helper.GetWindowLong(
            _hWnd,
            Win32Helper.GWL_EXSTYLE);
        extendedStyle |= Win32Helper.WS_EX_TOOLWINDOW |
                         Win32Helper.WS_EX_NOACTIVATE |
                         Win32Helper.WS_EX_TRANSPARENT |
                         Win32Helper.WS_EX_LAYERED |
                         Win32Helper.WS_EX_TOPMOST;
        _ = Win32Helper.SetWindowLongPtr(
            _hWnd,
            Win32Helper.GWL_EXSTYLE,
            new IntPtr(extendedStyle));
        int style = Win32Helper.GetWindowLong(_hWnd, Win32Helper.GWL_STYLE);
        style &= ~(Win32Helper.WS_CAPTION |
                   Win32Helper.WS_BORDER |
                   Win32Helper.WS_DLGFRAME |
                   Win32Helper.WS_THICKFRAME);
        _ = Win32Helper.SetWindowLong(_hWnd, Win32Helper.GWL_STYLE, style);
        _ = Win32Helper.SetLayeredWindowAttributes(
            _hWnd,
            0,
            TrackingOpacity,
            Win32Helper.LWA_ALPHA);
        Win32Helper.SetWindowBorderColor(
            _hWnd,
            unchecked((int)0xFFFFFFFE));
        int cornerPreference = 2;
        _ = Win32Helper.TrySetDwmWindowAttribute(
            _hWnd,
            Win32Helper.DWMWA_WINDOW_CORNER_PREFERENCE,
            ref cornerPreference);
    }

    public void BeginTracking(string caption, double cornerRadius)
    {
        PendingNativeWork work;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _animationGeneration++;
            ApplyAppearance(caption, cornerRadius);
            work = PlanHide();
            if (PlanOpacity(TrackingOpacity) is byte value)
            {
                work = work with { Opacity = value };
            }

            _hasBounds = false;
        }

        work.Invoke(_hWnd);
    }

    public void Update(RectInt32 bounds, bool visible)
    {
        PendingNativeWork work;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            RectInt32 normalized = NormalizeBounds(bounds);
            if (!visible)
            {
                _lastBounds = normalized;
                _hasBounds = true;
                work = PlanHide();
            }
            else
            {
                work = PlanShow(normalized);
            }
        }

        work.Invoke(_hWnd);
    }

    public void MarkCommitted(RectInt32 bounds)
    {
        PendingNativeWork work;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            work = PlanShow(NormalizeBounds(new RectInt32(
                bounds.X - 2,
                bounds.Y - 2,
                bounds.Width + 4,
                bounds.Height + 4)));
            if (PlanOpacity(CommittedOpacity) is byte value)
            {
                work = work with { Opacity = value };
            }
        }

        work.Invoke(_hWnd);
    }

    public async Task FadeOutAndHideAsync()
    {
        int generation;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            generation = ++_animationGeneration;
        }

        foreach (byte opacity in new byte[] { 176, 118, 58, 16 })
        {
            byte? planned;
            lock (_gate)
            {
                if (_closed || generation != _animationGeneration)
                {
                    return;
                }

                planned = PlanOpacity(opacity);
            }

            if (planned is byte value)
            {
                _ = Win32Helper.SetLayeredWindowAttributes(
                    _hWnd,
                    0,
                    value,
                    Win32Helper.LWA_ALPHA);
            }

            await Task.Delay(28);
        }

        PendingNativeWork work;
        lock (_gate)
        {
            if (_closed || generation != _animationGeneration)
            {
                return;
            }

            work = PlanHide();
        }

        work.Invoke(_hWnd);
    }

    public void Hide()
    {
        PendingNativeWork work;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _animationGeneration++;
            work = PlanHide();
        }

        work.Invoke(_hWnd);
    }

    public void Dispose()
    {
        PendingNativeWork work;
        lock (_gate)
        {
            if (_closed)
            {
                return;
            }

            _closed = true;
            _animationGeneration++;
            work = PlanHide();
        }

        work.Invoke(_hWnd);
        _window.Close();
    }

    /// <summary>
    /// DEF-071: the native window operations this silhouette needs —
    /// SetWindowPos, ShowWindow, SetLayeredWindowAttributes — synchronously
    /// pump messages on the HWND's owning UI thread. Running them while a
    /// UI-thread caller holds <c>_gate</c> (Hide, MarkCommitted,
    /// FadeOutAndHideAsync, Dispose) deadlocked both threads whenever the
    /// 16 ms tracking poll on the pool thread was mid-call: the UI thread
    /// waited for the lock while the pool thread waited for the UI pump.
    /// Mutators now only PLAN the transition under the lock — swapping the
    /// small guarded state snapshot (_lastBounds, _visible, _opacity,
    /// generation) — and apply the queued Win32 calls after releasing it.
    /// The plan encodes exactly the same show/move/hide decisions the former
    /// in-lock helpers made, so tracking cadence and visuals are unchanged.
    /// </summary>
    private readonly record struct PendingNativeWork(
        byte? Opacity,
        RectInt32? ShowBounds,
        RectInt32? MoveBounds,
        bool HideWindow)
    {
        public static PendingNativeWork None => default;

        /// <summary>Runs the planned transitions. Must be called OUTSIDE _gate.</summary>
        public void Invoke(IntPtr hWnd)
        {
            if (Opacity is byte opacity)
            {
                _ = Win32Helper.SetLayeredWindowAttributes(
                    hWnd,
                    0,
                    opacity,
                    Win32Helper.LWA_ALPHA);
            }

            if (ShowBounds is RectInt32 show)
            {
                _ = Win32Helper.SetWindowPos(
                    hWnd,
                    Win32Helper.HWND_TOPMOST,
                    show.X,
                    show.Y,
                    show.Width,
                    show.Height,
                    Win32Helper.SWP_NOACTIVATE |
                    Win32Helper.SWP_SHOWWINDOW);
            }
            else if (MoveBounds is RectInt32 move)
            {
                // WS_EX_TOPMOST is established on the hidden -> visible transition.
                // Tracking frames only move the silhouette; they must not rebuild the
                // global Z-order or issue another show request on every poll.
                _ = Win32Helper.SetWindowPos(
                    hWnd,
                    IntPtr.Zero,
                    move.X,
                    move.Y,
                    move.Width,
                    move.Height,
                    Win32Helper.SWP_NOACTIVATE |
                    Win32Helper.SWP_NOZORDER);
            }

            if (HideWindow)
            {
                _ = Win32Helper.ShowWindow(hWnd, Win32Helper.SW_HIDE);
            }
        }
    }

    /// <summary>Plans a move/show transition; caller holds _gate.</summary>
    private PendingNativeWork PlanShow(RectInt32 bounds)
    {
        bool boundsChanged = !_hasBounds || !AreEqual(_lastBounds, bounds);
        _lastBounds = bounds;
        _hasBounds = true;

        if (!_visible)
        {
            _visible = true;
            return new PendingNativeWork(null, bounds, null, false);
        }

        return boundsChanged
            ? new PendingNativeWork(null, null, bounds, false)
            : PendingNativeWork.None;
    }

    /// <summary>Plans a hide transition; caller holds _gate.</summary>
    private PendingNativeWork PlanHide()
    {
        if (!_visible)
        {
            return PendingNativeWork.None;
        }

        _visible = false;
        return new PendingNativeWork(null, null, null, true);
    }

    /// <summary>
    /// Plans an opacity transition; caller holds _gate. Returns the value to
    /// apply outside the lock, or null when the layer already carries it.
    /// </summary>
    private byte? PlanOpacity(byte opacity)
    {
        if (_opacity == opacity)
        {
            return null;
        }

        _opacity = opacity;
        return opacity;
    }

    private void ApplyAppearance(string caption, double cornerRadius)
    {
        Color tone = ResolveNeutralTone();
        double surfaceRadius = Math.Clamp(cornerRadius, 0, 32);
        _captionTextBlock.Text = caption;
        _surfaceBorder.Background = new SolidColorBrush(Color.FromArgb(
            0x18,
            tone.R,
            tone.G,
            tone.B));
        _surfaceBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(
            0xD8,
            tone.R,
            tone.G,
            tone.B));
        _surfaceBorder.CornerRadius = new CornerRadius(surfaceRadius);
        _badgeBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(
            0xE8,
            tone.R,
            tone.G,
            tone.B));
        _badgeBorder.CornerRadius = new CornerRadius(
            Math.Clamp(surfaceRadius * 0.45, 2, 4));
    }

    private static RectInt32 NormalizeBounds(RectInt32 bounds)
    {
        return new RectInt32(
            bounds.X,
            bounds.Y,
            Math.Max(1, bounds.Width),
            Math.Max(1, bounds.Height));
    }

    private static bool AreEqual(RectInt32 left, RectInt32 right)
    {
        return left.X == right.X &&
               left.Y == right.Y &&
               left.Width == right.Width &&
               left.Height == right.Height;
    }

    /// <summary>
    /// The neutral interaction tone this window draws its drag silhouette with.
    /// The window owns no visual tree yet when the constructor builds one, so it
    /// reads the application theme resources directly.
    /// </summary>
    private static Color ResolveNeutralTone() =>
        NeutralInteractionBrush.Line(null);
}
