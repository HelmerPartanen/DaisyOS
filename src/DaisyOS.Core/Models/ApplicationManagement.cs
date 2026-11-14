namespace DaisyOS.Core.Models;

public enum ApplicationBackend { Pacman, Flatpak, AppImage, Container, Unmanaged }
public enum ApplicationOperation { Install, Uninstall, Repair }
public sealed record ApplicationIdentity(ApplicationBackend Backend, string PackageId, string Scope = "user", string? Container = null, string? Reference = null);
public sealed record InstalledApplication(ApplicationIdentity Identity, string Name, string Version = "")
{
    public string SourceFingerprint { get; init; } = "";
    public string SourcePath { get; init; } = "";
    public string Origin { get; init; } = "";
    public string Architecture { get; init; } = "";
    public string Image { get; init; } = "";
    public string ImageDigest { get; init; } = "";
    public string DataDirectory { get; init; } = "";
    public string[] DesktopIds { get; init; } = [];
    public string[] OwnedLaunchers { get; init; } = [];
    public string[] OwnedCommands { get; init; } = [];
    public string[] OwnedIntegrationFiles { get; init; } = [];
    public string[] OwnedIcons { get; init; } = [];
    public string[] ReviewedPaths { get; init; } = [];
    public bool IsBroken { get; init; }
    public bool CleanupPending { get; init; }
    public bool IsProtected { get; init; }
    public string ProtectionReason { get; init; } = "";
}
public sealed record ApplicationTransactionPlan(ApplicationOperation Operation, InstalledApplication Application)
{
    public string[] AffectedPackages { get; init; } = [];
    public long DownloadBytes { get; init; }
    public long InstalledBytes { get; init; }
    public bool CleanupOnly { get; init; }
    public bool RequiresFlatpakSetup { get; init; }
    public bool IsDataShared { get; init; }
    public bool RequiresCompatibilitySetup { get; init; }
    public string Error { get; init; } = "";
    public bool CanExecute => Error.Length == 0 && !Application.IsProtected;
    public bool SupportsDataRemoval => Application.Identity.Backend == ApplicationBackend.Container ||
        Application.Identity.Backend == ApplicationBackend.Flatpak && Application.Identity.Scope == "user" && !IsDataShared;
    public bool RequiresReview => Application.Identity.Backend == ApplicationBackend.Unmanaged && Application.ReviewedPaths.Length == 0;
}
public sealed record ApplicationTransactionResult(bool Succeeded, string Message, string Details = "")
{
    public InstalledApplication? Application { get; init; }
    public string[] RemovedDesktopIds { get; init; } = [];
    public bool RequiresReview { get; init; }
    public bool CleanupPending { get; init; }
    public bool Cancelled { get; init; }
}
public sealed record ApplicationProgress(string Stage, double? Fraction = null);
