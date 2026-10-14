using System;
using System.Threading.Tasks;
using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class VideoWallpaperServiceTests
{
    [Fact]
    public void VideoWallpaperService_CanInstantiateAndDispose()
    {
        using var service = new VideoWallpaperService();
        Assert.True(service.IsLoaded);
    }

    [Fact]
    public void VideoWallpaperService_LoadNonExistentFile_ReturnsFalse()
    {
        using var service = new VideoWallpaperService();
        bool result = service.Load("/nonexistent/video.mp4");
        Assert.False(result);
    }

    [Fact]
    public void VideoWallpaperService_LoadFixture_Succeeds()
    {
        var videoPath = global::System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "video.mp4");
        Assert.True(global::System.IO.File.Exists(videoPath));

        using var service = new VideoWallpaperService();
        bool loaded = service.Load(videoPath);
        Assert.True(loaded);

        service.Play();
        bool gotStats = service.GetStats(out var stats);
        Assert.True(gotStats);
        Assert.Equal(32, stats.SrcWidth);
        Assert.Equal(32, stats.SrcHeight);
        Assert.False(string.IsNullOrEmpty(stats.CodecString));

        service.Pause();
        Assert.True(service.IsPaused);

        service.Play();
        Assert.False(service.IsPaused);

        service.Stop();
    }

    [Fact]
    public void VideoWallpaperService_Pause_SetsIsPausedTrue()
    {
        using var service = new VideoWallpaperService();
        Assert.False(service.IsPaused);
        service.Pause();
        Assert.True(service.IsPaused);
        service.Play();
        Assert.False(service.IsPaused);
    }

    [Fact]
    public async Task VideoWallpaperService_ValidatesPlayableVideoBeforeItIsApplied()
    {
        var videoPath = global::System.IO.Path.Combine(AppContext.BaseDirectory, "Fixtures", "video.mp4");
        Assert.True(global::System.IO.File.Exists(videoPath));

        Assert.True(await VideoWallpaperService.CanPlayAsync(videoPath, TimeSpan.FromSeconds(2)));
        Assert.False(await VideoWallpaperService.CanPlayAsync("/nonexistent/video.mp4", TimeSpan.FromMilliseconds(50)));
    }
}
