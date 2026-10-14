using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DaisyOS.Shell;

namespace DaisyOS.Shell.Converters;

public sealed class ResourceBrushConverter : IValueConverter
{
    public static readonly ResourceBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var key = value as string;
        return key is not null &&
               Application.Current is App app &&
               app.TryFindResource(key, app.ActualThemeVariant, out var resource) &&
               resource is IBrush brush
            ? brush
            : null;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
