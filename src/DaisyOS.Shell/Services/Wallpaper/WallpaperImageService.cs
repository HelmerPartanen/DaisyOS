using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>
/// Diagnostic counters for wallpaper rendering operations.
/// </summary>
public sealed class WallpaperDiagnostics
{
    /// <summary>Number of times a file was read from disk.</summary>
    public int FileReadCount { get; internal set; }

    /// <summary>Number of times an image was decoded from a stream.</summary>
    public int BitmapDecodeCount { get; internal set; }

    /// <summary>Number of times a crop/scale pre-render operation was performed.</summary>
    public int CropScaleCount { get; internal set; }

    /// <summary>Number of times the cached bitmap was replaced.</summary>
    public int CacheReplacementCount { get; internal set; }

    /// <summary>Number of times a render call was observed (for information only).</summary>
    public int RenderCallCount { get; internal set; }

    public void Reset()
    {
        FileReadCount = 0;
        BitmapDecodeCount = 0;
        CropScaleCount = 0;
        CacheReplacementCount = 0;
        RenderCallCount = 0;
    }

    public override string ToString()
        => $"FileReads={FileReadCount}, Decodes={BitmapDecodeCount}, CropScales={CropScaleCount}, Replacements={CacheReplacementCount}, Renders={RenderCallCount}";
}

/// <summary>
/// Immutable description of the target surface for wallpaper pre-rendering.
/// </summary>
public readonly record struct WallpaperRenderMetrics(
    int PhysicalWidth,
    int PhysicalHeight,
    double Scaling,
    Stretch Stretch)
{
    public static WallpaperRenderMetrics FromPixelSize(int pixelWidth, int pixelHeight, double scaling, Stretch stretch)
        => new(pixelWidth, pixelHeight, scaling, stretch);

    public bool Equals(WallpaperRenderMetrics other)
        => PhysicalWidth == other.PhysicalWidth
        && PhysicalHeight == other.PhysicalHeight
        && Scaling.Equals(other.Scaling)
        && Stretch == other.Stretch;

    public override int GetHashCode()
        => HashCode.Combine(PhysicalWidth, PhysicalHeight, Scaling.GetHashCode(), Stretch.GetHashCode());
}

/// <summary>
/// High-performance wallpaper image service that loads, decodes, and pre-renders
/// the wallpaper bitmap once per wallpaper path and monitor configuration.
/// 
/// Expensive work (file I/O, decode, crop/scale) happens exactly once per unique
/// combination of wallpaper path and render metrics. The resulting cached bitmap
/// is assigned directly to Image.Source and reused for all subsequent renders.
/// 
/// Normal Avalonia compositor redraws are expected and not prevented.
/// </summary>
public sealed class WallpaperImageService : IAsyncDisposable
{
    private string _wallpaperPath;
    private readonly WallpaperDiagnostics _diagnostics;

    /// <summary>
    /// Currently cached pre-rendered bitmap assigned to the Image control.
    /// </summary>
    private Bitmap? _cachedBitmap;
    // Avalonia may still be compositing the previous bitmap when a monitor reports its
    // settled size. The layer retires superseded targets after the replacement
    // has reached the render thread.
    private readonly List<Bitmap> _supersededBitmaps = [];

    /// <summary>
    /// The render metrics used to produce the current cached bitmap.
    /// </summary>
    private WallpaperRenderMetrics _currentMetrics;

    /// <summary>
    /// Cancellation token source for in-flight regeneration operations.
    /// </summary>
    private CancellationTokenSource? _regenCts;
    private Task<Bitmap?>? _generationTask;
    private WallpaperRenderMetrics _generationMetrics;

    /// <summary>
    /// Lock to protect access to the cached bitmap and metrics during swaps.
    /// </summary>
    private readonly object _swapLock = new();
    private bool _disposed;

    /// <summary>
    /// Gets the current wallpaper path.
    /// </summary>
    public string WallpaperPath => _wallpaperPath;

