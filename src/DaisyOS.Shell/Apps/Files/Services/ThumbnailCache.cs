using Avalonia.Media;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

internal sealed class ThumbnailCache(int capacity = 256)
{
    private int _generation;
    private readonly object _gate = new();
    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly LinkedList<Key> _lru = [];
    private readonly record struct Key(string Path, long Length, long Ticks, int Size, int Generation, string Variant);
    private sealed class Entry(FileVisual visual, LinkedListNode<Key> node)
    {
        public FileVisual Visual = visual;
        public LinkedListNode<Key> Node = node;
        public int Leases;
        public bool Cached = true;
    }
    public int Count { get { lock (_gate) return _entries.Count; } }
    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            foreach (var entry in _entries.Values)
            {
                entry.Cached = false;
                if (entry.Leases == 0) DisposeThumbnail(entry);
            }
            _entries.Clear();
            _lru.Clear();
        }
    }
    public VisualLease Acquire(FileEntrySnapshot snapshot, int size, Func<FileVisual> factory, string variant = "")
    {
        var key = new Key(DirectoryIndex.Normalize(snapshot.FullPath), snapshot.Length, snapshot.LastWriteUtcTicks, size, Volatile.Read(ref _generation), variant);
        lock (_gate)
            if (_entries.TryGetValue(key, out var cached)) return Lease(cached);
        var visual = factory();
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                if (!ReferenceEquals(visual.Thumbnail, cached.Visual.Thumbnail)) (visual.Thumbnail as IDisposable)?.Dispose();
                return Lease(cached);
            }
            var entry = new Entry(visual, _lru.AddFirst(key));
            _entries.Add(key, entry);
            while (_entries.Count > Math.Max(1, capacity))
            {
                var last = _lru.Last!;
                var removed = _entries[last.Value];
                _entries.Remove(last.Value);
                _lru.RemoveLast();
                removed.Cached = false;
                if (removed.Leases == 0) DisposeThumbnail(removed);
            }
            return Lease(entry);
        }
    }
    private VisualLease Lease(Entry entry)
    {
        _lru.Remove(entry.Node);
        _lru.AddFirst(entry.Node);
        entry.Leases++;
        return new VisualLease(entry.Visual, () =>
        {
            lock (_gate)
                if (--entry.Leases == 0 && !entry.Cached) DisposeThumbnail(entry);
        });
    }
    private static void DisposeThumbnail(Entry entry) => (entry.Visual.Thumbnail as IDisposable)?.Dispose();
}

internal sealed class VisualLease(FileVisual visual, Action release) : IDisposable
{
    private Action? _release = release;
    public FileVisual Visual { get; } = visual;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
