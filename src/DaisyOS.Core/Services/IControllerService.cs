using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

/// <summary>Reads the current controller connection state without changing system configuration.</summary>
public interface IControllerService
{
    Task<ControllerConnectionStatus> GetConnectionStatusAsync(CancellationToken cancellationToken = default);
}