    /// <summary>
    /// Gets the diagnostic counters for this service instance.
    /// </summary>
    public WallpaperDiagnostics Diagnostics => _diagnostics;

    /// <summary>
    /// Gets the currently cached bitmap, or null if none has been generated yet.
    /// This property returns the stored field and does not perform any I/O or allocation.
    /// </summary>
    public Bitmap? WallpaperBitmap
    {
        get
        {
            // Track render calls for diagnostics - reading the property is not a render,
            // but we count when the bitmap is actually used by the compositor.
            return _cachedBitmap;
        }
    }

    /// <summary>Transfers old render targets to the presenter for retirement after rendering.</summary>
    internal Bitmap[] TakeSupersededBitmaps()
    {
        lock (_swapLock)
        {
            var retired = _supersededBitmaps.ToArray();
            _supersededBitmaps.Clear();
            return retired;
        }
    }

    /// <summary>
    /// Event raised when the cached wallpaper bitmap is replaced.
    /// </summary>
    public event EventHandler<Bitmap?>? WallpaperBitmapChanged;

    public WallpaperImageService(string wallpaperPath, WallpaperDiagnostics? diagnostics = null)
    {
        if (string.IsNullOrWhiteSpace(wallpaperPath))
            throw new ArgumentException("Wallpaper path cannot be null or empty.", nameof(wallpaperPath));

        _wallpaperPath = wallpaperPath;
        _diagnostics = diagnostics ?? new WallpaperDiagnostics();
        _currentMetrics = default;
    }

