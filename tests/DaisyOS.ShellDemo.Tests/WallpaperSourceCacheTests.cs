using DaisyOS.Shell.Services.Wallpaper;
using Xunit;
using Avalonia.Headless.XUnit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WallpaperSourceCacheTests
{
    [AvaloniaFact]
    public async Task PressureTrimmingPreservesActiveSourcesAndReleasesThemAfterUse()
    {
        var path = Path.Combine(Path.GetTempPath(), "daisy-source-pressure-" + Guid.NewGuid() + ".png");
        WritePixel(path);
        try
        {
            using (var lease = await WallpaperSourceCache.AcquireAsync(path, new WallpaperDiagnostics(), CancellationToken.None))
            {
                Assert.NotNull(lease);
                WallpaperSourceCache.TrimUnused();
                using var shared = WallpaperSourceCache.TryAcquire(path);
                Assert.NotNull(shared);
                Assert.Same(lease.Bitmap, shared.Bitmap);
            }
            WallpaperSourceCache.TrimUnused();
            using var missing = WallpaperSourceCache.TryAcquire(path);
            Assert.Null(missing);
        }
        finally { File.Delete(path); }
    }

    private static void WritePixel(string path)
    {
        using var bitmap = new SkiaSharp.SKBitmap(1, 1);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    [AvaloniaFact]
    public async Task EvictionDoesNotDisposeSourcesThatAreStillBeingRendered()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-source-cache-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var leases = new List<WallpaperSourceCache.Lease>();
        try
        {
            for (var index = 0; index < 6; index++)
            {
                var path = Path.Combine(directory, index + ".png");
                WritePixel(path);
                var lease = await WallpaperSourceCache.AcquireAsync(path, new WallpaperDiagnostics(), CancellationToken.None);
                Assert.NotNull(lease);
                leases.Add(lease);
            }
            Assert.All(leases, lease => Assert.Equal(1, lease.Bitmap.PixelSize.Width));
        }
        finally { foreach (var lease in leases) lease.Dispose(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task ConcurrentAcquisitionsDecodeOnceAndModifiedFilesInvalidateTheCache()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-source-race-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "source.png");
        WritePixel(path);
        var diagnostics = new WallpaperDiagnostics();
        var leases = await Task.WhenAll(Enumerable.Range(0, 20).Select(_ => WallpaperSourceCache.AcquireAsync(path, diagnostics, CancellationToken.None)));
        WallpaperSourceCache.Lease? replacement = null;
        try
        {
            Assert.Equal(1, diagnostics.BitmapDecodeCount);
            Assert.All(leases, lease => Assert.Same(leases[0]!.Bitmap, lease!.Bitmap));
            File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(10));
            replacement = await WallpaperSourceCache.AcquireAsync(path, diagnostics, CancellationToken.None);
            Assert.NotNull(replacement);
            Assert.NotSame(leases[0]!.Bitmap, replacement.Bitmap);
            Assert.Equal(1, leases[0]!.Bitmap.PixelSize.Width);
            Assert.Equal(2, diagnostics.BitmapDecodeCount);
        }
        finally
        {
            replacement?.Dispose();
            foreach (var lease in leases) lease?.Dispose();
            Directory.Delete(directory, true);
        }
    }
}
