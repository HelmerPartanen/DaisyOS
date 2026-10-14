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