    /// <summary>
    /// Gets or creates the cached wallpaper bitmap for the specified render metrics.
    /// If the metrics match the current cache, the existing bitmap is returned immediately.
    /// Otherwise, a new bitmap is generated asynchronously.
    /// </summary>
    public async ValueTask<Bitmap?> GetWallpaperAsync(WallpaperRenderMetrics metrics, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Task<Bitmap?> generation;
        lock (_swapLock)
        {
            if (_cachedBitmap is not null && _currentMetrics.Equals(metrics)) return _cachedBitmap;
            if (_generationTask is { IsCompleted: false } && _generationMetrics.Equals(metrics))
                generation = _generationTask;
            else
            {
                _regenCts?.Cancel();
                _regenCts?.Dispose();
                _regenCts = new CancellationTokenSource();
                _generationMetrics = metrics;
                generation = _generationTask = GenerateWallpaperAsync(metrics, _regenCts.Token).AsTask();
            }
        }
        try { return await generation.WaitAsync(cancellationToken).ConfigureAwait(false); }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_swapLock) return _cachedBitmap;
        }
    }

    /// <summary>
    /// Synchronously gets the cached wallpaper bitmap if metrics match, otherwise returns null.
    /// Use this from UI-bound code when you need the current bitmap without awaiting.
    /// </summary>
    public Bitmap? GetCurrentWallpaper(WallpaperRenderMetrics metrics)
    {
        lock (_swapLock)
        {
            return _currentMetrics.Equals(metrics) ? _cachedBitmap : null;
        }
    }

    /// <summary>
    /// Forces regeneration of the wallpaper bitmap with new metrics.
    /// The previous bitmap is disposed after the new one is ready.
    /// </summary>
    public async ValueTask<Bitmap?> RefreshWallpaperAsync(WallpaperRenderMetrics metrics, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Cancel any in-flight regeneration
        _regenCts?.Cancel();
        _regenCts?.Dispose();
        _regenCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _regenCts.Token);
        var ct = linkedCts.Token;

        try
        {
            return await GenerateWallpaperAsync(metrics, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            lock (_swapLock)
            {
                return _cachedBitmap;
            }
        }
    }

    /// <summary>
    /// Core method that generates the wallpaper bitmap. This performs the expensive
    /// operations: file read, decode, and pre-rendering.
    /// </summary>
    private async ValueTask<Bitmap?> GenerateWallpaperAsync(WallpaperRenderMetrics metrics, CancellationToken cancellationToken)
    {
        // Step 1: Load and decode the source bitmap (once per path globally)
        using var sourceLease = await WallpaperSourceCache.AcquireAsync(_wallpaperPath, _diagnostics, cancellationToken).ConfigureAwait(false);
        var sourceBitmap = sourceLease?.Bitmap;
        if (sourceBitmap is null || cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        // Step 2: Pre-render to target size if needed
        Bitmap? resultBitmap;

        // Always create a service-owned render bitmap. Returning the shared source cache
        // directly lets one wallpaper instance dispose a bitmap another consumer still uses.
        resultBitmap = await PreRenderBitmapAsync(sourceBitmap, metrics, cancellationToken).ConfigureAwait(false);

        if (resultBitmap is null)
        {
            return null;
        }

        // Step 3: Swap the cached bitmap atomically
        Bitmap? oldBitmap = null;
        lock (_swapLock)
        {
            if (_disposed || cancellationToken.IsCancellationRequested)
            {
                resultBitmap.Dispose();
                return null;
            }
            oldBitmap = _cachedBitmap;
            if (oldBitmap is not null && oldBitmap != resultBitmap)
                _supersededBitmaps.Add(oldBitmap);
            _cachedBitmap = resultBitmap;
            _currentMetrics = metrics;
            _diagnostics.CacheReplacementCount++;
        }

        // Raise change notification on UI thread
        if (Dispatcher.UIThread.CheckAccess())
        {
            WallpaperBitmapChanged?.Invoke(this, resultBitmap);
        }
        else
        {
            await Dispatcher.UIThread.InvokeAsync(() => WallpaperBitmapChanged?.Invoke(this, resultBitmap));
        }

        return resultBitmap;
    }

    /// <summary>
    /// Pre-renders the wallpaper to the target size with UniformToFill cropping.
    /// Uses a DrawingContext approach for reliable rendering in Avalonia 12.
    /// </summary>
    private async Task<Bitmap?> PreRenderBitmapAsync(Bitmap source, WallpaperRenderMetrics metrics, CancellationToken cancellationToken)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            _diagnostics.CropScaleCount++;

            RenderTargetBitmap? renderTarget = null;
            try
            {
                var sourceSize = source.Size;
                var targetWidth = metrics.PhysicalWidth;
                var targetHeight = metrics.PhysicalHeight;

                // Calculate the source rectangle for UniformToFill
                // This preserves aspect ratio and center-crops
                var sourceRect = CalculateUniformToFillRect(sourceSize.Width, sourceSize.Height, targetWidth, targetHeight);

                // Create a render target bitmap at the target size
                renderTarget = new RenderTargetBitmap(new PixelSize(targetWidth, targetHeight));

                // Use a DrawingContext to draw the cropped and scaled bitmap
                using (var context = renderTarget.CreateDrawingContext())
                {
                    // Draw the image with cropping and scaling
                    // DrawImage in Avalonia 12 takes (IBitmap, Rect, Rect)
                    context.DrawImage(
                        source,
                        sourceRect,  // Source rectangle (crop area)
                        new Rect(0, 0, targetWidth, targetHeight));  // Destination rectangle
                }

                return renderTarget;
            }
            catch
            {
                renderTarget?.Dispose();
                return null;
            }
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Calculates the source rectangle for UniformToFill stretching.
    /// </summary>
    private static Rect CalculateUniformToFillRect(double sourceWidth, double sourceHeight, double targetWidth, double targetHeight)
    {
        double sourceAspect = sourceWidth / sourceHeight;
        double targetAspect = targetWidth / targetHeight;

        double cropWidth;
        double cropHeight;

        if (sourceAspect > targetAspect)
        {
            // Source is wider - crop horizontally
            // Scale source to fill target height, then crop width
            cropHeight = sourceHeight;
            cropWidth = targetWidth * (sourceHeight / targetHeight);
        }
        else
        {
            // Source is taller - crop vertically
            // Scale source to fill target width, then crop height
            cropWidth = sourceWidth;
            cropHeight = targetHeight * (sourceWidth / targetWidth);
        }

        // Center the crop
        var x = (sourceWidth - cropWidth) / 2.0;
        var y = (sourceHeight - cropHeight) / 2.0;

        return new Rect(x, y, cropWidth, cropHeight);
    }

    /// <summary>
    /// Replaces the wallpaper with a new path and regenerates the bitmap.
    /// </summary>
    public async ValueTask<Bitmap?> SetWallpaperAsync(string newPath, WallpaperRenderMetrics metrics, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.Equals(_wallpaperPath, newPath, StringComparison.Ordinal))
        {
            // Same path, just refresh with new metrics
            return await RefreshWallpaperAsync(metrics, cancellationToken).ConfigureAwait(false);
        }

        // Cancel any in-flight operations
        _regenCts?.Cancel();
        _regenCts?.Dispose();
        _regenCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _regenCts.Token);

        try
        {
            // Load source for new path
            using var sourceLease = await WallpaperSourceCache.AcquireAsync(newPath, _diagnostics, linkedCts.Token).ConfigureAwait(false);
            var newSource = sourceLease?.Bitmap;
            if (newSource is null)
            {
                return null;
            }

            // Like the normal load path, each service owns its rendered bitmap rather than
            // returning the shared decoded source directly.
            Bitmap? resultBitmap = await PreRenderBitmapAsync(newSource, metrics, linkedCts.Token).ConfigureAwait(false);

            if (resultBitmap is null)
            {
                return null;
            }

            // Swap
            Bitmap? oldBitmap = null;
            lock (_swapLock)
            {
                if (_disposed || linkedCts.IsCancellationRequested)
                {
                    resultBitmap.Dispose();
                    return null;
                }
                oldBitmap = _cachedBitmap;
                if (oldBitmap is not null && oldBitmap != resultBitmap)
                    _supersededBitmaps.Add(oldBitmap);
                _cachedBitmap = resultBitmap;
                _currentMetrics = metrics;
                _diagnostics.CacheReplacementCount++;
            }

            _wallpaperPath = newPath;

            WallpaperBitmapChanged?.Invoke(this, resultBitmap);
            return resultBitmap;
        }
        catch (OperationCanceledException)
        {
            if (cancellationToken.IsCancellationRequested) throw;
            lock (_swapLock)
            {
                return _cachedBitmap;
            }
        }
    }

    /// <summary>
    /// Disposes the cached bitmap and releases resources.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _regenCts?.Cancel();

        List<Bitmap> bitmapsToDispose;
        lock (_swapLock)
        {
            _disposed = true;
            bitmapsToDispose = [.. _supersededBitmaps];
            _supersededBitmaps.Clear();
            if (_cachedBitmap is not null)
            {
                bitmapsToDispose.Add(_cachedBitmap);
            }
            _cachedBitmap = null;
            _currentMetrics = default;
        }

        if (bitmapsToDispose.Count > 0)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                foreach (var bitmap in bitmapsToDispose) bitmap.Dispose();
            }
            else
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    foreach (var bitmap in bitmapsToDispose) bitmap.Dispose();
                });
            }
        }

        _regenCts?.Dispose();
        _regenCts = null;
    }

    /// <summary>
    /// Synchronously disposes resources.
    /// </summary>
    public void Dispose()
    {
        _regenCts?.Cancel();

        List<Bitmap> bitmapsToDispose;
        lock (_swapLock)
        {
            _disposed = true;
            bitmapsToDispose = [.. _supersededBitmaps];
            _supersededBitmaps.Clear();
            if (_cachedBitmap is not null)
            {
                bitmapsToDispose.Add(_cachedBitmap);
            }
            _cachedBitmap = null;
            _currentMetrics = default;
        }

        foreach (var bitmap in bitmapsToDispose) bitmap.Dispose();
        _regenCts?.Dispose();
        _regenCts = null;
    }
}
