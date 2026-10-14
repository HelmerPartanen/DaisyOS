using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;

namespace DaisyOS.System.Gaming;

public sealed class ArtworkCache : IArtworkCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public string CacheRootPath { get; }

    public ArtworkCache(string? cacheRootPath = null)
    {
        CacheRootPath = cacheRootPath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache",
            "daisyos",
            "game-artwork");
    }

    public string GetGameCacheDirectory(GameIdentity identity)
    {
        var key = BuildCacheKey(identity);
        return Path.Combine(CacheRootPath, key);
    }

    public GameArtworkAssets? Lookup(GameIdentity identity)
    {
        var dir = GetGameCacheDirectory(identity);
        if (!Directory.Exists(dir))
        {
            return null;
        }

        var metadataPath = Path.Combine(dir, "metadata.json");
        if (!File.Exists(metadataPath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(metadataPath);
            var metadata = JsonSerializer.Deserialize(json, DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.GameArtworkMetadata);
            if (metadata == null)
            {
                return null;
            }

            string? heroPath = metadata.Assets.Hero != null ? Path.Combine(dir, metadata.Assets.Hero) : null;
            string? coverPath = metadata.Assets.Cover != null ? Path.Combine(dir, metadata.Assets.Cover) : null;
            string? logoPath = metadata.Assets.Logo != null ? Path.Combine(dir, metadata.Assets.Logo) : null;
            string? iconPath = metadata.Assets.Icon != null ? Path.Combine(dir, metadata.Assets.Icon) : null;

            if (heroPath != null && !File.Exists(heroPath)) heroPath = null;
            if (coverPath != null && !File.Exists(coverPath)) coverPath = null;
            if (logoPath != null && !File.Exists(logoPath)) logoPath = null;
            if (iconPath != null && !File.Exists(iconPath)) iconPath = null;

            if (heroPath == null && coverPath == null && logoPath == null && iconPath == null)
            {
                return null;
            }

            var lastChecked = DateTimeOffset.TryParse(metadata.LastChecked, out var parsedDate)
                ? parsedDate
                : DateTimeOffset.UtcNow;

            return new GameArtworkAssets(
                heroPath,
                coverPath,
                logoPath,
                iconPath,
                IsFallback: string.Equals(metadata.ArtworkSource, "fallback", StringComparison.OrdinalIgnoreCase),
                ArtworkSource: metadata.ArtworkSource,
                LastChecked: lastChecked);
        }
        catch
        {
            return null;
        }
    }

    public async Task<GameArtworkAssets> StoreAsync(GameIdentity identity, GameArtworkAssets assets, CancellationToken cancellationToken = default)
    {
        var targetDir = GetGameCacheDirectory(identity);
        var tempDir = Path.Combine(CacheRootPath, ".tmp_" + Guid.NewGuid().ToString("N"));

        try
        {
            Directory.CreateDirectory(tempDir);

            string? targetHero = null;
            string? targetCover = null;
            string? targetLogo = null;
            string? targetIcon = null;

            if (assets.HasHero && File.Exists(assets.HeroPath))
            {
                var ext = Path.GetExtension(assets.HeroPath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                targetHero = "hero" + ext;
                File.Copy(assets.HeroPath!, Path.Combine(tempDir, targetHero), overwrite: true);
            }

            if (assets.HasCover && File.Exists(assets.CoverPath))
            {
                var ext = Path.GetExtension(assets.CoverPath);
                if (string.IsNullOrEmpty(ext)) ext = ".jpg";
                targetCover = "cover" + ext;
                File.Copy(assets.CoverPath!, Path.Combine(tempDir, targetCover), overwrite: true);
            }

            if (assets.HasLogo && File.Exists(assets.LogoPath))
            {
                var ext = Path.GetExtension(assets.LogoPath);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                targetLogo = "logo" + ext;
                File.Copy(assets.LogoPath!, Path.Combine(tempDir, targetLogo), overwrite: true);
            }

            if (assets.HasIcon && File.Exists(assets.IconPath))
            {
                var ext = Path.GetExtension(assets.IconPath);
                if (string.IsNullOrEmpty(ext)) ext = ".png";
                targetIcon = "icon" + ext;
                File.Copy(assets.IconPath!, Path.Combine(tempDir, targetIcon), overwrite: true);
            }

            var metadata = new GameArtworkMetadata(
                Game: identity.Title,
                Source: identity.Source.ToString().ToLowerInvariant(),
                SourceId: identity.SourceId,
                ArtworkSource: assets.ArtworkSource,
                LastChecked: assets.LastChecked.ToString("o"),
                Assets: new GameArtworkMetadataAssets(
                    Hero: targetHero,
                    Cover: targetCover,
                    Logo: targetLogo,
                    Icon: targetIcon));

            var json = JsonSerializer.Serialize(metadata, DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.GameArtworkMetadata);
            await File.WriteAllTextAsync(Path.Combine(tempDir, "metadata.json"), json, cancellationToken).ConfigureAwait(false);

            // Atomic replace of target directory
            if (Directory.Exists(targetDir))
            {
                Directory.Delete(targetDir, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(targetDir)!);
            Directory.Move(tempDir, targetDir);

            return Lookup(identity) ?? assets;
        }
        catch
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
            throw;
        }
    }

    public void Invalidate(GameIdentity identity)
    {
        var targetDir = GetGameCacheDirectory(identity);
        if (Directory.Exists(targetDir))
        {
            try { Directory.Delete(targetDir, recursive: true); } catch { }
        }
    }

    public Task<GameArtworkAssets?> RefreshAsync(GameIdentity identity, CancellationToken cancellationToken = default)
    {
        Invalidate(identity);
        return Task.FromResult<GameArtworkAssets?>(null);
    }

    public static string BuildCacheKey(GameIdentity identity)
    {
        var sourceStr = identity.Source.ToString().ToLowerInvariant();
        if (!string.IsNullOrWhiteSpace(identity.SourceId))
        {
            return $"{sourceStr}-{SanitizeFileName(identity.SourceId)}";
        }

        var normalizedTitle = string.IsNullOrWhiteSpace(identity.NormalizedTitle)
            ? GameTitleNormalizer.Normalize(identity.Title)
            : identity.NormalizedTitle;

        using var sha = SHA256.Create();
        var hashBytes = sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedTitle.ToLowerInvariant()));
        var hashHex = Convert.ToHexString(hashBytes)[..8].ToLowerInvariant();
        return $"{sourceStr}-{hashHex}";
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            sb.Append(invalid.Contains(ch) ? '_' : ch);
        }
        return sb.ToString();
    }
}
