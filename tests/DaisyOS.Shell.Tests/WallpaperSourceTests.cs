using Avalonia.Media;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Wallpaper;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class WallpaperSourceTests
{
    [Fact]
    public void DefaultWallpaperIsAnOpaqueSolidColor()
    {
        Assert.True(WallpaperSource.TryGetSolidColor(new ShellSettings().WallpaperUri, out var color));
        Assert.Equal(Color.Parse("#2E3440"), color);
        Assert.Equal(string.Empty, WallpaperSource.ResolveWallpaperPath(null));
    }

    [Fact]
    public void UserFilesResolveAsPathsAndFileUris()
    {
        var path = Path.Combine(Path.GetTempPath(), $"user wallpaper {Guid.NewGuid():N}.jpg");
        File.WriteAllBytes(path, []);
        try
        {
            Assert.Equal(path, WallpaperSource.ResolveWallpaperPath(path));
            Assert.Equal(path, WallpaperSource.ResolveWallpaperPath(new Uri(path).AbsoluteUri));
        }
        finally { File.Delete(path); }
    }
}
