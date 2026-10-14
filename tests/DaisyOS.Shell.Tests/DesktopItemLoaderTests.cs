using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class DesktopItemLoaderTests
{
    [Fact]
    public void IconQuality_PrefersApplicationArtworkOverThemeReplacement()
    {
        var official = DesktopItemLoader.GetIconQualityScore("/usr/share/icons/hicolor/256x256/apps/steam.png");
        var themed = DesktopItemLoader.GetIconQualityScore("/usr/share/icons/Qogir/scalable/apps/steam.svg");

        Assert.True(official > themed);
        Assert.True(official > DesktopItemLoader.GetIconQualityScore("/usr/share/icons/hicolor/48x48/apps/steam.png"));
        Assert.True(DesktopItemLoader.GetIconQualityScore("/usr/share/icons/hicolor/scalable/apps/example.svg") > official);
    }

    [Fact]
    public async Task IsCurrentThemeLight_WithoutApplication_ReturnsDarkFromWorkerThread()
    {
        Assert.Null(Avalonia.Application.Current);
        var isLight = await Task.Run(DesktopItemLoader.IsCurrentThemeLight)
            .WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(isLight);
    }

    [Fact]
    public void ResolveApplicationIconPath_PreservesDirectSvgPathForVectorRendering()
    {
        var iconFile = Path.Combine(Path.GetTempPath(), $"icon-{Guid.NewGuid():N}.svg");
        File.WriteAllText(iconFile, "<svg xmlns=\"http://www.w3.org/2000/svg\"/>");

        try
        {
            var path = DesktopItemLoader.ResolveApplicationIconPath([iconFile]);

            Assert.Equal(iconFile, path);
        }
        finally
        {
            File.Delete(iconFile);
        }
    }

    [Fact]
    public void FindDesktopEntryIconName_ResolvesAFlatpakStartupWmClass()
    {
        var desktopFile = Path.Combine(Path.GetTempPath(), $"desktop-entry-{Guid.NewGuid():N}.desktop");
        File.WriteAllText(desktopFile, """
            [Desktop Entry]
            Type=Application
            Name=Spotify
            Icon=com.spotify.Client
            StartupWMClass=spotify
            X-Flatpak=com.spotify.Client
            Exec=flatpak run --command=spotify com.spotify.Client
            """);

        try
        {
            var iconName = DesktopItemLoader.FindDesktopEntryIconName(["spotify"], [desktopFile]);

            Assert.Equal("com.spotify.Client", iconName);
        }
        finally
        {
            File.Delete(desktopFile);
        }
    }
}
