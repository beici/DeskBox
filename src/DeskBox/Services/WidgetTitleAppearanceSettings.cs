using DeskBox.Models;

namespace DeskBox.Services;

/// <summary>
/// Title bar appearance preferences: caption alignment and a custom icon
/// image. Global alignment lives on <see cref="AppSettings"/>; per-widget
/// overrides and the custom icon path live in <see cref="WidgetConfig.Metadata"/>
/// following the <see cref="WidgetForegroundSettings"/> override pattern, so
/// older settings files read and ignore them without a schema migration.
/// </summary>
public static class WidgetTitleAppearanceSettings
{
    public const string AlignLeft = "Left";
    public const string AlignCenter = "Center";
    public const string AlignRight = "Right";

    public const string AlignmentOverrideMetadataKey = "WidgetTitleAlignment";
    public const string CustomIconPathMetadataKey = "WidgetCustomIconPath";

    public static string NormalizeAlignment(string? value)
    {
        if (string.Equals(value, AlignCenter, StringComparison.OrdinalIgnoreCase))
        {
            return AlignCenter;
        }

        if (string.Equals(value, AlignRight, StringComparison.OrdinalIgnoreCase))
        {
            return AlignRight;
        }

        return AlignLeft;
    }

    public static bool IsSupportedImageExtension(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".png", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".ico", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public static string? GetAlignmentOverride(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Metadata is null ||
            !config.Metadata.TryGetValue(AlignmentOverrideMetadataKey, out string? value))
        {
            return null;
        }

        return NormalizeAlignment(value);
    }

    public static string ResolveAlignment(WidgetConfig config, AppSettings settings) =>
        GetAlignmentOverride(config) ?? NormalizeAlignment(settings.WidgetTitleAlignment);

    public static void SetAlignmentOverride(WidgetConfig config, string? value)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Metadata ??= [];
        string normalized = NormalizeAlignment(value);
        if (normalized == AlignLeft)
        {
            config.Metadata.Remove(AlignmentOverrideMetadataKey);
            return;
        }

        config.Metadata[AlignmentOverrideMetadataKey] = normalized;
    }

    public static string? GetCustomIconPath(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Metadata is null ||
            !config.Metadata.TryGetValue(CustomIconPathMetadataKey, out string? path) ||
            string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        return IsSupportedImageExtension(path) && File.Exists(path) ? path : null;
    }

    public static void SetCustomIconPath(WidgetConfig config, string? path)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Metadata ??= [];
        if (string.IsNullOrWhiteSpace(path))
        {
            config.Metadata.Remove(CustomIconPathMetadataKey);
            return;
        }

        config.Metadata[CustomIconPathMetadataKey] = Path.GetFullPath(path);
    }

    public static bool NormalizeGlobal(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        string normalized = NormalizeAlignment(settings.WidgetTitleAlignment);
        if (string.Equals(settings.WidgetTitleAlignment, normalized, StringComparison.Ordinal))
        {
            return false;
        }

        settings.WidgetTitleAlignment = normalized;
        return true;
    }
}

/// <summary>
/// Per-widget margin entry preferences for the margin dialog. The distance
/// values themselves are derived from the live window bounds (single source
/// of truth, so drag changes and typed values always agree); only the
/// entry mode (unified vs. per-side) is persisted.
/// </summary>
public static class WidgetMarginSettings
{
    public const string ModeUniform = "Uniform";
    public const string ModePerSide = "PerSide";

    public const string ModeOverrideMetadataKey = "WidgetMarginEntryMode";

    public const int MinimumMarginPixels = 0;
    public const int MaximumMarginPixels = 200;

    public static string NormalizeMode(string? value) =>
        string.Equals(value, ModePerSide, StringComparison.OrdinalIgnoreCase)
            ? ModePerSide
            : ModeUniform;

    public static string? GetModeOverride(WidgetConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);
        if (config.Metadata is null ||
            !config.Metadata.TryGetValue(ModeOverrideMetadataKey, out string? value))
        {
            return null;
        }

        return NormalizeMode(value);
    }

    public static void SetModeOverride(WidgetConfig config, string? value)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.Metadata ??= [];
        string normalized = NormalizeMode(value);
        if (normalized == ModeUniform)
        {
            config.Metadata.Remove(ModeOverrideMetadataKey);
            return;
        }

        config.Metadata[ModeOverrideMetadataKey] = normalized;
    }

    public static int ClampMargin(int value) =>
        Math.Clamp(value, MinimumMarginPixels, MaximumMarginPixels);

    public static bool TryParseMargin(string? text, out int value) =>
        int.TryParse(text, out value) &&
        value >= MinimumMarginPixels &&
        value <= MaximumMarginPixels;
}
