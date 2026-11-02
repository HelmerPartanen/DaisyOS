using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services.Diagnostics;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LauncherIconLoaderTests
{
    [AvaloniaFact]
    public async Task GamingSuspensionCancelsCatalogWorkAndResumeDoesNotStartABurst()
    {
        using var loader = new LauncherIconLoader();
        var items = Enumerable.Range(0, 32).Select(i =>
            new LauncherItemViewModel(new AppEntry($"suspended-{i}", $"Item {i}", "", "notes", false))).ToArray();
        loader.Preload(items, 1);
        var pending = loader.WhenReady;
        loader.SetBackgroundSuspended(true, []);
        await WaitForCompletion(pending);
        Assert.All(items, item => Assert.Null(item.IconBitmap));
        loader.Preload(items, 1);
        await WaitForCompletion(loader.WhenReady);
        Assert.All(items, item => Assert.Null(item.IconBitmap));
        loader.SetBackgroundSuspended(false, []);
        Dispatcher.UIThread.RunJobs();
        Assert.All(items, item => Assert.Null(item.IconBitmap));
        // A deliberately requested visible tile can still load during gaming.
        loader.SetBackgroundSuspended(true, []);
        loader.Register(items[0], 1);
        await WaitForIcon(items[0], loader.WhenReady);
        Assert.All(items.Skip(1), item => Assert.Null(item.IconBitmap));
    }

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

    [AvaloniaFact]
    public async Task ReplacingOrCancellingABackgroundPreloadCannotRestoreStaleIcons()
    {
        using var loader = new LauncherIconLoader();
        var old = Enumerable.Range(0, 24).Select(i =>
            new LauncherItemViewModel(new AppEntry($"old-{i}", $"Old {i}", "", "notes", false))).ToArray();
        var current = new LauncherItemViewModel(new AppEntry("current", "Current", "", "files", false));
        loader.Preload(old, 1);
        var superseded = loader.WhenReady;
        loader.Preload([current], 1);
        await WaitForIcon(current, Task.WhenAll(superseded, loader.WhenReady));
        Assert.All(old, item => Assert.Null(item.IconBitmap));
        loader.Preload(old, 1);
        var cancelled = loader.WhenReady;
        loader.Dispose();
        for (var attempt = 0; attempt < 500 && !cancelled.IsCompleted; attempt++)
        {
            await Task.Delay(10);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(cancelled.IsCompleted);
        await cancelled;
        Assert.All(old, item => Assert.Null(item.IconBitmap));
        Assert.Null(current.IconBitmap);
    }

    private static async Task WaitForCompletion(Task completion)
    {
        for (var i = 0; i < 250 && !completion.IsCompleted; i++)
        { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
        Assert.True(completion.IsCompleted);
        await completion;
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
