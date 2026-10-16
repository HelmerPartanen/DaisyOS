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

    public static Task<CommandResult> RemoveAsync(ICommandRunner runner, string package, CancellationToken token = default)
    {
        if (!PackageName().IsMatch(package)) throw new ArgumentException("Invalid package name.", nameof(package));
        // Keep dependency checks enabled: removing an application must not remove the desktop with it.
        return runner.RunAsync("pkexec", ["pacman", "-R", "--noconfirm", "--", package], TimeSpan.FromMinutes(3), token);
    }

    [GeneratedRegex(@"^[a-zA-Z0-9@._+][a-zA-Z0-9@._+\-]*\z")]
    private static partial Regex PackageName();
}
