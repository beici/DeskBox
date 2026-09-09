// Copyright (c) DeskBox. All rights reserved.

using Microsoft.UI.Xaml.Data;

namespace DeskBox.Helpers;

/// <summary>
/// Converts a <see cref="Windows.UI.Color"/> into the shared cached brush.
/// <para>
/// View-models expose marker colors as plain structs so they stay testable
/// headlessly; turning a color into a WinUI brush requires a live XAML runtime
/// and is therefore a view concern, done here on the UI thread via
/// <see cref="SharedBrushCache"/> so list virtualization reuses brushes.
/// </para>
/// </summary>
public sealed class ColorToSharedBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        return value is Windows.UI.Color color
            ? SharedBrushCache.GetOrCreate(color)
            : Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language)
    {
        return Microsoft.UI.Xaml.DependencyProperty.UnsetValue;
    }
}
