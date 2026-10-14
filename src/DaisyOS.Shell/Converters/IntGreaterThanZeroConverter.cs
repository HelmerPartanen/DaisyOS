using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace DaisyOS.Shell.Converters;

/// <summary>
/// Returns <c>true</c> when the bound integer value is greater than zero.
/// Used to show the countdown progress bar only on timed notifications.
/// </summary>
public sealed class IntGreaterThanZeroConverter : IValueConverter
{
    public static readonly IntGreaterThanZeroConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int i && i > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

