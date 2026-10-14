using System.IO;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;

namespace DaisyOS.System.Gaming.Providers;

public sealed class LocalArtworkProvider : IGameArtworkProvider
{
    public string Name => "Local Store Artwork";
    public int Priority => 1;

    public bool CanResolve(GameIdentity identity) => true;

    public Task<GameArtworkAssets?> ResolveAsync(GameIdentity identity, CancellationToken cancellationToken = default)
    {
        string? hero = null;
        string? cover = null;
        string? logo = null;
        string? icon = null;

        // 1. Steam AppCache local artwork check
        if (identity.Source == GameStoreSource.Steam && !string.IsNullOrWhiteSpace(identity.SourceId))
        {
            var appId = identity.SourceId;
            var steamCacheDirs = GetSteamLibraryCacheDirectories();

            foreach (var dir in steamCacheDirs)
            {
                if (!Directory.Exists(dir)) continue;

                // Hero
                var candidateHero = Path.Combine(dir, $"{appId}_hero.jpg");
                if (File.Exists(candidateHero) && GameArtworkValidator.IsValidImageFile(candidateHero, minWidth: 100))
                {
                    hero ??= candidateHero;
                }

                // Cover (600x900 or portrait)
                var candidateCover = Path.Combine(dir, $"{appId}_library_600x900.jpg");
                if (!File.Exists(candidateCover)) candidateCover = Path.Combine(dir, $"{appId}_cover.jpg");
                if (File.Exists(candidateCover) && GameArtworkValidator.IsValidImageFile(candidateCover, minWidth: 100))
                {
                    cover ??= candidateCover;
                }

                // Logo
                var candidateLogo = Path.Combine(dir, $"{appId}_logo.png");
                if (File.Exists(candidateLogo) && GameArtworkValidator.IsValidImageFile(candidateLogo))
                {
                    logo ??= candidateLogo;
                }
            }
        }

        // 2. Heroic local cache check
        if (identity.Source == GameStoreSource.Heroic || identity.Source == GameStoreSource.GOG || identity.Source == GameStoreSource.Epic)
        {
            var heroicCacheDirs = GetHeroicCacheDirectories();
            var searchName = !string.IsNullOrWhiteSpace(identity.SourceId) ? identity.SourceId : identity.NormalizedTitle;

            foreach (var dir in heroicCacheDirs)
            {
                if (!Directory.Exists(dir)) continue;

                foreach (var file in Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly))
                {
                    var filename = Path.GetFileName(file);
                    if (!filename.Contains(searchName, StringComparison.OrdinalIgnoreCase)) continue;

                    if (filename.Contains("hero", StringComparison.OrdinalIgnoreCase) || filename.Contains("background", StringComparison.OrdinalIgnoreCase))
                    {
                        if (GameArtworkValidator.IsValidImageFile(file)) hero ??= file;
                    }
                    else if (filename.Contains("cover", StringComparison.OrdinalIgnoreCase) || filename.Contains("box", StringComparison.OrdinalIgnoreCase))
                    {
                        if (GameArtworkValidator.IsValidImageFile(file)) cover ??= file;
                    }
                    else if (filename.Contains("logo", StringComparison.OrdinalIgnoreCase))
                    {
                        if (GameArtworkValidator.IsValidImageFile(file)) logo ??= file;
                    }
                }
            }
        }

        // 3. Lutris local coverart / banners
        if (identity.Source == GameStoreSource.Lutris)
        {
            var lutrisCoverDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "lutris", "coverart");
            var lutrisBannerDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share", "lutris", "banners");

            if (!string.IsNullOrWhiteSpace(identity.SourceId))
            {
                var candidateCover = Path.Combine(lutrisCoverDir, $"{identity.SourceId}.jpg");
                if (File.Exists(candidateCover) && GameArtworkValidator.IsValidImageFile(candidateCover)) cover ??= candidateCover;

                var candidateBanner = Path.Combine(lutrisBannerDir, $"{identity.SourceId}.jpg");
                if (File.Exists(candidateBanner) && GameArtworkValidator.IsValidImageFile(candidateBanner)) hero ??= candidateBanner;
            }
        }

        // 4. Local explicit artwork path or desktop icon
        if (!string.IsNullOrWhiteSpace(identity.LocalArtworkPath) && File.Exists(identity.LocalArtworkPath) && GameArtworkValidator.IsValidImageFile(identity.LocalArtworkPath))
        {
            hero ??= identity.LocalArtworkPath;
            cover ??= identity.LocalArtworkPath;
        }

        if (!string.IsNullOrWhiteSpace(identity.IconNameOrPath) && File.Exists(identity.IconNameOrPath) && GameArtworkValidator.IsValidImageFile(identity.IconNameOrPath))
        {
            icon ??= identity.IconNameOrPath;
        }

        if (hero == null && cover == null && logo == null && icon == null)
        {
            return Task.FromResult<GameArtworkAssets?>(null);
        }

        return Task.FromResult<GameArtworkAssets?>(new GameArtworkAssets(
            HeroPath: hero,
            CoverPath: cover,
            LogoPath: logo,
            IconPath: icon,
            IsFallback: false,
            ArtworkSource: "local_store",
            LastChecked: DateTimeOffset.UtcNow));
    }

    private static IEnumerable<string> GetSteamLibraryCacheDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".steam", "steam", "appcache", "librarycache");
        yield return Path.Combine(home, ".local", "share", "Steam", "appcache", "librarycache");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", "data", "Steam", "appcache", "librarycache");
    }

    private static IEnumerable<string> GetHeroicCacheDirectories()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(home, ".config", "heroic", "gog_store", "librarycache");
        yield return Path.Combine(home, ".config", "heroic", "legendaryConfig", "legendary", "librarycache");
        yield return Path.Combine(home, ".config", "heroic", "store_cache");
    }
}
