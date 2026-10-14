using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IUserSessionService
{
    Task<UserSessionStatus> GetStatusAsync(CancellationToken cancellationToken = default);
}
