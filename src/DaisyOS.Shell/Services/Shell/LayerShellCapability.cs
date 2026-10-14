using System.Diagnostics;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>
/// Minimal, testable capability boundary for the production session. The
/// backend itself performs its own registry bind; this probe prevents the
/// launcher from ever selecting the production path on a compositor which
/// cannot host it.
/// </summary>
public static class LayerShellCapability
{
    public const string ProtocolName = "zwlr_layer_shell_v1";
    public const int RequiredProtocolVersion = 5;

    public static bool Supports(string? registryDump) =>
        !string.IsNullOrWhiteSpace(registryDump)
        && registryDump.Contains(ProtocolName, StringComparison.Ordinal);

    public static async Task<bool> ProbeAsync(CancellationToken cancellationToken = default)
    {
        if (!OperatingSystem.IsLinux() || string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        {
            return false;
        }

        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "wayland-info",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false
                }
            };
            process.Start();
            // This probe is deliberately called before the first shell surface
            // exists. Native startup cannot afford to depend on Avalonia's UI
            // dispatcher: StartNativeShellSession performs the hard gate on
            // that thread, and a captured continuation would leave KWin with
            // an empty black scene forever.
            var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await Task.WhenAll(outputTask, errorTask).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            return process.ExitCode == 0 && Supports(output);
        }
        catch (Exception) when (OperatingSystem.IsLinux())
        {
            return false;
        }
    }
}
