using Avalonia;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using DaisyOS.Shell;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DaisyIconBundleTests
{
    [Fact]
    public void EmbeddedIconsContainOnlyTreeShakenHugeIcons()
    {
        _ = typeof(DaisyIcon).Assembly;
        var loader = new StandardAssetLoader(typeof(DaisyIcon).Assembly);
        var hugeIcons = loader.GetAssets(new Uri("avares://DaisyOS.Shell/Assets/Icons/HugeIcons/"), null).ToArray();
        Assert.Contains(hugeIcons, uri => uri.AbsolutePath.EndsWith("/magnifying-glass.svg"));
        Assert.DoesNotContain(hugeIcons, uri => uri.AbsolutePath.EndsWith("/0-circle.svg"));
        Assert.Empty(loader.GetAssets(new Uri("avares://DaisyOS.Shell/Assets/Icons/Phosphor/"), null));
        Assert.InRange(hugeIcons.Count(uri => uri.AbsolutePath.EndsWith(".svg")), 1, 250);
    }

    [Fact]
    public void FillWeightResolvesToTheGeneratedFillAsset()
    {
        using var scope = AvaloniaLocator.EnterScope();
        AvaloniaLocator.CurrentMutable.Bind<ICursorFactory>().ToConstant(new DummyCursorFactory());
        AvaloniaLocator.CurrentMutable.Bind<IAssetLoader>().ToConstant(new StandardAssetLoader(typeof(DaisyIcon).Assembly));
        var icon = new DaisyIcon { Icon = "play" };
        var regular = icon.Path;
        icon.Weight = DaisyIconWeight.Fill;
        Assert.NotEqual(regular, icon.Path);
        Assert.Equal("avares://DaisyOS.Shell/Assets/Icons/HugeIcons/play-fill.svg", icon.Path);
        icon.Icon = UIIcons.Filled("play");
        Assert.Equal("avares://DaisyOS.Shell/Assets/Icons/HugeIcons/play-fill.svg", icon.Path);
    }

    [Fact]
    public void EthernetIconLoadsAndHasGeometry()
    {
        using var scope = AvaloniaLocator.EnterScope();
        AvaloniaLocator.CurrentMutable.Bind<ICursorFactory>().ToConstant(new DummyCursorFactory());
        var loader = new StandardAssetLoader(typeof(DaisyIcon).Assembly);
        AvaloniaLocator.CurrentMutable.Bind<IAssetLoader>().ToConstant(loader);
        var icon = new DaisyIcon { Icon = "Ethernet" };
        Assert.Equal("avares://DaisyOS.Shell/Assets/Icons/HugeIcons/ethernet.svg", icon.Path);
        using var stream = loader.Open(new Uri(icon.Path));
        using var reader = new global::System.IO.StreamReader(stream);
        var content = reader.ReadToEnd();
        Assert.Contains("currentColor", content);
    }

    private sealed class DummyCursorFactory : ICursorFactory
    {
        private sealed class DummyCursor : ICursorImpl { public void Dispose() { } }
        public ICursorImpl GetCursor(StandardCursorType cursorType) => new DummyCursor();
        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot) => new DummyCursor();
    }
}
