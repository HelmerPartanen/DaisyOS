using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

public static class DirectoryReconciler
{
    public static DirectoryDelta Diff(DirectoryIndex current, IReadOnlyList<FileEntrySnapshot> snapshot) => Diff(current.Entries.Select(e => e.Snapshot).ToArray(), snapshot);
    public static DirectoryDelta Diff(IReadOnlyList<FileEntrySnapshot> current, IReadOnlyList<FileEntrySnapshot> snapshot)
    {
        var old = current.ToDictionary(e => DirectoryIndex.Normalize(e.FullPath), StringComparer.Ordinal);
        List<FileEntrySnapshot> added = [], changed = [];
        foreach (var entry in snapshot)
        {
            if (!old.Remove(DirectoryIndex.Normalize(entry.FullPath), out var previous)) added.Add(entry);
            else if (previous != entry) changed.Add(entry);
        }
        return new DirectoryDelta(added, old.Keys.ToArray(), [], changed);
    }
}
