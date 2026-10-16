using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class GameArtworkResolverTests
{
    [Fact]
    public async Task SlowDiskCacheCannotDelayFallbackOrStartDuplicateResolutions()
    {
        using var cache = new BlockingCache();
        var resolver = new GameArtworkResolver(cache, []);
        var game = new GameIdentity("Cached game", "Cached game", GameStoreSource.Manual);
        var updated = new TaskCompletionSource<GameArtworkAssets>(TaskCreationOptions.RunContinuationsAsynchronously);
        resolver.ArtworkUpdated += (_, args) => updated.TrySetResult(args.Assets);
        try
        {
            var request = Task.Run(() => resolver.GetArtworkAsync(game));
            await cache.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.True((await request.WaitAsync(TimeSpan.FromSeconds(2))).IsFallback);
            for (var i = 0; i < 20; i++) Assert.True((await resolver.GetArtworkAsync(game)).IsFallback);
            Assert.Equal(1, cache.Lookups);
        }
        finally { cache.Release.Set(); }
        var artwork = await updated.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Same(cache.Cached, artwork);
        Assert.Same(artwork, await resolver.GetArtworkAsync(game));
        Assert.Equal(1, cache.Lookups);
    }

    private sealed class BlockingCache : IArtworkCache, IDisposable
    {
        public string CacheRootPath => "";
        public readonly TaskCompletionSource Entered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly ManualResetEventSlim Release = new();
        public readonly GameArtworkAssets Cached = new(null, "/cached/cover.png", null, null, false, "cache", DateTimeOffset.UtcNow);
        public int Lookups;
        public GameArtworkAssets? Lookup(GameIdentity identity)
        {
            Interlocked.Increment(ref Lookups); Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return Cached;
        }
        public Task<GameArtworkAssets> StoreAsync(GameIdentity identity, GameArtworkAssets assets, CancellationToken cancellationToken = default) => Task.FromResult(assets);
        public void Invalidate(GameIdentity identity) { }
        public Task<GameArtworkAssets?> RefreshAsync(GameIdentity identity, CancellationToken cancellationToken = default) => Task.FromResult<GameArtworkAssets?>(Cached);
        public void Dispose() => Release.Dispose();
    }

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
