using System.IO;
using System.Net.Http;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;

namespace DaisyOS.System.Gaming.Providers;

public sealed class SteamArtworkProvider : IGameArtworkProvider
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly string[] SteamCdnHosts = [
        "https://shared.akamai.steamstatic.com/store_item_assets/steam/apps",
        "https://cdn.cloudflare.steamstatic.com/steam/apps"
    ];

    public string Name => "Steam Official CDN";
    public int Priority => 2;

    public bool CanResolve(GameIdentity identity)
    {
        return identity.Source == GameStoreSource.Steam && !string.IsNullOrWhiteSpace(identity.SourceId);
    }

    public async Task<GameArtworkAssets?> ResolveAsync(GameIdentity identity, CancellationToken cancellationToken = default)
    {
        if (!CanResolve(identity))
        {
            return null;
        }

        var appId = identity.SourceId!;
        string? heroPath = null;
        string? coverPath = null;
        string? logoPath = null;

        var tempDir = Path.Combine(Path.GetTempPath(), "daisyos_steam_dl_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir);

            // Fetch Cover (600x900)
            var coverBytes = await TryDownloadSteamAssetAsync(appId, ["library_600x900.jpg", "header.jpg"], cancellationToken).ConfigureAwait(false);
            if (coverBytes != null && GameArtworkValidator.IsValidImageResponse("image/jpeg", coverBytes, minWidth: 100))
            {
                coverPath = Path.Combine(tempDir, "cover.jpg");
                await File.WriteAllBytesAsync(coverPath, coverBytes, cancellationToken).ConfigureAwait(false);
            }

            // Fetch Hero background (16:9)
            var heroBytes = await TryDownloadSteamAssetAsync(appId, ["library_hero.jpg", "page_bg_generated_v6.jpg"], cancellationToken).ConfigureAwait(false);
            if (heroBytes != null && GameArtworkValidator.IsValidImageResponse("image/jpeg", heroBytes, minWidth: 100))
            {
                heroPath = Path.Combine(tempDir, "hero.jpg");
                await File.WriteAllBytesAsync(heroPath, heroBytes, cancellationToken).ConfigureAwait(false);
            }

            // Fetch Logo (transparent)
            var logoBytes = await TryDownloadSteamAssetAsync(appId, ["logo.png"], cancellationToken).ConfigureAwait(false);
            if (logoBytes != null && GameArtworkValidator.IsValidImageResponse("image/png", logoBytes))
            {
                logoPath = Path.Combine(tempDir, "logo.png");
                await File.WriteAllBytesAsync(logoPath, logoBytes, cancellationToken).ConfigureAwait(false);
            }

            if (heroPath == null && coverPath == null && logoPath == null)
            {
                return null;
            }

            return new GameArtworkAssets(
                HeroPath: heroPath,
                CoverPath: coverPath,
                LogoPath: logoPath,
                IconPath: null,
                IsFallback: false,
                ArtworkSource: "steam",
                LastChecked: DateTimeOffset.UtcNow);
        }
        catch
        {
            return null;
        }
    }

    private static async Task<byte[]?> TryDownloadSteamAssetAsync(string appId, string[] filenames, CancellationToken ct)
    {
        foreach (var cdnHost in SteamCdnHosts)
        {
            foreach (var filename in filenames)
            {
                var url = $"{cdnHost}/{appId}/{filename}";
                try
                {
                    using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                    if (!response.IsSuccessStatusCode)
                    {
                        continue;
                    }

                    var contentType = response.Content.Headers.ContentType?.MediaType;
                    if (contentType != null && contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
                    if (GameArtworkValidator.IsValidImageResponse(contentType, bytes))
                    {
                        return bytes;
                    }
                }
                catch
                {
                    // Ignore and try next endpoint
                }
            }
        }

        return null;
    }
}
