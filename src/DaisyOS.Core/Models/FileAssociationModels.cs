namespace DaisyOS.Core.Models;

public sealed record FileHandler(
    string Id,
    string Name,
    string Icon,
    string DesktopFilePath);

public sealed record FileHandlerQueryResult(
    bool IsAvailable,
    string MimeType,
    IReadOnlyList<FileHandler> Handlers,
    string ErrorMessage);
