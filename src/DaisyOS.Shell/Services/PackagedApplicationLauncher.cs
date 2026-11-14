using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

/// <summary>Launches the installed applications, with a development-checkout fallback.</summary>
public static class PackagedApplicationLauncher
{
    public static Task<AppLaunchResult> LaunchFilesAsync(FileManagerActivationRequest? activation = null) =>
        Task.Run(() => LaunchFiles(activation));
    public static Task<AppLaunchResult> LaunchAsync(string application, IEnumerable<string>? arguments = null) =>
        Task.Run(() => Launch(application, arguments));

    public static AppLaunchResult LaunchFiles(FileManagerActivationRequest? activation = null)
    {
        var arguments = new List<string>();
        if (activation?.SelectItems == true) arguments.Add("--select");
        arguments.AddRange(activation?.Paths ?? []);
        return Launch("Files", arguments);
    }
    public static AppLaunchResult Launch(string application, IEnumerable<string>? arguments = null)
    {
        if (DaisyOS.System.Applications.OptionalApplicationState.IsRemoved(application))
            return new AppLaunchResult(false, "This optional application was removed. Reinstall it from App Store or an application package.");
        var binary = Environment.GetEnvironmentVariable("DAISYOS_" + application.ToUpperInvariant() + "_BINARY");
        if (string.IsNullOrWhiteSpace(binary))
        {
            binary = Path.Combine(AppContext.BaseDirectory, "apps", application, "DaisyOS." + application);
            if (!File.Exists(binary))
            {
                var output = new DirectoryInfo(AppContext.BaseDirectory);
                var source = output.Parent?.Parent?.Parent?.Parent;
                var developmentBinary = source is null ? null : Path.Combine(source.FullName,
                    "DaisyOS." + application, "bin",
                    Environment.GetEnvironmentVariable("DAISYOS_APP_CONFIGURATION") ?? "Release", output.Name, "DaisyOS." + application);
                binary = developmentBinary is not null && File.Exists(developmentBinary)
                    ? developmentBinary : "daisyos-" + application.ToLowerInvariant();
            }
        }
        return new SafeProcessLauncher().Launch(binary, arguments?.ToArray() ?? []);
    }
}
