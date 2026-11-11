using System.Text.RegularExpressions;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

/// <summary>
/// Temporarily selects the PPD performance profile for Gaming Mode when the
/// platform advertises it. Restores the captured profile only while the active
/// profile is still the one DaisyOS selected.
/// </summary>
internal sealed class GamingPowerProfileController : IDisposable
{
    public const string EnabledPreferenceKey = "gaming.use-performance-profile";
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);
    private static readonly Regex ProfileLine = new(
        @"(?m)^\s*\*?\s*(power-saver|balanced|performance):?\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly ICommandRunner _runner;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _previousProfile;
    private bool _ownsTemporaryProfile;
    private bool _disposed;

    public GamingPowerProfileController(ICommandRunner? runner = null) =>
        _runner = runner ?? new SafeCommandRunner();

    public async Task SetActiveAsync(bool active, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            if (active)
                await ActivateAsync(cancellationToken).ConfigureAwait(false);
            else
                await RestoreAsync(cancellationToken).ConfigureAwait(false);
        }
        finally { _gate.Release(); }
    }

    private async Task ActivateAsync(CancellationToken cancellationToken)
    {
        if (_ownsTemporaryProfile) return;

        var listing = await _runner.RunAsync("powerprofilesctl", ["list"], CommandTimeout, cancellationToken).ConfigureAwait(false);
        if (!listing.Succeeded || !ProfileLine.Matches(listing.StandardOutput).Cast<Match>()
                .Any(match => match.Groups[1].Value == "performance"))
            return;

        var current = await ReadActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (current is null || current == "performance") return;

        var changed = await _runner.RunAsync("powerprofilesctl", ["set", "performance"], CommandTimeout, cancellationToken)
            .ConfigureAwait(false);
        if (!changed.Succeeded) return;

        // A successful PPD set is the transaction acknowledgment. Record the
        // snapshot immediately; restoration will still verify the current
        // profile before making any change.
        _previousProfile = current;
        _ownsTemporaryProfile = true;
    }

    private async Task RestoreAsync(CancellationToken cancellationToken)
    {
        if (!_ownsTemporaryProfile || _previousProfile is null) return;

        var current = await ReadActiveProfileAsync(cancellationToken).ConfigureAwait(false);
        if (current is null) return;
        if (current == "performance")
        {
            var restored = await _runner.RunAsync("powerprofilesctl", ["set", _previousProfile], CommandTimeout, cancellationToken)
                .ConfigureAwait(false);
            if (!restored.Succeeded) return;
        }

        // A user or another service selected a different profile while Gaming
        // Mode was active. Do not overwrite that choice.
        _previousProfile = null;
        _ownsTemporaryProfile = false;
    }

    private async Task<string?> ReadActiveProfileAsync(CancellationToken cancellationToken)
    {
        var result = await _runner.RunAsync("powerprofilesctl", ["get"], CommandTimeout, cancellationToken).ConfigureAwait(false);
        if (!result.Succeeded) return null;
        var profile = result.StandardOutput.Trim();
        return profile is "power-saver" or "balanced" or "performance" ? profile : null;
    }

    public void Dispose()
    {
        _gate.Wait();
        try { _disposed = true; }
        finally { _gate.Release(); }
        _gate.Dispose();
    }
}
