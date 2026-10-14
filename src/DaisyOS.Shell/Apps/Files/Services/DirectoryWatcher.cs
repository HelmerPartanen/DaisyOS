using System.IO;

namespace DaisyOS.Shell.Apps.Files.Services;

public enum DirectoryEventKind { Upsert, Remove, Rename, Failure }
public readonly record struct DirectoryEvent(DirectoryEventKind Kind, string Path, string? OldPath = null);

public sealed class DirectoryWatcher : IDisposable
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Action<IReadOnlyList<DirectoryEvent>> _apply;
    private readonly List<DirectoryEvent> _buffer = [];
    private readonly Timer _timer;
    private FileSystemWatcher? _watcher;
    private bool _live;
    private bool _failurePending;
    private bool _disposed;
    public DirectoryWatcher(string path, Action<IReadOnlyList<DirectoryEvent>> apply, bool enabled = true)
    {
        _path = DirectoryIndex.Normalize(path);
        _apply = apply;
        _timer = new Timer(_ => Flush(), null, Timeout.Infinite, Timeout.Infinite);
        if (enabled) Start();
    }
    public void Enqueue(DirectoryEvent change)
    {
        if (change.Kind == DirectoryEventKind.Remove && DirectoryIndex.Normalize(change.Path) == _path)
            change = new(DirectoryEventKind.Failure, _path);
        // Inotify can report attributes on the watched directory itself.
        // A session owns its immediate children, not its root or other directories.
        if (change.Kind != DirectoryEventKind.Failure && (string.IsNullOrWhiteSpace(change.Path) ||
            !string.Equals(Path.GetDirectoryName(DirectoryIndex.Normalize(change.Path)), _path, StringComparison.Ordinal))) return;
        lock (_gate)
        {
            if (_disposed) return;
            // Bound memory even when enumeration or a filesystem stalls.
            if (_buffer.Count >= 8192)
            {
                _buffer.Clear();
                _buffer.Add(new DirectoryEvent(DirectoryEventKind.Failure, _path));
            }
            else _buffer.Add(change);
            _failurePending |= change.Kind == DirectoryEventKind.Failure || _buffer[0].Kind == DirectoryEventKind.Failure;
            if (_live) _timer.Change(_failurePending ? 1000 : 40, Timeout.Infinite);
        }
    }
    public void Activate()
    {
        lock (_gate) { if (_disposed) return; _live = true; }
        Flush();
    }
    public void Restart()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _watcher?.Dispose();
            _watcher = null;
            Start();
        }
    }
    private void Start()
    {
        try
        {
            _watcher = new FileSystemWatcher(_path)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.Attributes,
                InternalBufferSize = 32768
            };
            _watcher.Created += (_, e) => Enqueue(new(DirectoryEventKind.Upsert, e.FullPath));
            _watcher.Changed += (_, e) => Enqueue(new(DirectoryEventKind.Upsert, e.FullPath));
            _watcher.Deleted += (_, e) => Enqueue(new(DirectoryEventKind.Remove, e.FullPath));
            _watcher.Renamed += (_, e) => Enqueue(new(DirectoryEventKind.Rename, e.FullPath, e.OldFullPath));
            _watcher.Error += (_, _) => Enqueue(new(DirectoryEventKind.Failure, _path));
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        { Enqueue(new(DirectoryEventKind.Failure, _path)); }
    }
    private void Flush()
    {
        DirectoryEvent[] events;
        lock (_gate)
        {
            if (_disposed || !_live || _buffer.Count == 0) return;
            events = _buffer.ToArray();
            _buffer.Clear();
            _failurePending = false;
        }
        _apply(Coalesce(events));
    }
    public static IReadOnlyList<DirectoryEvent> Coalesce(IEnumerable<DirectoryEvent> events)
    {
        var paths = new Dictionary<string, DirectoryEvent>(StringComparer.Ordinal);
        List<DirectoryEvent> renames = [];
        bool failure = false;
        foreach (var e in events)
        {
            if (e.Kind == DirectoryEventKind.Failure) { failure = true; continue; }
            if (e.Kind == DirectoryEventKind.Rename)
            {
                renames.Add(e);
                if (e.OldPath != null) paths.Remove(e.OldPath);
                paths.Remove(e.Path);
            }
            else paths[e.Path] = e;
        }
        if (failure) renames.Add(new(DirectoryEventKind.Failure, string.Empty));
        renames.AddRange(paths.Values);
        return renames;
    }
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _watcher?.Dispose();
            _timer.Dispose();
            _buffer.Clear();
        }
    }
}
