using DeskBox.Helpers;
using DeskBox.Models;
using Windows.UI;

namespace DeskBox.Services;

/// <summary>
/// Resolves and persists the per-widget Quick Capture clipboard record list
/// colors (item text color, item card background color, and hovered-item
/// text color).
///
/// Values live in <see cref="WidgetConfig.Metadata"/> following the same
/// override pattern as <see cref="WidgetForegroundSettings"/>, so older
/// settings files read and ignore them without a schema migration. Each
/// channel resolves to either the follow-theme mode (default) or a custom
/// color, and every stored custom color is validated with a WCAG-style
/// contrast check against the opposite channel before it is accepted.
/// </summary>
public static class QuickCaptureClipboardColorSettings
{
    public const string ModeFollowTheme = "FollowTheme";
    public const string ModeCustom = "Custom";

    public const string TextModeOverrideMetadataKey = "QuickCaptureClipboardItemTextColorMode";
    public const string TextColorOverrideMetadataKey = "QuickCaptureClipboardItemTextColor";
    public const string BackgroundModeOverrideMetadataKey = "QuickCaptureClipboardItemBackgroundMode";
    public const string BackgroundColorOverrideMetadataKey = "QuickCaptureClipboardItemBackgroundColor";
    public const string HoverTextModeOverrideMetadataKey = "QuickCaptureClipboardItemHoverTextColorMode";
    public const string HoverTextColorOverrideMetadataKey = "QuickCaptureClipboardItemHoverTextColor";

    /// <summary>
    /// Minimum accepted WCAG contrast ratio between the item text color and
    /// the item background color. The requirement is "never unreadable", not
    /// "accessibility certified", so the floor is deliberately low; values
    /// below it would render the list effectively illegible.
    /// </summary>
    public const double MinimumContrastRatio = 1.3;

    public static string NormalizeMode(string? value) =>
        string.Equals(value, ModeCustom, StringComparison.OrdinalIgnoreCase)
            ? ModeCustom
            : ModeFollowTheme;

    public static string? GetTextModeOverride(WidgetConfig config) =>
        GetModeOverride(config, TextModeOverrideMetadataKey);

    public static string? GetBackgroundModeOverride(WidgetConfig config) =>
        GetModeOverride(config, BackgroundModeOverrideMetadataKey);

    public static bool TryGetTextColorOverride(WidgetConfig config, out Color color) =>
        TryGetColorOverride(config, TextColorOverrideMetadataKey, out color);

    public static bool TryGetBackgroundColorOverride(WidgetConfig config, out Color color) =>
        TryGetColorOverride(config, BackgroundColorOverrideMetadataKey, out color);

    public static string? GetHoverTextModeOverride(WidgetConfig config) =>
        GetModeOverride(config, HoverTextModeOverrideMetadataKey);

    public static bool TryGetHoverTextColorOverride(WidgetConfig config, out Color color) =>
        TryGetColorOverride(config, HoverTextColorOverrideMetadataKey, out color);

    public static void SetTextModeOverride(WidgetConfig config, string? value) =>
        SetModeOverride(config, TextModeOverrideMetadataKey, value);

    public static void SetBackgroundModeOverride(WidgetConfig config, string? value) =>
        SetModeOverride(config, BackgroundModeOverrideMetadataKey, value);

    public static void SetHoverTextModeOverride(WidgetConfig config, string? value) =>
        SetModeOverride(config, HoverTextModeOverrideMetadataKey, value);

    public static void SetTextColorOverride(WidgetConfig config, Color color) =>
        SetColorOverride(config, TextColorOverrideMetadataKey, color);

    public static void SetBackgroundColorOverride(WidgetConfig config, Color color) =>
        SetColorOverride(config, BackgroundColorOverrideMetadataKey, color);

    public static void SetHoverTextColorOverride(WidgetConfig config, Color color) =>
        SetColorOverride(config, HoverTextColorOverrideMetadataKey, color);

    /// <summary>
    /// Removes all overrides so the list returns to the follow-theme
    /// appearance ("一键恢复默认配色").
    /// </summary>
    public static void ResetOverrides(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Metadata is null)
        {
            return;
        }

