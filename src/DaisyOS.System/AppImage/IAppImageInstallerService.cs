namespace DaisyOS.System.AppImage;

public interface IAppImageInstallerService
{
    Task<AppImageInspection> InspectAsync(string path, CancellationToken cancellationToken = default);

    Task<AppImageInstallation> InstallAsync(string path, string? destinationDirectory = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AppImageInstallation>> GetInstalledAsync(CancellationToken cancellationToken = default);

    Task<bool> UninstallAsync(string desktopId, CancellationToken cancellationToken = default);
}
