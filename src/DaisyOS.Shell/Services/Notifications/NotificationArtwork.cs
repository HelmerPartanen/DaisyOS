using System;
using System.IO;
using System.Linq;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Services.Notifications;

/// <summary>Separates application identity from notification content images.</summary>
internal sealed record NotificationArtwork(string? AppIconPath, string? PreviewSource)
{
    internal static NotificationArtwork Resolve(NotificationItem notification)
    {
        // Resolve identities independently: an unrelated built-in icon name must
        // not win over the sender's installed desktop-entry identity.
        var sourceIcon = ResolveIcon(notification.SourceAppId);
        var nameIcon = ResolveIcon(notification.SourceAppName);
        var sourceIsDisplayName = !string.IsNullOrWhiteSpace(notification.SourceAppName)
            && string.Equals(notification.SourceAppId, notification.SourceAppName, StringComparison.OrdinalIgnoreCase);
        var explicitIcon = ResolveIcon(notification.Icon);
        var image = LocalPath(notification.PreviewImage);
        var hintedIcon = IsApplicationIcon(image, notification, sourceIcon, explicitIcon) ? ResolveIcon(image) : null;
        return new NotificationArtwork(First(hintedIcon, sourceIsDisplayName ? null : sourceIcon, explicitIcon, sourceIcon, nameIcon),
            hintedIcon is not null ? null : notification.PreviewImage);
    }

    private static string? First(params string?[] paths) => paths.FirstOrDefault(path => !string.IsNullOrWhiteSpace(path));

    private static string? ResolveIcon(string? source)
    {
        source = LocalPath(source);
        if (string.IsNullOrWhiteSpace(source)) return null;
        if (Uri.TryCreate(source, UriKind.Absolute, out var uri) && !uri.IsFile)
            return uri.Scheme == "avares" && Avalonia.Platform.AssetLoader.Exists(uri) ? source : null;
        // An explicit file must not be mistaken for a built-in app merely
        // because its basename is "notes.png" or "mail.png".
        if (Path.IsPathFullyQualified(source)) return File.Exists(source) ? source : null;
        var path = DesktopItemLoader.ResolveApplicationIconPath([source]);
        return string.IsNullOrWhiteSpace(path) ? null : path;
    }

    private static bool IsApplicationIcon(string? image, NotificationItem item, string? sourceIcon, string? explicitIcon)
    {
        if (string.IsNullOrWhiteSpace(image) || image.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(image, sourceIcon, StringComparison.Ordinal) || string.Equals(image, explicitIcon, StringComparison.Ordinal)) return true;
        // Named theme icons are application/icon artwork, not image attachments.
        if (!Path.IsPathFullyQualified(image)) return ResolveIcon(image) is not null;
        if (image.Contains("/share/icons/", StringComparison.OrdinalIgnoreCase)
            || image.Contains("/share/pixmaps/", StringComparison.OrdinalIgnoreCase)
            || image.StartsWith(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".icons") + Path.DirectorySeparatorChar, StringComparison.Ordinal)) return true;
        // Electron packages commonly put their notification logo here instead
        // of passing app_icon. Do not classify arbitrary square photos as logos.
        if (image.Contains("/resources/linux/", StringComparison.OrdinalIgnoreCase)) return true;
        var name = Normalize(Path.GetFileNameWithoutExtension(image));
        return name.Length > 0 && (name == Normalize(item.SourceAppId) || name == Normalize(item.SourceAppName));
    }

    private static string Normalize(string? identity)
    {
        if (identity?.EndsWith(".desktop", StringComparison.OrdinalIgnoreCase) == true) identity = identity[..^8];
        return string.Concat((identity ?? "").Where(char.IsLetterOrDigit)).ToLowerInvariant();
    }

    private static string? LocalPath(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri) && uri.IsFile ? uri.LocalPath : source;
}
