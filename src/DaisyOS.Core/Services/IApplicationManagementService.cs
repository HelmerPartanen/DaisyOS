using DaisyOS.Core.Models;
namespace DaisyOS.Core.Services;
public interface IApplicationManagementService
{
    Task<IReadOnlyList<InstalledApplication>> GetInstalledAsync(CancellationToken token = default);
    Task<ApplicationTransactionPlan> InspectFileAsync(string path, string? compatibilityImage = null, CancellationToken token = default);
    Task<ApplicationTransactionPlan> PrepareInstallAsync(InstalledApplication application, CancellationToken token = default);
    Task<ApplicationTransactionPlan> PrepareRepairAsync(InstalledApplication application, CancellationToken token = default);
    Task<ApplicationTransactionPlan> PrepareUninstallAsync(InstalledApplication application, CancellationToken token = default);
    Task<ApplicationTransactionResult> ExecuteAsync(ApplicationTransactionPlan plan, bool removeData = false, IProgress<ApplicationProgress>? progress = null, CancellationToken token = default);
}
