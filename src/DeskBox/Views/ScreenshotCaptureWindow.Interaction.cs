using DeskBox.Helpers;
using DeskBox.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;

namespace DeskBox.Views;

public sealed partial class ScreenshotCaptureWindow
{
    private void InitializeCaptureShortcuts()
    {
        AddShortcut(VirtualKey.Enter, VirtualKeyModifiers.None, copy: true);
        AddShortcut(VirtualKey.S, VirtualKeyModifiers.Control, copy: false);
        foreach (Button button in new[]
        {
            RectangleToolButton, EllipseToolButton, LineToolButton, ArrowToolButton,
            PencilToolButton, MarkerToolButton, TextToolButton, MosaicToolButton,
            BlurToolButton, EraserToolButton
        })
        {
            SetAccessibleCaption(button, _localization.T($"Screenshot.Tool.{button.Tag}"));
        }
        SetAccessibleCaption(UndoButton, _localization.T("Screenshot.Undo"));
        SetAccessibleCaption(RedoButton, _localization.T("Screenshot.Redo"));
        SetAccessibleCaption(CopyButton, _localization.T("Screenshot.Copy") + " / Enter");
        SetAccessibleCaption(SaveButton, _localization.T("Screenshot.Save") + " (Ctrl+S)");
        SetAccessibleCaption(PinButton, _localization.T("Screenshot.Pin") + " (F3)");
        SetAccessibleCaption(ResetButton, _localization.T("Screenshot.ReSelect"));
        SetAccessibleCaption(CancelButton, _localization.T("Screenshot.Cancel"));
        SetAccessibleCaption(ColorPickButton, _localization.T("Screenshot.ColorLabel") + " (C)");
    }

    private static void SetAccessibleCaption(Button button, string caption)
    {
        ToolTipService.SetToolTip(button, caption);
        AutomationProperties.SetName(button, caption);
    }

    private void AddShortcut(VirtualKey key, VirtualKeyModifiers modifiers, bool copy)
    {
        var accelerator = new KeyboardAccelerator { Key = key, Modifiers = modifiers };
        accelerator.Invoked += (sender, args) =>
        {
            if (_isCapturing || IsTextInputFocused() || _dragMode != DragMode.None)
            {
                return;
            }
            if (_preSelectMode)
            {
                _hasSelection = true;
                CommitSelection();
            }
            if (_hasSelection)
            {
                args.Handled = true;
                _ = ExportSelectionAsync(copy);
            }
        };
        RootGrid.KeyboardAccelerators.Add(accelerator);
    }

    private void SetCaptureBusy(bool busy)
    {
        if (busy)
        {
            AnnotationLayer.CommitPendingEdit();
        }
        _isCapturing = busy;
        // Export awaits background encoding/pickers. Keep geometry and the
        // annotation model stable until the operation completes or is cancelled.
        RootGrid.IsHitTestVisible = !busy;
        foreach (Button button in _toolButtons)
        {
            button.IsEnabled = !busy;
        }
        ColorPickButton.IsEnabled = !busy;
        UndoButton.IsEnabled = !busy && AnnotationLayer.CanUndo;
        RedoButton.IsEnabled = !busy && AnnotationLayer.CanRedo;
        AnnotationLayer.IsEnabled = !busy;
        CopyButton.IsEnabled = !busy;
        SaveButton.IsEnabled = !busy;
        PinButton.IsEnabled = !busy;
        ResetButton.IsEnabled = !busy;
    }

    private static async Task CopyBitmapWithRetryAsync(DataPackage package)
    {
        const int clipboardCannotOpen = unchecked((int)0x800401D0);
        const int maximumAttempts = 4;
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                DeskBoxClipboardWriteScope.MarkWrite(hasImage: true);
                Clipboard.SetContent(package);
                Clipboard.Flush();
                return;
            }
            catch (Exception ex) when (ex.HResult == clipboardCannotOpen && attempt + 1 < maximumAttempts)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(40 * (attempt + 1)));
            }
        }
    }
}
