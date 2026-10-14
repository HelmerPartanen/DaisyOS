using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IBatteryStatusService
{
    Task<BatteryStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
