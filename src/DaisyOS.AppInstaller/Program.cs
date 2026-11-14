using DaisyOS.Shell.Apps.AppInstaller;
using DaisyOS.Shell.Services;

namespace DaisyOS.AppInstaller;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("appinstaller", args,
        _ => new AppInstallerWindow(),
        (window, activation) =>
        {
            if (window is not AppInstallerWindow installer) return;
            var autoInstall = activation.Any(arg => string.Equals(arg, "--install", StringComparison.OrdinalIgnoreCase) || string.Equals(arg, "-i", StringComparison.OrdinalIgnoreCase));
            var path = activation
                .Where(arg => !arg.StartsWith('-'))
                .Select(ToLocalPath)
                .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) &&
                    DaisyOS.System.Applications.ApplicationManagementService.SupportsFile(candidate));
            if (path is not null)
            {
                if (autoInstall)
                    _ = installer.InstallFileAsync(path);
                else
                    _ = installer.OpenFileAsync(path);
            }
        });

    private static string? ToLocalPath(string argument)
    {
        if (string.IsNullOrWhiteSpace(argument)) return null;
        var trimmed = argument.Trim('"', '\'', ' ');
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.IsFile) return uri.LocalPath;
        try { return Path.GetFullPath(trimmed); }
        catch { return null; }
    }
}