using System.IO;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

public sealed class DirectoryIndex
{
    private readonly Dictionary<string, FileEntryViewModel> _byPath = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _positions = new(StringComparer.Ordinal);
    private readonly List<FileEntryViewModel> _entries = [];
    public IReadOnlyDictionary<string, FileEntryViewModel> ByPath => _byPath;
    public IReadOnlyList<FileEntryViewModel> Entries => _entries;
    public long Revision { get; private set; }
    public static string Normalize(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
    public FileEntryViewModel? Find(string path) => _byPath.GetValueOrDefault(Normalize(path));
    public FileEntryViewModel Upsert(FileEntrySnapshot snapshot)
    {
        var key = Normalize(snapshot.FullPath);
        if (_byPath.TryGetValue(key, out var existing))
        {
            if (existing.Snapshot != snapshot) { existing.ApplySnapshot(snapshot); Revision++; }
            return existing;
        }
        var entry = new FileEntryViewModel(snapshot);
        _byPath.Add(key, entry);
        _positions.Add(key, _entries.Count);
        _entries.Add(entry);
        Revision++;
        return entry;
    }
    public FileEntryViewModel? Remove(string path)
    {
        var key = Normalize(path);
        if (!_byPath.Remove(key, out var entry)) return null;
        var position = _positions[key];
        var last = _entries[^1];
        _entries[position] = last;
        _positions[Normalize(last.Path)] = position;
        _entries.RemoveAt(_entries.Count - 1);
        _positions.Remove(key);
        Revision++;
        return entry;
    }
    public FileEntryViewModel Rename(FileRenameDelta rename)
    {
        var oldKey = Normalize(rename.OldPath);
        var newKey = Normalize(rename.Snapshot.FullPath);
        if (oldKey == newKey) return Upsert(rename.Snapshot);
        if (Find(oldKey) is not { } entry) return Upsert(rename.Snapshot);
        Remove(newKey);
        var position = _positions[oldKey];
        _byPath.Remove(oldKey);
        _positions.Remove(oldKey);
        entry.ApplySnapshot(rename.Snapshot);
        _byPath[newKey] = entry;
        _positions[newKey] = position;
        Revision++;
        return entry;
    }
}
