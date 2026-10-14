using DaisyOS.Shell.Services.Wallpaper;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class WallpaperFileTypesTests
{
    [Theory]
    [InlineData("/wallpapers/forest.jpg")]
    [InlineData("/wallpapers/forest.JPEG")]
    [InlineData("/wallpapers/forest.webp")]
    public void StillImages_AreNotTreatedAsVideos(string path)
    {
        Assert.True(WallpaperFileTypes.IsImage(path));
        Assert.False(WallpaperFileTypes.IsVideo(path));
    }

    [Theory]
    [InlineData("/wallpapers/ocean.mp4")]
    [InlineData("/wallpapers/ocean.WEBM")]
    [InlineData("/wallpapers/ocean.m4v")]
    [InlineData("/wallpapers/ocean.avi")]
    public void SupportedVideos_AreRecognized(string path)
    {
        Assert.True(WallpaperFileTypes.IsVideo(path));
        Assert.False(WallpaperFileTypes.IsImage(path));
    }

    [Fact]
    public void DefaultWallpaperPickerFilter_IncludesImagesAndVideos()
    {
        Assert.Contains("*.jpg", WallpaperFileTypes.WallpaperPatterns);
        Assert.Contains("*.mp4", WallpaperFileTypes.WallpaperPatterns);
    }
}
