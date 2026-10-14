using DaisyOS.Core.Models.Gaming;

namespace DaisyOS.Core.Services.Gaming;

public interface IArtworkCache
{
    string CacheRootPath { get; }
    GameArtworkAssets? Lookup(GameIdentity identity);
    Task<GameArtworkAssets> StoreAsync(GameIdentity identity, GameArtworkAssets assets, CancellationToken cancellationToken = default);
    void Invalidate(GameIdentity identity);
    Task<GameArtworkAssets?> RefreshAsync(GameIdentity identity, CancellationToken cancellationToken = default);
}
