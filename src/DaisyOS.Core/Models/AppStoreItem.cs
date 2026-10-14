namespace DaisyOS.Core.Models;

public enum AppCategory
{
    Discover,
    Productivity,
    Creative,
    Development,
    Gaming,
    Audio,
    Installed,
    Updates,
    Internet,
    Video,
    Education,
    Utilities
}

public enum AppInstallState
{
    NotInstalled,
    Installing,
    Installed,
    UpdateAvailable
}

public sealed record AppStoreItem(
    string Id,
    string PackageName,
    string Name,
    string Summary,
    string Description,
    AppCategory Category,
    string IconPath,
    string IconGlyph,
    string Version,
    string Publisher,
    string DownloadSize,
    double Rating,
    bool IsFeatured,
    bool IsEditorsChoice,
    AppInstallState InstallState = AppInstallState.NotInstalled)
{
    public string DesktopFileId { get; init; } = "";
    public string NativeDesktopFile { get; init; } = "";
    public IReadOnlyList<string> Screenshots { get; init; } = [];
    public IReadOnlyList<string> Keywords { get; init; } = [];
    public IReadOnlyList<string> Categories { get; init; } = [];
    public string License { get; init; } = string.Empty;
    public string Homepage { get; init; } = string.Empty;
    public string ReleaseNotes { get; init; } = string.Empty;
    public bool IsVerified { get; init; }
    public string Origin { get; init; } = "flathub";
    public string InstallationScope { get; init; } = "user";
}