        config.Metadata.Remove(TextModeOverrideMetadataKey);
        config.Metadata.Remove(TextColorOverrideMetadataKey);
        config.Metadata.Remove(BackgroundModeOverrideMetadataKey);
        config.Metadata.Remove(BackgroundColorOverrideMetadataKey);
        config.Metadata.Remove(HoverTextModeOverrideMetadataKey);
        config.Metadata.Remove(HoverTextColorOverrideMetadataKey);
    }

    /// <summary>
    /// WCAG relative-contrast ratio between two opaque colors. Returns 1.0
    /// (identical colors) when either input is fully transparent, so an
    /// unset channel never passes validation on its own.
    /// </summary>
    public static double ContrastRatio(Color first, Color second)
    {
        if (first.A == 0 || second.A == 0)
        {
            return 1.0;
        }

        double firstLuminance = RelativeLuminance(first);
        double secondLuminance = RelativeLuminance(second);
        double lighter = Math.Max(firstLuminance, secondLuminance);
        double darker = Math.Min(firstLuminance, secondLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Validates a pending custom pair (existing resolved values for
    /// channels the user is not changing) before any override is written.
    /// </summary>
    public static bool IsPairReadable(
        Color? pendingText,
        Color? pendingBackground,
        out double contrastRatio)
    {
        Color text = pendingText ?? default;
        Color background = pendingBackground ?? default;
        contrastRatio = ContrastRatio(text, background);
        return contrastRatio >= MinimumContrastRatio;
    }

    public static bool NormalizeOverrides(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Metadata ??= [];
        bool changed = false;

        changed |= NormalizeModeOverride(config, TextModeOverrideMetadataKey);
        changed |= NormalizeModeOverride(config, BackgroundModeOverrideMetadataKey);
        changed |= NormalizeModeOverride(config, HoverTextModeOverrideMetadataKey);
        changed |= NormalizeColorOverride(config, TextColorOverrideMetadataKey);
        changed |= NormalizeColorOverride(config, BackgroundColorOverrideMetadataKey);
        changed |= NormalizeColorOverride(config, HoverTextColorOverrideMetadataKey);
        return changed;
    }

    /// <summary>
    /// Picks the readable hover text color for the follow-theme mode: the
    /// white/black candidate with the higher WCAG contrast ratio against the
    /// effective card background. The system ListViewItem hover foreground is
    /// theme-driven only, so on a light (custom or light-theme) card the dark
    /// panel theme resolved it to white and the hovered text disappeared;
    /// deriving from the background instead keeps the hover state readable on
    /// every channel combination.
    /// </summary>
    public static Color ResolveAutoHoverTextColor(Color background)
    {
        Color white = Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF5);
        Color black = Color.FromArgb(0xFF, 0x1A, 0x1A, 0x1A);
        return ContrastRatio(white, background) >= ContrastRatio(black, background)
            ? white
            : black;
    }

    private static string? GetModeOverride(WidgetConfig config, string key)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Metadata is null ||
            !config.Metadata.TryGetValue(key, out string? value))
        {
            return null;
        }

        return NormalizeMode(value);
    }

    private static bool TryGetColorOverride(WidgetConfig config, string key, out Color color)
    {
        color = default;
        if (config.Metadata is null ||
            !config.Metadata.TryGetValue(key, out string? value) ||
            !AccentColorHelper.TryParseHex(value, out color))
        {
            return false;
        }

        color = Color.FromArgb(0xFF, color.R, color.G, color.B);
        return true;
    }

    private static void SetModeOverride(WidgetConfig config, string key, string? value)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Metadata ??= [];
        string normalized = NormalizeMode(value);
        if (normalized == ModeFollowTheme)
        {
            config.Metadata.Remove(key);
            return;
        }

        config.Metadata[key] = normalized;
    }

    private static void SetColorOverride(WidgetConfig config, string key, Color color)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Metadata ??= [];
        config.Metadata[key] = AccentColorHelper.ToHex(
            Color.FromArgb(0xFF, color.R, color.G, color.B));
    }

    private static bool NormalizeModeOverride(WidgetConfig config, string key)
    {
        if (!config.Metadata.TryGetValue(key, out string? value))
        {
            return false;
        }

        string normalized = NormalizeMode(value);
        if (string.Equals(value, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        config.Metadata[key] = normalized;
        return true;
    }

    private static bool NormalizeColorOverride(WidgetConfig config, string key)
    {
        if (!config.Metadata.TryGetValue(key, out string? value))
        {
            return false;
        }

        if (!TryGetColorOverride(config, key, out Color color))
        {
            config.Metadata.Remove(key);
            return true;
        }

        string normalized = AccentColorHelper.ToHex(color);
        if (string.Equals(value, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        config.Metadata[key] = normalized;
        return true;
    }

    private static double RelativeLuminance(Color color)
    {
        double r = SrgbChannelToLinear(color.R);
        double g = SrgbChannelToLinear(color.G);
        double b = SrgbChannelToLinear(color.B);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    private static double SrgbChannelToLinear(byte value)
    {
        double channel = value / 255.0;
        return channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);
    }
}
