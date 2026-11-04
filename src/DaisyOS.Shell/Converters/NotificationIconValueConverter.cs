using System;
using System.Globalization;
using Avalonia.Data.Converters;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Services.Notifications;

namespace DaisyOS.Shell.Converters;

/// <summary>
/// Resolves freedesktop icon names, desktop IDs, application names, and direct
/// image paths through the same Linux icon-theme/desktop-entry path as the
/// taskbar. A null result deliberately leaves the semantic system glyph visible.
/// </summary>
public sealed class NotificationIconValueConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var image = Resolve(value as NotificationItem);
        return parameter switch
        {
            "Visible" => image is not null,
            "FallbackVisible" => image is null,
            _ => image
        };
    }

    private static Avalonia.Media.IImage? Resolve(NotificationItem? notification)
    {
        if (notification is null)
        {
            return null;
        }

        var path = NotificationArtwork.Resolve(notification).AppIconPath;

        return string.IsNullOrWhiteSpace(path) ? null : DesktopItemLoader.LoadBitmapSafe(path);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

}
