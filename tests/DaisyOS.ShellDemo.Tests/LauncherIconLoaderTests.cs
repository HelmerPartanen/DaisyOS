using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LauncherIconLoaderTests
{
    [Fact]
    public void SystemMonitorUsesNativeThemeIcons()
    {
        Assert.EndsWith("System_Monitor_Light.png", DesktopItemLoader.GetAppIconPath("org.daisyos.taskmanager", isLight: true));
        Assert.EndsWith("System_Monitor_Dark.png", DesktopItemLoader.GetAppIconPath("taskmanager", isLight: false));
    }

    [AvaloniaFact]
    public async Task HiddenLauncherCanReleaseIconsAndReloadThemOnNextOpen()
    {
        var directory = Directory.CreateTempSubdirectory("daisy-launcher-reclaim-");
        var icon = Path.Combine(directory.FullName, "fixture.png");
        using (var pixels = new SkiaSharp.SKBitmap(8, 8))
        {
            pixels.Erase(SkiaSharp.SKColors.CornflowerBlue);
            using var encoded = pixels.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
            using var output = File.Create(icon);
            encoded.SaveTo(output);
        }
        var model = new LauncherViewModel(new DaisyOS.Shell.Services.ShellSessionState(Path.Combine(directory.FullName, "state.json")),
            [new AppEntry("fixture", "Fixture", "", icon, false)]);
        var view = new DaisyOS.Shell.Views.Components.Launcher.LauncherView(model) { IsVisible = false };
        var window = new Avalonia.Controls.Window { Content = view, Width = 900, Height = 800 };
        window.Show();
        try
        {
            var item = Assert.Single(model.AllApps);
            for (var i = 0; i < 250 && item.IconBitmap is null; i++)
            { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            Assert.NotNull(item.IconBitmap);
            view.ReclaimHiddenIcons();
            Assert.Null(item.IconBitmap);
            view.AnimateIn();
            for (var i = 0; i < 250 && item.IconBitmap is null; i++)
            { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            Assert.NotNull(item.IconBitmap);
            view.ReclaimHiddenIcons();
            Assert.NotNull(item.IconBitmap);
        }
        finally { view.CloseImmediate(); view.ReclaimHiddenIcons(); window.Close(); directory.Delete(true); }
    }

    [AvaloniaFact]
    public async Task CatalogIconsLoadWithoutRealizedTilesAndRefreshWhenChanged()
    {
        using var loader = new LauncherIconLoader();
        var item = new LauncherItemViewModel(new AppEntry("notes", "Notes", "", "notes", false));
        loader.Preload([item], 1);
        await WaitForIcon(item, loader.WhenReady);
        var original = item.IconBitmap;
        loader.Preload([item], 1);
        Assert.Same(original, item.IconBitmap);
        item.UpdateApp(item.App with { Icon = "files" });
        loader.Preload([item], 1);
        await WaitForIcon(item, loader.WhenReady);
        Assert.NotSame(original, item.IconBitmap);
        loader.Preload([], 1);
        Assert.Null(item.IconBitmap);
    }

    private static async Task WaitForIcon(LauncherItemViewModel item, Task completion)
    {
        for (var attempt = 0; attempt < 500 && !completion.IsCompleted; attempt++)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(completion.IsCompleted, "Icon loading did not complete while the UI dispatcher was pumped.");
        await completion;
        Dispatcher.UIThread.RunJobs();
        Assert.NotNull(item.IconBitmap);
    }
}
