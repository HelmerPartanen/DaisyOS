using Avalonia.Media;
using DaisyOS.Shell.Services.Wallpaper;
using Xunit;

namespace DaisyOS.Tests;

public sealed class WallpaperLabelContrastTests
{
    [Fact]
    public void DarkWallpaperUsesLightDesktopCaptions() =>
        Assert.Equal(WallpaperLabelContrast.LightLabel, WallpaperLabelContrast.ForWallpaper(Color.Parse("#121D12")));

    [Fact]
    public void VeryLightWallpaperUsesDarkDesktopCaptions() =>
        Assert.Equal(WallpaperLabelContrast.DarkLabel, WallpaperLabelContrast.ForWallpaper(Color.Parse("#F1EDE5")));

    [Fact]
    public void MediumBrightWallpaperStaysLightWithIncreasedThreshold()
    {
        // #D0D0D0 has luminance ~0.63. Under the old 0.60 threshold this turned dark,
        // but with the increased 0.75 threshold it stays light with drop shadow.
        var mediumBright = Color.FromRgb(0xD0, 0xD0, 0xD0);
        Assert.Equal(WallpaperLabelContrast.LightLabel, WallpaperLabelContrast.ForWallpaper(mediumBright));
    }
}

