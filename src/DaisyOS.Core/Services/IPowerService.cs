using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IPowerService
{
    Task<PowerActionResult> ShutdownAsync(CancellationToken cancellationToken = default);

    Task<PowerActionResult> RebootAsync(CancellationToken cancellationToken = default);

    Task<PowerActionResult> SuspendAsync(CancellationToken cancellationToken = default);

    Task<PowerActionResult> LogOutAsync(CancellationToken cancellationToken = default);

    Task<string> GetPowerProfileAsync(CancellationToken cancellationToken = default);

    Task<bool> SetPowerProfileAsync(string profile, CancellationToken cancellationToken = default);
}

