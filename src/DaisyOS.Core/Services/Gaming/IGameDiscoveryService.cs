using DaisyOS.Core.Models.Gaming;

namespace DaisyOS.Core.Services.Gaming;

public interface IGameDiscoveryService
{
    Task<IReadOnlyList<GameIdentity>> DiscoverGamesAsync(CancellationToken cancellationToken = default);
}
