using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IDisplayStatusService
{
    Task<DisplayStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
