using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;

namespace DaisyOS.Shell.Converters;

public sealed class HalfHeightCornerRadiusConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        new CornerRadius(value is double height && double.IsFinite(height) ? Math.Max(0, height / 2) : 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
