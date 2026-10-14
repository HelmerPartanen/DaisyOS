using DaisyOS.Core.Models.Gaming;

namespace DaisyOS.Core.Services.Gaming;

public sealed record ArtworkChangedEventArgs(GameIdentity Game, GameArtworkAssets Assets);

public interface IGameArtworkResolver
{
    event EventHandler<ArtworkChangedEventArgs>? ArtworkUpdated;
    Task<GameArtworkAssets> GetArtworkAsync(GameIdentity identity, CancellationToken cancellationToken = default);
}
