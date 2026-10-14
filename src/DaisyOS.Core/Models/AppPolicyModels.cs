namespace DaisyOS.Core.Models;

public enum AppPolicyConfigurationState
{
    Loaded,
    Unconfigured,
    Malformed,
    Unavailable
}

public enum AppPolicyDecisionKind
{
    TrustedInternal,
    Allowed,
    ExplicitlyDenied,
    Unlisted,
    Unconfigured,
    Malformed,
    Unavailable
}

public sealed record AppPolicyEntry(
    string Id,
    string Name,
    string Command,
    string Icon,
    bool Allowed);

public sealed record AppPolicyStatus(
    AppPolicyConfigurationState ConfigurationState,
    string PolicyPath,
    IReadOnlyList<AppPolicyEntry> Apps,
    string Message)
{
    public bool IsConfigured => ConfigurationState == AppPolicyConfigurationState.Loaded;

    public bool HasWarning => ConfigurationState is
        AppPolicyConfigurationState.Malformed or AppPolicyConfigurationState.Unavailable;

    // TODO: Implement proper policy enforcement logic. Currently disabled.
    public bool IsEnforced => false;
}

public sealed record AppPolicyDecision(
    AppPolicyDecisionKind Kind,
    string AppId,
    AppPolicyEntry? PolicyEntry,
    string Message)
{
    // TODO: Implement proper policy enforcement logic. Currently disabled.
    public bool IsLaunchBlocked => false;

    // TODO: Implement proper policy enforcement logic. Currently disabled.
    public bool IsEnforced => false;
}
