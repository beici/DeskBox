using DeskBox.Platform;
using Microsoft.Windows.Storage.Pickers;

namespace DeskBox.Services;

/// <summary>
/// File-save picker entrance for the screenshot export, mirroring the
/// FileOpenPickerService pattern (Microsoft.Windows.Storage.Pickers with a
/// Win32 owner window id, so no InitializeWithWindow dance is needed).
/// </summary>
public static class FileSavePickerService
{
    public static async Task<string?> PickSaveFileAsync(
        IntPtr ownerHwnd,
        string suggestedFileName,
        IReadOnlyList<string> fileTypeChoices,
        PickerLocationId suggestedStartLocation = PickerLocationId.PicturesLibrary)
    {
        FileOpenPickerService.ValidateOwnerWindowHandle(ownerHwnd);

        try
        {
            var ownerWindowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(ownerHwnd);
            var picker = new FileSavePicker(ownerWindowId)
            {
                SuggestedStartLocation = suggestedStartLocation,
                SuggestedFileName = suggestedFileName,
            };
            foreach (string fileType in fileTypeChoices)
            {
                picker.FileTypeChoices.Add(fileType, new List<string> { fileType });
            }

            var file = await picker.PickSaveFileAsync();
            return string.IsNullOrWhiteSpace(file?.Path) ? null : file.Path;
        }
        catch (Exception ex)
        {
            App.Log($"[FileSavePicker] Failed to pick save file: {ex}");
            return null;
        }
    }
}
