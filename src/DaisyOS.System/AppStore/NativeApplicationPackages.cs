using System.Text.RegularExpressions;
using DaisyOS.System.Processes;

namespace DaisyOS.System.AppStore;

/// <summary>Uses Pacman's machine-readable ownership query, independent of the session language.</summary>
public static partial class NativeApplicationPackages
{
    public static async Task<string?> FindOwnerAsync(ICommandRunner runner, string desktopPath, CancellationToken token = default)
    {
        var result = await runner.RunAsync("pacman", ["-Qqo", "--", desktopPath], TimeSpan.FromSeconds(4), token).ConfigureAwait(false);
        var package = result.StandardOutput.Trim();
        return result.Succeeded && PackageName().IsMatch(package) ? package : null;
    }

    public static async Task<CommandResult> RemoveAsync(ICommandRunner runner, string package, CancellationToken token = default)
    {
        if (!PackageName().IsMatch(package)) throw new ArgumentException("Invalid package name.", nameof(package));
        var service = new DaisyOS.System.Applications.ApplicationManagementService(runner);
        var plan = await service.PrepareUninstallAsync(new(new(DaisyOS.Core.Models.ApplicationBackend.Pacman, package, "system"), package), token).ConfigureAwait(false);
        var result = await service.ExecuteAsync(plan, token: token).ConfigureAwait(false);
        return result.Succeeded ? CommandResult.Success(result.Message) : CommandResult.Failed(result.Message);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9@._+][a-zA-Z0-9@._+\-]*\z")]
    private static partial Regex PackageName();
}
