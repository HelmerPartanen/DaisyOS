using DaisyOS.Core.Models.Gaming;

namespace DaisyOS.Core.Services.Gaming;

public interface IGameArtworkProvider
{
    string Name { get; }
    int Priority { get; }
    bool CanResolve(GameIdentity identity);
    Task<GameArtworkAssets?> ResolveAsync(GameIdentity identity, CancellationToken cancellationToken = default);
}
