using System;
using System.IO;

namespace DaisyOS.Shell.Apps.Files;

public enum FileLocationKind
{
    Generic,
    Root,
    Home,
    Desktop,
    Documents,
    Downloads,
    Pictures,
    Videos,
    Music,
    Trash,
    Removable
}

public sealed record FileLocationPresentation(FileLocationKind Kind, string Icon, string ColorResourceKey)
{
    public static FileLocationPresentation Resolve(string? path, string? homePath = null, bool removable = false)
    {
        if (removable)
            return new(FileLocationKind.Removable, "hard-drives", "StatusSuccessBrush");

        if (string.IsNullOrWhiteSpace(path))
            return Generic;

        var home = Normalize(homePath ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        var normalized = Normalize(path);
        var directories = homePath is null ? UserDirectoryPaths.Current : UserDirectoryPaths.Load(homePath);
        if (normalized == Path.GetPathRoot(normalized))
            return new(FileLocationKind.Root, "hard-drives", "ContentSecondaryBrush");
        if (Same(normalized, home))
            return new(FileLocationKind.Home, "house", "ActionPrimaryBrush");
        if (Same(normalized, directories["Desktop"]))
            return new(FileLocationKind.Desktop, "desktop", "FileLocationDesktopBrush");
        if (Same(normalized, directories["Documents"]))
            return new(FileLocationKind.Documents, "file-text", "FileLocationDocumentsBrush");
        if (Same(normalized, directories["Downloads"]))
            return new(FileLocationKind.Downloads, "download-simple", "FileLocationDownloadsBrush");
        if (Same(normalized, directories["Pictures"]))
            return new(FileLocationKind.Pictures, "image", "FileLocationPicturesBrush");
        if (Same(normalized, directories["Videos"]))
            return new(FileLocationKind.Videos, "film-strip", "FileLocationVideosBrush");
        if (Same(normalized, directories["Music"]))
            return new(FileLocationKind.Music, "music-note", "FileLocationMusicBrush");

        var trash = homePath is null ? UserTrashPaths.Files : Path.Combine(UserTrashPaths.ResolveRoot(home, null), "files");
        if (Same(normalized, trash))
            return new(FileLocationKind.Trash, "trash", "ContentSecondaryBrush");

        return Generic;
    }

    public static FileLocationPresentation Generic { get; } =
        new(FileLocationKind.Generic, "folder", "ContentSecondaryBrush");

    private static string Normalize(string path)
    {
        var fullPath = Path.GetFullPath(path);
        var root = Path.GetPathRoot(fullPath);
        return string.Equals(fullPath, root, StringComparison.Ordinal)
            ? fullPath
            : fullPath.TrimEnd(Path.DirectorySeparatorChar);
    }

    private static bool Same(string left, string right) =>
        string.Equals(left, Normalize(right), StringComparison.Ordinal);
}
