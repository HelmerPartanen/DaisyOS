using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IAppStoreService
{
    Task<IReadOnlyList<AppStoreItem>> GetFeaturedAppsAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppStoreItem>> GetAppsByCategoryAsync(AppCategory category, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppStoreItem>> SearchAppsAsync(string query, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppStoreItem>> GetInstalledAppsAsync(CancellationToken cancellationToken = default);

    Task<bool> InstallAppAsync(string packageName, Action<double>? progressCallback = null, CancellationToken cancellationToken = default);

    Task<bool> UninstallAppAsync(string packageName, CancellationToken cancellationToken = default);

    Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AppStoreItem>> GetUpdatesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<AppStoreItem>>([]);

    Task<bool> UpdateAppAsync(string appId, Action<double>? progressCallback = null, CancellationToken cancellationToken = default)
        => Task.FromResult(false);

    Task<AppPackageInfo?> GetPackageInfoAsync(string appId, CancellationToken cancellationToken = default)
        => Task.FromResult<AppPackageInfo?>(null);

    Task RefreshCatalogAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
