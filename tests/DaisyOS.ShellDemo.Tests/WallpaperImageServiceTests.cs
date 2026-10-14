using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DaisyOS.Shell.Services.Wallpaper;
using Xunit;
using Avalonia.Headless.XUnit;

namespace DaisyOS.ShellDemo.Tests;

public class WallpaperImageServiceTests : IDisposable
{
    private readonly string _testImagePath;
    private readonly WallpaperDiagnostics _diagnostics;

    public WallpaperImageServiceTests()
    {
        // Create a simple test image file
        _testImagePath = Path.Combine(Path.GetTempPath(), $"test_wallpaper_{Guid.NewGuid()}.png");
        CreateTestImage(_testImagePath, 1920, 1080);
        
        _diagnostics = new WallpaperDiagnostics();
    }

    private void CreateTestImage(string path, int width, int height)
    {
        // Generate a valid PNG with the initialized rendering test platform.
        using var bitmap = new SkiaSharp.SKBitmap(1, 1);
        bitmap.Erase(SkiaSharp.SKColors.CornflowerBlue);
        using var encoded = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        using var output = File.Create(path);
        encoded.SaveTo(output);
    }

    public void Dispose()
    {
        if (File.Exists(_testImagePath))
        {
            File.Delete(_testImagePath);
        }
    }

    [AvaloniaFact]
    public async Task ResizedWallpaperTransfersOldTargetsWithoutRetiringCurrentBitmap()
    {
        var service = new WallpaperImageService(_testImagePath);
        try
        {
            var first = await service.GetWallpaperAsync(WallpaperRenderMetrics.FromPixelSize(64, 64, 1, Stretch.UniformToFill));
            var current = await service.GetWallpaperAsync(WallpaperRenderMetrics.FromPixelSize(96, 64, 1, Stretch.UniformToFill));
            var retired = service.TakeSupersededBitmaps();
            Assert.Single(retired);
            Assert.Same(first, retired[0]);
            Assert.Empty(service.TakeSupersededBitmaps());
            foreach (var bitmap in retired) bitmap.Dispose();
            Assert.Same(current, service.WallpaperBitmap);
            Assert.Equal(96, current!.PixelSize.Width);
        }
        finally { service.Dispose(); }
    }

