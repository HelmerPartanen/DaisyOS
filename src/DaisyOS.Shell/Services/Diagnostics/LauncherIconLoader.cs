using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaisyOS.Shell.Apps.Files.Models;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.ViewModels;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Catalog icons share a bounded cache and leave CPU time for input and rendering.</summary>
internal sealed class LauncherIconLoader : IDisposable
{
    private static readonly SemaphoreSlim Workers = new(Math.Clamp(Environment.ProcessorCount / 2, 2, 4));
    // Byte budget covers cache ownership; catalog leases are counted separately.
    private readonly ThumbnailCache _cache = new(896, 16L * 1024 * 1024);
    private readonly Dictionary<LauncherItemViewModel, Request> _requests = [];
    private int _generation;
    private sealed class Request(int generation, string iconName, bool isLight)
    {
        public readonly string IconName = iconName;
        public readonly bool IsLight = isLight;
        public int Count = 1;
        public readonly int Generation = generation;
        public readonly CancellationTokenSource Cancellation = new();
        public VisualLease? Lease;
        public int Finished;
        public Task Completion = Task.CompletedTask;
    }
    public void Register(LauncherItemViewModel item, double scaling)
    {
        if (item.IsCalendarIcon || string.IsNullOrWhiteSpace(item.IconName)) return;
        if (item.IconBitmap != null) return;
        if (_requests.ContainsKey(item)) return;
        // Capture UI-owned appearance before dispatching decode work. Workers
        // must not synchronously invoke the UI dispatcher while holding a slot.
        var request = new Request(_generation, item.IconName, DesktopItemLoader.IsCurrentThemeLight());
        _requests.Add(item, request);
        request.Completion = LoadAsync(item, request, scaling);
    }
    public Task WhenReady => Task.WhenAll(_requests.Values.Select(request => request.Completion));
    public void Preload(IEnumerable<LauncherItemViewModel> items, double scaling)
    {
        var ordered = new List<LauncherItemViewModel>();
        var catalog = new HashSet<LauncherItemViewModel>();
        foreach (var item in items)
        {
            if (catalog.Add(item)) ordered.Add(item);
        }
        foreach (var item in _requests.Keys.Where(item => !catalog.Contains(item)).ToArray()) Unregister(item);
        foreach (var item in ordered)
            if (_requests.TryGetValue(item, out var request) && request.IconName != item.IconName) Unregister(item);
        foreach (var item in ordered) Register(item, scaling);
    }
    private async Task LoadAsync(LauncherItemViewModel item, Request request, double scaling)
    {
        var token = request.Cancellation.Token;
        VisualLease? lease = null;
        try
        {
            await Workers.WaitAsync(token);
            try
            {
                lease = await Task.Run(() =>
                {
                    token.ThrowIfCancellationRequested();
                    using var trace = ResponsivenessTrace.Measure("launcher.icon.decode", item.App.Id);
                    var path = DesktopItemLoader.ResolveIconPath(request.IconName, request.IsLight);
                    if (string.IsNullOrWhiteSpace(path)) return null;
                    var resource = path.StartsWith("avares:", StringComparison.Ordinal);
                    var info = resource ? null : new FileInfo(path);
                    var snapshot = new FileEntrySnapshot(path, path, Path.GetExtension(path), false, 0,
                        info?.Length ?? 0, info?.LastWriteTimeUtc.Ticks ?? request.Generation);
                    if (snapshot.Length > 16 * 1024 * 1024) return null;
                    return _cache.Acquire(snapshot, 128, () =>
                    {
                        using var stream = resource ? AssetLoader.Open(new Uri(path)) : File.OpenRead(path);
                        Bitmap? bitmap = Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
                            ? DesktopItemLoader.LoadSvgBitmap(stream, 128)
                            : DecodeBitmap(stream);
                        return new FileVisual(bitmap, null, string.Empty);
                    }, $"{request.Generation}:{scaling.ToString(global::System.Globalization.CultureInfo.InvariantCulture)}");
                }, token);
            }
            finally { Workers.Release(); }
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested || request.Generation != _generation ||
                    !_requests.TryGetValue(item, out var current) || !ReferenceEquals(current, request)) return;
                request.Lease = lease;
                item.IconBitmap = lease?.Visual.Thumbnail;
                lease = null;
            }, DispatcherPriority.Background);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        finally
        {
            lease?.Dispose();
            lock (request)
            {
                request.Finished = 1;
                if (request.Count == 0) request.Cancellation.Dispose();
            }
        }
    }
    private static Bitmap? DecodeBitmap(Stream stream)
    {
        using var codec = SkiaSharp.SKCodec.Create(stream);
        if (codec is null || codec.Info.Width <= 0 || codec.Info.Height <= 0) return null;
        if ((long)codec.Info.Width * codec.Info.Height > 4 * 1024 * 1024) return null;
        var scale = Math.Min(1.0, 128.0 / Math.Max(codec.Info.Width, codec.Info.Height));
        var targetWidth = Math.Max(1, (int)Math.Round(codec.Info.Width * scale));
        var targetHeight = Math.Max(1, (int)Math.Round(codec.Info.Height * scale));
        var sourceInfo = new SkiaSharp.SKImageInfo(codec.Info.Width, codec.Info.Height, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using var source = SkiaSharp.SKBitmap.Decode(codec, sourceInfo);
        if (source is null) return null;
        if (source.Width == targetWidth && source.Height == targetHeight)
        {
            return new Bitmap(
                PixelFormat.Bgra8888,
                AlphaFormat.Premul,
                source.GetPixels(),
                new Avalonia.PixelSize(source.Width, source.Height),
                new Avalonia.Vector(96, 96),
                source.RowBytes);
        }
        var targetInfo = new SkiaSharp.SKImageInfo(targetWidth, targetHeight, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul);
        using var pixels = source.Resize(targetInfo, new SkiaSharp.SKSamplingOptions(SkiaSharp.SKCubicResampler.Mitchell));
        if (pixels is null) return null;
        return new Bitmap(
            PixelFormat.Bgra8888,
            AlphaFormat.Premul,
            pixels.GetPixels(),
            new Avalonia.PixelSize(pixels.Width, pixels.Height),
            new Avalonia.Vector(96, 96),
            pixels.RowBytes);
    }
    public void Unregister(LauncherItemViewModel item)
    {
        if (!_requests.TryGetValue(item, out var request)) return;
        lock (request)
        {
            if (--request.Count > 0) return;
            _requests.Remove(item);
            item.IconBitmap = null;
            request.Cancellation.Cancel();
            request.Lease?.Dispose();
            if (request.Finished != 0) request.Cancellation.Dispose();
        }
    }
    public void Dispose()
    {
        _generation++;
        foreach (var pair in _requests.ToArray())
        {
            pair.Value.Count = 1;
            Unregister(pair.Key);
        }
        _cache.Clear();
    }
}
