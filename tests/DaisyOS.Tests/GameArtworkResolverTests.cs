using DaisyOS.Core.Models.Gaming;
using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class GameArtworkResolverTests
{
    [Fact]
    public async Task GetArtworkAsync_ReturnsFallbackImmediatelyWhenUncached()
    {
        var tempCachePath = Path.Combine(Path.GetTempPath(), "daisyos_resolver_test_" + Guid.NewGuid().ToString("N"));
        try
        {
            var cache = new ArtworkCache(tempCachePath);
            var resolver = new GameArtworkResolver(cache);

            var game = new GameIdentity("Unknown Game", "Unknown Game", GameStoreSource.Manual);

            var assets = await resolver.GetArtworkAsync(game);

            Assert.NotNull(assets);
            Assert.True(assets.IsFallback);
            Assert.Equal("fallback", assets.ArtworkSource);
        }
        finally
        {
            if (Directory.Exists(tempCachePath))
            {
                try { Directory.Delete(tempCachePath, recursive: true); } catch { }
            }
        }
    }
}
