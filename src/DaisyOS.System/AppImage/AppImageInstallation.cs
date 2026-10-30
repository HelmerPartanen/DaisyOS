namespace DaisyOS.System.AppImage;

/// <summary>One AppImage installed for the current user.</summary>
public sealed record AppImageInstallation
{
    public required string DesktopId { get; init; }
    public required string Name { get; init; }
    public string Comment { get; init; } = "";
    public string Version { get; init; } = "";
    public string StartupWMClass { get; init; } = "";
    public string Categories { get; init; } = "";
    public required string AppImagePath { get; init; }
    public required string DesktopFilePath { get; init; }
    public string IconPath { get; init; } = "";
    public DateTimeOffset InstalledAt { get; init; }
}
