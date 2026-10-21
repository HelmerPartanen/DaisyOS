using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>Bounded decoded sources; active renders keep a lease through eviction.</summary>
internal static class WallpaperSourceCache
{
    private const long MaximumBytes = 128L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Entry> Entries = new(StringComparer.Ordinal);
    private static readonly LinkedList<string> Order = new();
    private static long _bytes;

    internal static long CachedBytes { get { lock (Gate) return _bytes; } }

    internal static void TrimUnused()
    {
        lock (Gate)
            foreach (var path in Entries.Where(pair => pair.Value.Leases == 0).Select(pair => pair.Key).ToArray())
                Evict(path);
    }

    internal sealed class Entry(Bitmap bitmap, long length, long ticks, LinkedListNode<string> node)
    {
        public readonly Bitmap Bitmap = bitmap;
        public readonly long Length = length, Ticks = ticks;
        public readonly long Bytes = (long)bitmap.PixelSize.Width * bitmap.PixelSize.Height * 4;
        public readonly LinkedListNode<string> Node = node;
        public int Leases;
        public bool Cached = true;
    }

    internal sealed class Lease : IDisposable
    {
        private Entry? _entry;
        internal Lease(Entry entry) { _entry = entry; entry.Leases++; }
        public Bitmap Bitmap => _entry?.Bitmap ?? throw new ObjectDisposedException(nameof(Lease));
        public void Dispose()
        {
            lock (Gate)
            {
                if (_entry is not { } entry) return;
                _entry = null;
                if (--entry.Leases == 0 && !entry.Cached) entry.Bitmap.Dispose();
            }
        }
    }

    // A lock surface can reuse the already decoded desktop frame on its first
    // paint. This path performs no file I/O or decoding and owns a real lease.
    public static Lease? TryAcquire(string path)
    {
        if (!path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase)) path = Path.GetFullPath(path);
        if (!Monitor.TryEnter(Gate)) return null;
        try { return Entries.TryGetValue(path, out var entry) ? new Lease(entry) : null; }
        finally { Monitor.Exit(Gate); }
    }

    public static Task<Lease?> AcquireAsync(string path, WallpaperDiagnostics diagnostics, CancellationToken token) => Task.Run(() =>
    {
        lock (Gate)
        {
            token.ThrowIfCancellationRequested();
            var asset = path.StartsWith("avares://", StringComparison.OrdinalIgnoreCase);
            if (!asset) path = Path.GetFullPath(path);
            var file = asset ? null : new FileInfo(path);
            if (file is { Exists: false }) { diagnostics.FileReadCount++; return null; }
            var length = file?.Length ?? 0;
            var ticks = file?.LastWriteTimeUtc.Ticks ?? 0;
            if (Entries.TryGetValue(path, out var cached))
            {
                if (cached.Length == length && cached.Ticks == ticks)
                {
                    Order.Remove(cached.Node);
                    Order.AddFirst(cached.Node);
                    return new Lease(cached);
                }
                Evict(path);
            }
            Bitmap bitmap;
            try
            {
                diagnostics.FileReadCount++;
                if (asset)
                {
                    using var stream = AssetLoader.Open(new Uri(path));
                    bitmap = new Bitmap(stream);
                }
                else bitmap = new Bitmap(path);
                diagnostics.BitmapDecodeCount++;
            }
            catch (Exception exception) when (exception is not (OutOfMemoryException or AccessViolationException)) { return null; }
            var entry = new Entry(bitmap, length, ticks, Order.AddFirst(path));
            Entries.Add(path, entry);
            _bytes += entry.Bytes;
            var lease = new Lease(entry);
            while (Entries.Count > 3 || _bytes > MaximumBytes) Evict(Order.Last!.Value);
            return lease;
        }
    }, token);

    private static void Evict(string path)
    {
        var entry = Entries[path];
        Entries.Remove(path);
        Order.Remove(entry.Node);
        _bytes -= entry.Bytes;
        entry.Cached = false;
        if (entry.Leases == 0) entry.Bitmap.Dispose();
    }
}
