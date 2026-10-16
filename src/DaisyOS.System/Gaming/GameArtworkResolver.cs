using System.Collections.Concurrent;
using DaisyOS.Core.Models.Gaming;
using DaisyOS.Core.Services.Gaming;
using DaisyOS.System.Gaming.Providers;

namespace DaisyOS.System.Gaming;

public sealed class GameArtworkResolver : IGameArtworkResolver
{
    private readonly IArtworkCache _cache;
    private readonly IReadOnlyList<IGameArtworkProvider> _providers;
    private readonly ConcurrentDictionary<string, byte> _pendingResolutions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, GameArtworkAssets> _resolvedAssets = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _resolutionSlots = new(4, 4);

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
        if (cancellationToken.IsCancellationRequested) return Task.FromCanceled<GameArtworkAssets>(cancellationToken);
        var key = ArtworkCache.BuildCacheKey(identity);
        if (_resolvedAssets.TryGetValue(key, out var cached)) return Task.FromResult(cached);
        // Cache metadata reads and providers can do synchronous disk/image work
        // before returning a Task. None of that belongs on the caller's UI thread.
        if (_pendingResolutions.TryAdd(key, 0)) _ = Task.Run(() => EnqueueBackgroundResolutionAsync(identity, key));
        return Task.FromResult(new GameArtworkAssets(null, null, null, null, true, "fallback", DateTimeOffset.UtcNow));
    }

    private async Task EnqueueBackgroundResolutionAsync(GameIdentity identity, string cacheKey)
    {
        try
        {
            await _resolutionSlots.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_cache.Lookup(identity) is { } cached)
                {
                    _resolvedAssets[cacheKey] = cached;
                    ArtworkUpdated?.Invoke(this, new ArtworkChangedEventArgs(identity, cached));
                    return;
                }
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
                    _resolvedAssets[cacheKey] = storedAssets;
                    ArtworkUpdated?.Invoke(this, new ArtworkChangedEventArgs(identity, storedAssets));
                }
                else
                {
                    var fallbackProvider = _providers.OfType<FallbackArtworkProvider>().FirstOrDefault() ?? new FallbackArtworkProvider();
                    var fallbackAssets = await fallbackProvider.ResolveAsync(identity).ConfigureAwait(false)
                        ?? new GameArtworkAssets(null, null, null, identity.IconNameOrPath, true, "fallback", DateTimeOffset.UtcNow);

                    var storedFallback = await _cache.StoreAsync(identity, fallbackAssets).ConfigureAwait(false);
                    _resolvedAssets[cacheKey] = storedFallback;
                    ArtworkUpdated?.Invoke(this, new ArtworkChangedEventArgs(identity, storedFallback));
                }
            }
            finally { _resolutionSlots.Release(); }
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
