using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Services.Wallpaper;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Reclaims unused decoded images without forcing GC or touching active render leases.</summary>
internal static class ShellMemoryPressureMonitor
{
    public static async Task RunAsync(CancellationToken token, Action reclaimHiddenViews)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await timer.WaitForNextTickAsync(token).ConfigureAwait(false))
            {
                if (!RecentAppPrefetcher.MemoryPressure()) continue;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (!token.IsCancellationRequested) reclaimHiddenViews();
                });
                ThumbnailCache.TrimAllUnused();
                WallpaperSourceCache.TrimUnused();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
