using DaisyOS.Core.Models.Gaming;
using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class ArtworkCacheTests
{
    [Fact]
    public async Task StoreAndLookup_PersistsAndReadsMetadataJson()
    {
        var tempCachePath = Path.Combine(Path.GetTempPath(), "daisyos_test_cache_" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ArtworkCache(tempCachePath);
            var game = new GameIdentity("Cyberpunk 2077", "Cyberpunk 2077", GameStoreSource.Steam, "1091500");

            var fakeHero = Path.Combine(tempCachePath, "test_hero.jpg");
            Directory.CreateDirectory(tempCachePath);
            await File.WriteAllBytesAsync(fakeHero, new byte[2048]);

            var assets = new GameArtworkAssets(
                HeroPath: fakeHero,
                CoverPath: null,
                LogoPath: null,
                IconPath: null,
                IsFallback: false,
                ArtworkSource: "steam",
                LastChecked: DateTimeOffset.UtcNow);

            var stored = await cache.StoreAsync(game, assets);
            Assert.NotNull(stored);

            var retrieved = cache.Lookup(game);
            Assert.NotNull(retrieved);
            Assert.Equal("steam", retrieved.ArtworkSource);
            Assert.True(retrieved.HasHero);

            var metadataJsonPath = Path.Combine(cache.GetGameCacheDirectory(game), "metadata.json");
            Assert.True(File.Exists(metadataJsonPath));

            var jsonText = await File.ReadAllTextAsync(metadataJsonPath);
            Assert.Contains("\"game\": \"Cyberpunk 2077\"", jsonText, StringComparison.Ordinal);
            Assert.Contains("\"source_id\": \"1091500\"", jsonText, StringComparison.Ordinal);
        }
        finally
        {
            if (Directory.Exists(tempCachePath))
            {
                try { Directory.Delete(tempCachePath, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void BuildCacheKey_GeneratesPredictablePath()
    {
        var game = new GameIdentity("Cyberpunk 2077", "Cyberpunk 2077", GameStoreSource.Steam, "1091500");
        var cacheKey = ArtworkCache.BuildCacheKey(game);
        Assert.Equal("steam-1091500", cacheKey);
    }
}
