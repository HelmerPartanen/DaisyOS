namespace DaisyOS.Core.Continuity;

public enum ContinuityCapability
{
    Clipboard,
    Files,
    Input,
    Handoff,
    Display
}

public enum ContinuityDenial
{
    None,
    Disabled,
    SessionUnavailable,
    Untrusted,
    Unauthenticated,
    PermissionRequired,
    LocalConsentRequired,
    Unsupported
}

/// <summary>
/// Immutable inputs to the receiving-side policy. Authentication and trust must
/// come from the daemon's verified connection and trust store, never peer claims.
/// No runtime feature is enabled by this model.
/// </summary>
public sealed record ContinuityAccess(
    bool Enabled = false,
    bool Paused = false,
    bool SessionActive = false,
    bool Locked = true,
    bool Trusted = false,
    bool Revoked = false,
    bool Authenticated = false,
    bool PermissionGranted = false,
    bool LocalConsentGranted = false,
    DateTimeOffset? ConsentExpiresAt = null);

public static class ContinuityPolicy
{
    /// <summary>
    /// Evaluate immediately before dispatch, using current state. This pure rule
    /// cannot cancel work already in flight; adapters must cancel and revalidate
    /// grants on lock, pause, revocation, owner loss and disconnect.
    /// </summary>
    public static ContinuityDenial Evaluate(
        ContinuityAccess access, ContinuityCapability capability, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(access);
        if (!access.Enabled || access.Paused) return ContinuityDenial.Disabled;
        if (!access.SessionActive || access.Locked) return ContinuityDenial.SessionUnavailable;
        if (!access.Trusted || access.Revoked) return ContinuityDenial.Untrusted;
        if (!access.Authenticated) return ContinuityDenial.Unauthenticated;
        // Release-two capabilities stay unavailable even if a stale or malicious
        // permission record claims they were granted.
        if (capability is not (ContinuityCapability.Clipboard or ContinuityCapability.Files))
            return ContinuityDenial.Unsupported;
        if (!access.PermissionGranted) return ContinuityDenial.PermissionRequired;
        if (capability == ContinuityCapability.Files &&
            (!access.LocalConsentGranted || access.ConsentExpiresAt is not { } expiry || expiry <= now))
            return ContinuityDenial.LocalConsentRequired;
        return ContinuityDenial.None;
    }
}