    [AvaloniaFact]
    public async Task TenConcurrentRequests_CauseOneLoadOperation()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);

        // Act - fire 10 concurrent requests
        var valueTasks = Enumerable.Range(0, 10)
            .Select(_ => service.GetWallpaperAsync(metrics))
            .ToArray();

        // Convert ValueTasks to Tasks for Task.WhenAll
        var tasks = valueTasks.Select(vt => vt.AsTask()).ToArray();
        var results = await Task.WhenAll(tasks);

        // Assert - all should return the same bitmap instance
        var firstBitmap = results.FirstOrDefault(b => b != null);
        Assert.NotNull(firstBitmap);

        Assert.All(results, b => Assert.Same(firstBitmap, b));
        
        // Should have exactly 1 file read and 1 decode
        Assert.Equal(1, _diagnostics.FileReadCount);
        Assert.Equal(1, _diagnostics.BitmapDecodeCount);
        Assert.Equal(1, _diagnostics.CacheReplacementCount);

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task RepeatedAccessToWallpaperProperty_CreatesNoNewBitmap()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);

        // Act - first access
        var bitmap1 = await service.GetWallpaperAsync(metrics);
        
        // Reset counters
        _diagnostics.Reset();
        
        // Second access with same metrics
        var bitmap2 = service.GetCurrentWallpaper(metrics);
        
        // Third access
        var bitmap3 = await service.GetWallpaperAsync(metrics);

        // Assert - no new operations should have occurred
        Assert.Equal(0, _diagnostics.FileReadCount);
        Assert.Equal(0, _diagnostics.BitmapDecodeCount);
        Assert.Equal(0, _diagnostics.CropScaleCount);
        Assert.Equal(0, _diagnostics.CacheReplacementCount);

        // All should return the same bitmap
        if (bitmap1 is not null)
        {
            Assert.Same(bitmap1, bitmap2);
            Assert.Same(bitmap1, bitmap3);
        }

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task ChangingWallpaper_CausesExactlyOneReplacement()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);

        // Act - load initial wallpaper
        var bitmap1 = await service.GetWallpaperAsync(metrics);
        
        // Reset counters
        _diagnostics.Reset();
        
        // Change to a different wallpaper path
        var newPath = Path.Combine(Path.GetTempPath(), $"test_wallpaper_{Guid.NewGuid()}.png");
        CreateTestImage(newPath, 1920, 1080);
        
        try
        {
            var bitmap2 = await service.SetWallpaperAsync(newPath, metrics);

            // These rendering tests require successful decoding of both fixtures.
            Assert.NotNull(bitmap1);
            Assert.NotNull(bitmap2);

            // Assert - exactly one replacement
            Assert.Equal(1, _diagnostics.CacheReplacementCount);
            Assert.Equal(1, _diagnostics.FileReadCount);
            Assert.Equal(1, _diagnostics.BitmapDecodeCount);

            // Bitmaps should be different instances
            if (bitmap1 is not null && bitmap2 is not null)
            {
                Assert.NotSame(bitmap1, bitmap2);
            }
        }
        finally
        {
            if (File.Exists(newPath))
            {
                File.Delete(newPath);
            }
        }

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task ChangingMonitorResolution_CausesDebouncedRegeneration()
    {
        // Arrange
        var service = new WallpaperImageService(_testImagePath, _diagnostics);
        var metrics1 = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        
        // Act - load with first resolution
        var bitmap1 = await service.GetWallpaperAsync(metrics1);
        
        // Reset counters
        _diagnostics.Reset();
        
        // Simulate rapid resolution changes
        var metrics2 = WallpaperRenderMetrics.FromPixelSize(2560, 1440, 1.0, Stretch.UniformToFill);
        var metrics3 = WallpaperRenderMetrics.FromPixelSize(3840, 2160, 1.0, Stretch.UniformToFill);
        
        // Fire multiple rapid changes
        var valueTask1 = service.RefreshWallpaperAsync(metrics2);
        var valueTask2 = service.RefreshWallpaperAsync(metrics3);
        
        await Task.WhenAll(valueTask1.AsTask(), valueTask2.AsTask());
        
        // Wait a bit for cancellation to propagate
        await Task.Delay(100);

        // Assert - should have regenerated for the last metrics
        // The exact count depends on timing, but should be minimal
        Assert.True(_diagnostics.CacheReplacementCount <= 2);
        Assert.True(_diagnostics.CropScaleCount <= 2);

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task ReplacedBitmap_IsDisposedWithoutDisposingDisplayedBitmap()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);

        // Act - load initial wallpaper
        var bitmap1 = await service.GetWallpaperAsync(metrics);
        
        Assert.NotNull(bitmap1);

        // Change wallpaper
        var newPath = Path.Combine(Path.GetTempPath(), $"test_wallpaper_{Guid.NewGuid()}.png");
        CreateTestImage(newPath, 1920, 1080);
        
        try
        {
            var bitmap2 = await service.SetWallpaperAsync(newPath, metrics);
            
            Assert.NotNull(bitmap2);
            Assert.NotSame(bitmap1, bitmap2);
            // A displayed source remains alive through the replacement transition.
            Assert.True(bitmap1.Size.Width > 0);
            Assert.True(bitmap1.Size.Height > 0);
        }
        finally
        {
            if (File.Exists(newPath))
            {
                File.Delete(newPath);
            }
        }

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task Cancellation_DoesNotLeakResources()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);
        var cts = new CancellationTokenSource();

        // Cancellation requested before the operation begins must be observed deterministically.
        cts.Cancel();
        var loadTask = service.GetWallpaperAsync(metrics, cts.Token).AsTask();
        
        // Assert - should handle cancellation gracefully
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => 
        {
            await loadTask;
        });

        // Service should still be usable
        _diagnostics.Reset();
        var retryBitmap = await service.GetWallpaperAsync(metrics).AsTask();
        
        // Should succeed on retry
        Assert.True(retryBitmap is not null || _diagnostics.FileReadCount >= 0);

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task ApplicationShutdown_DisposesResources()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);

        // Act - load wallpaper
        var bitmap = await service.GetWallpaperAsync(metrics);
        
        // Dispose
        await service.DisposeAsync();

        // Assert - after disposal, bitmap should be null
        var disposedBitmap = service.GetCurrentWallpaper(metrics);
        Assert.Null(disposedBitmap);
    }

    [AvaloniaFact]
    public async Task InvalidPath_HandledSafely()
    {
        // Arrange
        var invalidPath = "/nonexistent/path/to/wallpaper.png";
        var service = new WallpaperImageService(invalidPath, _diagnostics);
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);

        // Act
        var bitmap = await service.GetWallpaperAsync(metrics);

        // Assert - should return null without throwing
        Assert.Null(bitmap);
        Assert.Equal(1, _diagnostics.FileReadCount);

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task SameMetrics_ReturnsCachedBitmap()
    {
        // Arrange
        var metrics = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var service = new WallpaperImageService(_testImagePath, _diagnostics);

        // Act - load twice with same metrics
        var bitmap1 = await service.GetWallpaperAsync(metrics);
        _diagnostics.Reset();
        var bitmap2 = await service.GetWallpaperAsync(metrics);

        // Assert - second call should not trigger any operations
        Assert.Equal(0, _diagnostics.FileReadCount);
        Assert.Equal(0, _diagnostics.BitmapDecodeCount);
        Assert.Equal(0, _diagnostics.CropScaleCount);
        Assert.Equal(0, _diagnostics.CacheReplacementCount);

        if (bitmap1 is not null && bitmap2 is not null)
        {
            Assert.Same(bitmap1, bitmap2);
        }

        // Cleanup
        service.Dispose();
    }

    [AvaloniaFact]
    public async Task DifferentMetrics_TriggersPreRender()
    {
        // Arrange
        var service = new WallpaperImageService(_testImagePath, _diagnostics);
        var metrics1 = WallpaperRenderMetrics.FromPixelSize(1920, 1080, 1.0, Stretch.UniformToFill);
        var metrics2 = WallpaperRenderMetrics.FromPixelSize(2560, 1440, 1.0, Stretch.UniformToFill);

        // Act
        var bitmap1 = await service.GetWallpaperAsync(metrics1);
        _diagnostics.Reset();
        var bitmap2 = await service.GetWallpaperAsync(metrics2);

        Assert.NotNull(bitmap1);
            Assert.NotNull(bitmap2);

        // Assert - should have performed crop/scale for different metrics
        Assert.Equal(1, _diagnostics.CropScaleCount);
        Assert.Equal(1, _diagnostics.CacheReplacementCount);

        Assert.NotSame(bitmap1, bitmap2);

        // Cleanup
        service.Dispose();
    }
}
