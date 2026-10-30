using DaisyOS.System.Continuity;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal static class ContinuityPortalGrant
{
    // The frontend preserves the helper's session token in the session path.
    // Only the daemon knows the random token issued to a paired live operation.
    public static async Task<bool> ValidateAsync(string session, CancellationToken token)
    {
        var name = session.Split('/').Last();
        if (!name.StartsWith("continuity_", StringComparison.Ordinal) || name.Length != 75) return false;
        try
        {
            await new ContinuityClient().ExecuteAsync(new("capture-grant", Value: name[11..]), token);
            return true;
        }
        catch { return false; }
    }
}
