namespace DaisyOS.Core.Models;

public enum AppLaunchKind
{
    Internal,
    Uri,
    DesktopEntry,
    DirectCommand
}

public sealed record AppEntry(
    string Id,
    string Name,
    string Description,
    string Icon,
    bool IsPinned,
    AppLaunchKind LaunchKind = AppLaunchKind.Internal,
    string? LaunchTarget = null,
    IReadOnlyList<string>? LaunchArguments = null,
    IReadOnlyList<string>? Categories = null,
    IReadOnlyList<string>? SearchTerms = null)
{
    public IReadOnlyList<AppDesktopAction> DesktopActions { get; init; } = [];
    public ApplicationIdentity? Installation { get; init; }
}

public sealed record AppDesktopAction(string Id, string Name, string? Icon = null);
