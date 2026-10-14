using System.IO;

namespace DaisyOS.Shell.Apps.Files.Models;

public readonly record struct FileEntrySnapshot(
    string FullPath, string Name, string Extension, bool IsDirectory,
    FileAttributes Attributes, long Length, long LastWriteUtcTicks);
