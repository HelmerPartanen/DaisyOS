using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface ISystemMetricsService
{
    Task<SystemMetricsStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
