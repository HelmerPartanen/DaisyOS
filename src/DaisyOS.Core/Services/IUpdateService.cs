using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IUpdateService
{
    Task<UpdateStatus> CheckAsync(CancellationToken ct = default);
    Task<UpdateStatus> GetCachedStatusAsync(CancellationToken ct = default);
    event EventHandler? UpdateStatusChanged;
}
