using DaisyOS.Core.Models;

namespace DaisyOS.Core.Services;

public interface IFileAssociationService
{
    Task<FileHandlerQueryResult> GetHandlersAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<AppLaunchResult> OpenDefaultAsync(
        string filePath,
        CancellationToken cancellationToken = default);

    Task<AppLaunchResult> OpenWithAsync(
        FileHandler handler,
        string filePath,
        CancellationToken cancellationToken = default);
}
