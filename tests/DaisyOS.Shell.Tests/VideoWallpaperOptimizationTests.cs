using System.Diagnostics;
using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class VideoWallpaperOptimizationTests
{
    [Fact]
    public async Task OptimizedCopyPreservesColorSpeedAndOriginalAndReusesCache()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Path.Combine(Path.GetTempPath(), "daisy-wallpaper-test-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var previousCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        Environment.SetEnvironmentVariable("XDG_CACHE_HOME", directory);
        try
        {
            var source = Path.Combine(directory, "p3.mp4");
            var start = new ProcessStartInfo("ffmpeg") { RedirectStandardError = true, UseShellExecute = false };
            foreach (var arg in new[] { "-v", "error", "-f", "lavfi", "-i", "testsrc2=size=128x72:rate=120", "-t", "1",
                "-vf", "setparams=color_primaries=smpte432:color_trc=bt470m:colorspace=bt709:range=limited", "-c:v", "libx265", "-pix_fmt", "yuv420p10le", "-preset", "fast", "-x265-params", "pools=none:frame-threads=2:log-level=error",
                "-color_primaries", "smpte432", "-color_trc", "bt470m", "-colorspace", "bt709", "-color_range", "tv", "-an", "-y", source }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();
            Assert.True(process.ExitCode == 0, error);
            var original = await File.ReadAllBytesAsync(source);
            var metadata = await VideoMetadata.ProbeAsync(source);
            Assert.Equal("smpte432", metadata.Primaries);
            Assert.Equal("bt470m", metadata.Transfer);
            var cached = await VideoWallpaperOptimization.PrepareAsync(source, metadata, 128, 72, false, CancellationToken.None);
            Assert.NotEqual(source, cached);
            var optimized = await VideoMetadata.ProbeAsync(cached);
            Assert.Equal(VideoWallpaperOptimization.PlaybackFramesPerSecond, optimized.Fps);
            Assert.Equal(metadata.Transfer, optimized.Transfer);
            Assert.Equal(metadata.Primaries, optimized.Primaries);
            Assert.Equal(metadata.Matrix, optimized.Matrix);
            Assert.Equal(metadata.Range, optimized.Range);
            Assert.InRange(Math.Abs(metadata.Duration - optimized.Duration), 0, .02);
            Assert.Equal(original, await File.ReadAllBytesAsync(source));
            var written = File.GetLastWriteTimeUtc(cached);
            Assert.Equal(cached, await VideoWallpaperOptimization.PrepareAsync(source, metadata, 128, 72, false, CancellationToken.None));
            Assert.Equal(written, File.GetLastWriteTimeUtc(cached));
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_CACHE_HOME", previousCache);
            Directory.Delete(directory, true);
        }
    }
}
