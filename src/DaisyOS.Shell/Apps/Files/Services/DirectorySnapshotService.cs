using System.IO;
using System.Diagnostics;
using System.IO.Enumeration;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

public class DirectorySnapshotService
{
    private long _enumerations;
    public double LastSnapshotProjectionMilliseconds { get; private set; }
    public long EnumerationCount => Interlocked.Read(ref _enumerations);
    public virtual Task<IReadOnlyList<FileEntrySnapshot>> EnumerateAsync(string path, CancellationToken cancellationToken) => Task.Run<IReadOnlyList<FileEntrySnapshot>>(() =>
    {
        Interlocked.Increment(ref _enumerations);
        bool profile = Environment.GetEnvironmentVariable("DAISYOS_FILES_PERF") == "1";
        long projectionTicks = 0;
        var entries = new FileSystemEnumerable<FileEntrySnapshot>(path,
            (ref FileSystemEntry entry) =>
            {
                var started = profile ? Stopwatch.GetTimestamp() : 0;
                var name = entry.FileName.ToString();
                var directory = entry.IsDirectory;
                var snapshot = new FileEntrySnapshot(entry.ToFullPath(), name,
                    directory ? string.Empty : Path.GetExtension(name).ToLowerInvariant(),
                    directory, entry.Attributes, directory ? 0 : entry.Length, entry.LastWriteTimeUtc.UtcTicks);
                if (profile) projectionTicks += Stopwatch.GetTimestamp() - started;
                return snapshot;
            },
            new EnumerationOptions { RecurseSubdirectories = false, IgnoreInaccessible = false, AttributesToSkip = 0, ReturnSpecialDirectories = false });
        var result = new List<FileEntrySnapshot>();
        foreach (var entry in entries)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Add(entry);
        }
        LastSnapshotProjectionMilliseconds = projectionTicks * 1000d / Stopwatch.Frequency;
        return result;
    }, cancellationToken);

    // Targeted metadata reads are for changed paths, never the enumeration hot path.
    public virtual FileEntrySnapshot? Read(string path)
    {
        try
        {
            var info = new FileInfo(path);
            var attributes = info.Attributes;
            if ((int)attributes == -1) return null;
            if (!info.Exists && (attributes & FileAttributes.Directory) == 0) return null;
            var directory = (attributes & FileAttributes.Directory) != 0;
            return new FileEntrySnapshot(Path.GetFullPath(path), info.Name,
                directory ? string.Empty : info.Extension.ToLowerInvariant(), directory, attributes,
                directory ? 0 : info.Length, info.LastWriteTimeUtc.Ticks);
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException) { return null; }
    }
}
