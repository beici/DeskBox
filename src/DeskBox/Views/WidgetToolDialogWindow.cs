using DeskBox.Helpers;
using DeskBox.Services;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
using Windows.System;
using WinRT.Interop;

namespace DeskBox.Views;

/// <summary>
/// Small always-on-top settings host for widget-owned editors (margins, custom
/// colour) that do not fit inside the widget itself.
///
/// A widget is typically ~313x326 physical pixels, so a ContentDialog opened in
/// the widget's XamlRoot was clipped by the host window and lost entire rows of
/// input. A windowed XAML popup is not an option either — it does not take Win32
/// focus, which is why the stack popover rename editor is a real window too — so
/// the editor gets its own window, sized from the monitor work area and centred
/// over the widget it belongs to.
/// </summary>
internal sealed class WidgetToolDialogWindow : Window
{
    private readonly AppWindow _appWindow;
    private readonly TaskCompletionSource<bool> _completion =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Button _primaryButton;
    private bool _closed;

    internal WidgetToolDialogWindow(
        string title,
        FrameworkElement content,
        string primaryText,
        string closeText,
        WidgetDialogViewport viewport)
    {
        Title = title;

        var scroller = new ScrollViewer
        {
            Content = content,
            HorizontalScrollMode = ScrollMode.Disabled,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            // The window is already sized to the content budget; the scroller is
            // the safety net for long localized text and small work areas.
            MaxHeight = viewport.ContentHeight,
            Padding = new Thickness(0, 0, 2, 0)
        };

        _primaryButton = new Button
        {
            Content = primaryText,
            Style = ResolveStyle("AccentButtonStyle"),
            MinWidth = 96
        };
        _primaryButton.Click += (_, _) => Complete(true);
        var closeButton = new Button
        {
            Content = closeText,
            MinWidth = 96
        };
        closeButton.Click += (_, _) => Complete(false);

        var commands = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Right
        };
        commands.Children.Add(_primaryButton);
        commands.Children.Add(closeButton);

        var root = new Grid
        {
            Padding = new Thickness(
                WidgetDialogLayout.ChromePaddingWidthDips / 2,
                12,
                WidgetDialogLayout.ChromePaddingWidthDips / 2,
                16),
            RowSpacing = 12
        };
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(scroller, 0);
        Grid.SetRow(commands, 1);
        root.Children.Add(scroller);
        root.Children.Add(commands);

        // Enter saves, Escape cancels: the window has no dialog chrome to imply
        // it, so the shortcuts are wired explicitly.
        root.KeyDown += Root_KeyDown;

        string backdrop = WindowsCompatibilityService.ApplySafeBackdrop(this);
        if (string.Equals(backdrop, "Solid", StringComparison.Ordinal) &&
            Application.Current.Resources.TryGetValue(
                "ApplicationPageBackgroundThemeBrush",
                out object? pageBrush) &&
            pageBrush is Brush brush)
        {
            // Windows 10 has no system material here, so the host needs an
            // opaque background of its own instead of showing through.
            root.Background = brush;
        }

        Content = root;
        Closed += (_, _) => Complete(false);

        WindowHandle = WindowNative.GetWindowHandle(this);
        WindowId windowId = Win32Interop.GetWindowIdFromWindow(WindowHandle);
        _appWindow = AppWindow.GetFromWindowId(windowId);
        AppBranding.ApplyWindowIcon(_appWindow);
        _appWindow.IsShownInSwitchers = false;
        if (_appWindow.Presenter is OverlappedPresenter presenter)
        {
            presenter.IsResizable = true;
            presenter.IsMaximizable = false;
            presenter.IsMinimizable = false;
            // The widget being edited lives at the bottom of the desktop band,
            // so the editor must stay above every other window to keep the live
            // preview visible while the user types.
            presenter.IsAlwaysOnTop = true;
        }

        App.Current.ThemeService.TrackWindow(this);
    }

    internal IntPtr WindowHandle { get; }

    /// <summary>
    /// Shows the editor at <paramref name="bounds"/> and completes with true when
    /// the primary button was used, false for cancel or a closed window.
    /// </summary>
    internal Task<bool> ShowAtAsync(RectInt32 bounds)
    {
        _appWindow.MoveAndResize(bounds);
        _appWindow.Show();
        Activate();
        _ = Win32Helper.SetForegroundWindow(WindowHandle);
        _primaryButton.Focus(FocusState.Programmatic);
        App.LogVerbose(
            $"[WidgetToolDialog] Shown hwnd=0x{WindowHandle.ToInt64():X} " +
            $"bounds={bounds.X},{bounds.Y} {bounds.Width}x{bounds.Height}");
        return _completion.Task;
    }

    private void Root_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Escape)
        {
            e.Handled = true;
            Complete(false);
        }
        else if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            Complete(true);
        }
    }

    private void Complete(bool primary)
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _completion.TrySetResult(primary);
        try
        {
            Close();
        }
        catch (Exception ex)
        {
            App.LogVerbose($"[WidgetToolDialog] Close failed: {ex.Message}");
        }
    }

    private static Style? ResolveStyle(string key)
    {
        return Application.Current.Resources.TryGetValue(key, out object? value) && value is Style style
            ? style
            : null;
    }
}
