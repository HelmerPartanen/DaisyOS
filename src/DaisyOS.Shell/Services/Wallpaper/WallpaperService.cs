namespace DaisyOS.Shell.Services.Wallpaper;

/// <summary>Shell-owned wallpaper state and change notification point.</summary>
public sealed class WallpaperService : IWallpaperService
{
    public WallpaperService(string initialWallpaperUri)
    {
        CurrentWallpaperUri = initialWallpaperUri;
    }

    public string CurrentWallpaperUri { get; private set; }

    public event EventHandler<string>? WallpaperChanged;

    public Task SetWallpaperAsync(string wallpaperUri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(wallpaperUri))
        {
            throw new ArgumentException("A wallpaper URI is required.", nameof(wallpaperUri));
        }

        if (string.Equals(CurrentWallpaperUri, wallpaperUri, StringComparison.Ordinal))
        {
            return Task.CompletedTask;
        }

        CurrentWallpaperUri = wallpaperUri;
        WallpaperChanged?.Invoke(this, wallpaperUri);
        return Task.CompletedTask;
    }
}
