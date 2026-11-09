namespace DeskBox.Models;

/// <summary>
/// Flat settings snapshot for the screenshot capture feature, persisted by
/// <c>ScreenshotSettingsStore</c> under data/screenshot/settings.json. The
/// gesture fields follow the repo idiom for hotkey persistence (two ints,
/// see <c>CoreSettingsSlice.GlobalHotkeyModifiers/Key</c> and
/// <c>SearchSettingsSlice.SearchHotkeyModifiers/Key</c>); every property has
/// a default so future fields migrate by absence alone and no schema
/// version marker is required.
/// </summary>
public sealed class ScreenshotSettings
{
    public bool HotkeyEnabled { get; set; } = true;

    /// <summary>Modifier bitmask (<see cref="HotkeyModifierKeys"/>); default Ctrl+Alt.</summary>
    public int HotkeyModifiers { get; set; } =
        (int)HotkeyModifierKeys.Control | (int)HotkeyModifierKeys.Alt;

    /// <summary>Virtual key; default 0x53 ('S') → Ctrl+Alt+S.</summary>
    public int HotkeyKey { get; set; } = 0x53;
}
