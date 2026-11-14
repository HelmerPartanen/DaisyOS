using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace DaisyOS.System.Search;

/// <summary>Resolve every path component relative to an open directory with O_NOFOLLOW.
/// Enumeration uses held descriptors, so a directory-to-symlink race cannot escape a root.</summary>
internal static partial class SearchFileAccess
{
    private const int NoFollow = 0x20000, DirectoryFlag = 0x10000, CloseOnExec = 0x80000, NonBlock = 0x800;
    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int openat(int directory, string path, int flags);
    [LibraryImport("libc")] private static partial uint geteuid();
    internal static bool IsRoot => OperatingSystem.IsLinux() && geteuid() == 0;

    internal static SafeFileHandle? Open(string path)
    {
        if (!OperatingSystem.IsLinux()) return null;
        var parts = global::System.IO.Path.GetFullPath(path).Split('/', StringSplitOptions.RemoveEmptyEntries);
        var descriptor = openat(-100, "/", NoFollow | DirectoryFlag | CloseOnExec | NonBlock);
        if (descriptor < 0) return null;
        var handle = new SafeFileHandle(descriptor, true);
        for (var i = 0; i < parts.Length; i++)
        {
            descriptor = openat(handle.DangerousGetHandle().ToInt32(), parts[i], NoFollow | CloseOnExec | NonBlock |
                (i < parts.Length - 1 ? DirectoryFlag : 0));
            handle.Dispose();
            if (descriptor < 0) return null;
            handle = new SafeFileHandle(descriptor, true);
        }
        return handle;
    }

    internal static IndexedFile? Read(string path)
    {
        try
        {
            using var handle = Open(path);
            if (handle is null) return null;
            var attributes = File.GetAttributes(handle);
            if ((attributes & (FileAttributes.ReparsePoint | FileAttributes.Device)) != 0) return null;
            var directory = (attributes & FileAttributes.Directory) != 0;
            return new(path, global::System.IO.Path.GetFileName(path), global::System.IO.Path.GetDirectoryName(path) ?? "",
                directory ? "" : global::System.IO.Path.GetExtension(path).ToLowerInvariant(), directory,
                directory ? 0 : RandomAccess.GetLength(handle), File.GetLastWriteTimeUtc(handle).Ticks);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
    }

    internal static IEnumerable<string> Children(string path)
    {
        using var handle = Open(path);
        if (handle is null) yield break;
        var descriptorPath = $"/proc/self/fd/{handle.DangerousGetHandle().ToInt32()}";
        using var enumerator = Directory.EnumerateFileSystemEntries(descriptorPath, "*", new EnumerationOptions
        { AttributesToSkip = 0, IgnoreInaccessible = false, RecurseSubdirectories = false }).GetEnumerator();
        while (enumerator.MoveNext()) yield return global::System.IO.Path.Combine(path, global::System.IO.Path.GetFileName(enumerator.Current));
    }
}
