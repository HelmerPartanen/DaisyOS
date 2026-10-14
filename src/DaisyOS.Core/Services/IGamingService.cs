using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IGamingService
{
    Task<GamingStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
