using System.Collections.Concurrent;
using System.IO;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.System.Gaming.Providers;

namespace DaisyOS.System.Gaming;

public sealed class GameArtworkResolver : IGameArtworkResolver
{
    private readonly IArtworkCache _cache;
    private readonly IReadOnlyList<IGameArtworkProvider> _providers;
    private readonly ConcurrentDictionary<string, byte> _pendingResolutions = new(StringComparer.Ordinal);

    public event EventHandler<ArtworkChangedEventArgs>? ArtworkUpdated;

    public GameArtworkResolver(IArtworkCache? cache = null, IEnumerable<IGameArtworkProvider>? providers = null)
    {
        _cache = cache ?? new ArtworkCache();
        _providers = (providers ?? [
            new LocalArtworkProvider(),
            new SteamArtworkProvider(),
            new MetadataArtworkProvider(),
            new FallbackArtworkProvider()
        ]).OrderBy(p => p.Priority).ToList();
    }

    public Task<GameArtworkAssets> GetArtworkAsync(GameIdentity identity, CancellationToken cancellationToken = default)
    {
        // 1. Check local DaisyOS artwork cache first (Zero UI delay!)
        var cached = _cache.Lookup(identity);
        if (cached != null)
        {
            return Task.FromResult(cached);
        }

        // 2. Immediate fallback return to keep shell UI instant
        var fallbackProvider = _providers.OfType<FallbackArtworkProvider>().FirstOrDefault() ?? new FallbackArtworkProvider();
        var fallbackAssets = fallbackProvider.ResolveAsync(identity, cancellationToken).GetAwaiter().GetResult()
            ?? new GameArtworkAssets(null, null, null, identity.IconNameOrPath, true, "fallback", DateTimeOffset.UtcNow);

        // 3. Queue asynchronous resolution in background
        _ = EnqueueBackgroundResolutionAsync(identity);

        return Task.FromResult(fallbackAssets);
    }

    private async Task EnqueueBackgroundResolutionAsync(GameIdentity identity)
    {
        var cacheKey = ArtworkCache.BuildCacheKey(identity);
        if (!_pendingResolutions.TryAdd(cacheKey, 0))
        {
            return; // Already resolving
        }

        try
        {
            GameArtworkAssets? resolved = null;

            foreach (var provider in _providers)
            {
                if (provider is FallbackArtworkProvider) continue;
                if (!provider.CanResolve(identity)) continue;

                try
                {
                    var assets = await provider.ResolveAsync(identity).ConfigureAwait(false);
                    if (assets != null && (assets.HasCover || assets.HasHero || assets.HasLogo))
                    {
                        resolved = assets;
                        break;
                    }
                }
                catch
                {
                    // Best effort provider iteration
                }
            }

            if (resolved != null)
            {
                var storedAssets = await _cache.StoreAsync(identity, resolved).ConfigureAwait(false);
                ArtworkUpdated?.Invoke(this, new ArtworkChangedEventArgs(identity, storedAssets));
            }
            else
            {
                var fallbackProvider = _providers.OfType<FallbackArtworkProvider>().FirstOrDefault() ?? new FallbackArtworkProvider();
                var fallbackAssets = await fallbackProvider.ResolveAsync(identity).ConfigureAwait(false)
                    ?? new GameArtworkAssets(null, null, null, identity.IconNameOrPath, true, "fallback", DateTimeOffset.UtcNow);

                var storedFallback = await _cache.StoreAsync(identity, fallbackAssets).ConfigureAwait(false);
                ArtworkUpdated?.Invoke(this, new ArtworkChangedEventArgs(identity, storedFallback));
            }
        }
        catch
        {
            // Background resolution failure does not crash the shell UI
        }
        finally
        {
            _pendingResolutions.TryRemove(cacheKey, out _);
        }
    }
}
