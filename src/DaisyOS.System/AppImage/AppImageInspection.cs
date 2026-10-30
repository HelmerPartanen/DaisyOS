namespace DaisyOS.System.AppImage;

/// <summary>Read-only metadata discovered before an AppImage is installed.</summary>
public sealed record AppImageInspection
{
    public required string SourcePath { get; init; }
    public required string Name { get; init; }
    public string Comment { get; init; } = "";
    public string Version { get; init; } = "";
    public string Executable { get; init; } = "";
    public string DesktopId { get; init; } = "";
    public string StartupWMClass { get; init; } = "";
    public string Categories { get; init; } = "";
    public long SizeBytes { get; init; }
    public bool HasEmbeddedMetadata { get; init; }
    public byte[]? IconData { get; init; }
    public string IconExtension { get; init; } = "";
}
