using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface INetworkStatusService
{
    Task<NetworkStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}

