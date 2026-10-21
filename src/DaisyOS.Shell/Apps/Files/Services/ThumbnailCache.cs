using Avalonia.Media.Imaging;
using DaisyOS.Shell.Apps.Files.Models;

namespace DaisyOS.Shell.Apps.Files.Services;

internal sealed class ThumbnailCache
{
    private static readonly object RegistryGate = new();
    private static readonly List<WeakReference<ThumbnailCache>> Registry = [];
    private readonly int _capacity;
    private readonly long _maximumBytes;
    private int _generation;
    private long _cachedBytes, _liveBytes;
    private readonly object _gate = new();
    private readonly Dictionary<Key, Entry> _entries = [];
    private readonly LinkedList<Key> _lru = [];
    private readonly record struct Key(string Path, long Length, long Ticks, int Size, int Generation, string Variant);
    private sealed class Entry(FileVisual visual, long bytes, LinkedListNode<Key> node)
    {
        public readonly FileVisual Visual = visual;
        public readonly long Bytes = bytes;
        public readonly LinkedListNode<Key> Node = node;
        public int Leases;
        public bool Cached;
    }

    public ThumbnailCache(int capacity = 256, long maximumBytes = 32L * 1024 * 1024)
    {
        _capacity = Math.Max(1, capacity);
        _maximumBytes = Math.Max(0, maximumBytes);
        lock (RegistryGate)
        {
            Registry.RemoveAll(reference => !reference.TryGetTarget(out _));
            Registry.Add(new(this));
        }
    }

    public int Count { get { lock (_gate) return _entries.Count; } }
    public long CachedBytes { get { lock (_gate) return _cachedBytes; } }
    // Includes images evicted from the cache but still held by a visible item.
    public long LiveBytes { get { lock (_gate) return _liveBytes; } }

    internal static (long CachedBytes, long LiveBytes) GetTotals()
    {
        long cached = 0, live = 0;
        ThumbnailCache[] caches;
        lock (RegistryGate)
        {
            caches = Registry.Select(reference => reference.TryGetTarget(out var cache) ? cache : null)
                .OfType<ThumbnailCache>().ToArray();
        }
        foreach (var cache in caches)
            lock (cache._gate) { cached += cache._cachedBytes; live += cache._liveBytes; }
        return (cached, live);
    }

    internal static void TrimAllUnused()
    {
        ThumbnailCache[] caches;
        lock (RegistryGate)
        {
            Registry.RemoveAll(reference => !reference.TryGetTarget(out _));
            caches = Registry.Select(reference => reference.TryGetTarget(out var cache) ? cache : null)
                .OfType<ThumbnailCache>().ToArray();
        }
        foreach (var cache in caches) cache.TrimUnused();
    }

    public void TrimUnused()
    {
        lock (_gate)
            foreach (var entry in _entries.Values.Where(entry => entry.Leases == 0).ToArray()) Evict(entry);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _generation++;
            foreach (var entry in _entries.Values.ToArray()) Evict(entry);
        }
    }

    public VisualLease Acquire(FileEntrySnapshot snapshot, int size, Func<FileVisual> factory, string variant = "")
    {
        Key key;
        lock (_gate)
        {
            key = new Key(DirectoryIndex.Normalize(snapshot.FullPath), snapshot.Length, snapshot.LastWriteUtcTicks, size, _generation, variant);
            if (_entries.TryGetValue(key, out var cached)) return Lease(cached);
        }
        var visual = factory();
        lock (_gate)
        {
            if (_entries.TryGetValue(key, out var cached))
            {
                if (!ReferenceEquals(visual.Thumbnail, cached.Visual.Thumbnail)) (visual.Thumbnail as IDisposable)?.Dispose();
                return Lease(cached);
            }
            var bytes = visual.Thumbnail is Bitmap bitmap
                ? (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4
                : visual.Thumbnail is null ? 0 : (long)Math.Max(1, size) * Math.Max(1, size) * 4;
            var entry = new Entry(visual, bytes, new LinkedListNode<Key>(key));
            _liveBytes += bytes;
            // A decode that crossed Clear() must not refill an invalidated cache.
            if (key.Generation == _generation && bytes <= _maximumBytes)
            {
                entry.Cached = true;
                _lru.AddFirst(entry.Node);
                _entries.Add(key, entry);
                _cachedBytes += bytes;
            }
            // Take ownership before eviction: an oversized new image must survive
            // until its caller releases it, even if it is never cached.
            var lease = Lease(entry);
            while (_entries.Count > _capacity || _cachedBytes > _maximumBytes)
                Evict(_entries[_lru.Last!.Value]);
            return lease;
        }
    }

    private VisualLease Lease(Entry entry)
    {
        if (entry.Cached)
        {
            _lru.Remove(entry.Node);
            _lru.AddFirst(entry.Node);
        }
        entry.Leases++;
        return new VisualLease(entry.Visual, () =>
        {
            lock (_gate)
                if (--entry.Leases == 0 && !entry.Cached) DisposeThumbnail(entry);
        });
    }

    private void Evict(Entry entry)
    {
        _entries.Remove(entry.Node.Value);
        _lru.Remove(entry.Node);
        _cachedBytes -= entry.Bytes;
        entry.Cached = false;
        if (entry.Leases == 0) DisposeThumbnail(entry);
    }

    private void DisposeThumbnail(Entry entry)
    {
        _liveBytes -= entry.Bytes;
        (entry.Visual.Thumbnail as IDisposable)?.Dispose();
    }
}

internal sealed class VisualLease(FileVisual visual, Action release) : IDisposable
{
    private Action? _release = release;
    public FileVisual Visual { get; } = visual;
    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
