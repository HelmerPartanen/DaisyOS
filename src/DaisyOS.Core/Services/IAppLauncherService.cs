using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IAppLauncherService
{
    IReadOnlyList<AppEntry> GetAvailableApps();

    Task<AppLaunchResult> LaunchAsync(AppEntry app, CancellationToken cancellationToken = default);
}
