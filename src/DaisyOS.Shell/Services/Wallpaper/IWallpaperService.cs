namespace DaisyOS.Shell.Services.Wallpaper;

public interface IWallpaperService
{
    string CurrentWallpaperUri { get; }

    event EventHandler<string>? WallpaperChanged;

    Task SetWallpaperAsync(string wallpaperUri, CancellationToken cancellationToken = default);
}
