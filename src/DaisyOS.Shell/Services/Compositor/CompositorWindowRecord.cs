namespace DaisyOS.Shell.Services.Compositor;

/// <summary>
/// A compositor-owned application window.  The identifier is assigned by the
/// compositor and is deliberately never inferred from a process id.
/// </summary>
public sealed record CompositorWindowRecord(
    string Id,
    string AppId,
    string Title,
    string? DesktopFileId,
    string? IconName,
    string? OutputName,
    int Workspace,
    bool IsActive,
    bool IsMinimized,
    bool IsMaximized,
    bool IsFullScreen,
    bool CanClose,
    string? DaisyAppId = null,
    int ProcessId = 0,
    bool IsUnresponsive = false,
    bool IsOnCurrentWorkspace = true)
{
    public string DisplayName => string.IsNullOrWhiteSpace(Title) ? AppId : Title;
}
