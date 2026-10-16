using System.ComponentModel;
using System.Diagnostics;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Processes;

public sealed class SafeProcessLauncher
{
    private readonly ILogService _log;
    private const string DaisyOsAppMemorySlice = "daisyos-apps.slice";

    public SafeProcessLauncher(ILogService? log = null)
    {
        _log = log ?? NullLogService.Instance;
    }

    public AppLaunchResult Launch(string fileName, IReadOnlyList<string>? arguments = null)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Contains('\0', StringComparison.Ordinal))
        {
            return new AppLaunchResult(false, "Launch target was invalid.");
        }

        var placeInMemoryBoundary = ShouldPlaceInDaisyOsMemoryBoundary();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        if (arguments is not null)
        {
            foreach (var argument in arguments)
            {
                if (argument.Contains('\0', StringComparison.Ordinal))
                {
                    return new AppLaunchResult(false, "Launch argument was invalid.");
                }

                startInfo.ArgumentList.Add(argument);
            }
        }

        EnsureDisplayEnvironment(startInfo);

        try
        {
            using var process = Process.Start(startInfo);

            // The native session protects KWin and DaisyOS in a separate cgroup.
            // We launch the app directly (inheriting the launcher's environment,
            // including the per-session WAYLAND_DISPLAY and KWin's private
            // Wayland socket) and then move it into the aggregate app memory
            // boundary cgroup post-launch. This avoids the 50-200ms overhead of
            // systemd-run's fork + D-Bus scope creation, while still applying
            // the memory limit. The child keeps the correct environment because
            // it was forked from this process — unlike a systemd service, which
            // starts from the user manager's (potentially stale) environment.
            if (placeInMemoryBoundary && process?.Id is > 0)
            {
                TryMoveToAppSlice(process.Id);
            }

            return new AppLaunchResult(true, "Launch requested.", process?.Id);
        }
        catch (Exception ex) when (ex is InvalidOperationException or Win32Exception)
        {
            _log.Log(LogLevel.Warning, $"Could not launch process: {fileName}", ex);
            return new AppLaunchResult(false, $"Could not start {fileName}.");
        }
    }

    private void TryMoveToAppSlice(int pid)
    {
        try
        {
            var uid = GetCurrentUid();
            if (uid is null)
            {
                _log.Log(LogLevel.Warning, "Could not determine current uid for cgroup assignment.");
                return;
            }

            var slicePath = $"/sys/fs/cgroup/user.slice/user-{uid}.slice/user@{uid}.service/{DaisyOsAppMemorySlice}";
            var procsPath = Path.Combine(slicePath, "cgroup.procs");

            // If the slice directory doesn't exist, try to create it.
            // cgroup v2 supports mkdir to create sub-cgroups.
            if (!Directory.Exists(slicePath))
            {
                try
                {
                    Directory.CreateDirectory(slicePath);
                }
                catch (Exception ex)
                {
                    _log.Log(LogLevel.Warning,
                        $"Could not create cgroup slice directory: {slicePath}", ex);
                    return;
                }
            }

            File.WriteAllText(procsPath, pid.ToString());
        }
        catch (Exception ex)
        {
            // The app is already running — just not memory-bounded.
            _log.Log(LogLevel.Warning,
                $"Could not move PID {pid} into {DaisyOsAppMemorySlice} cgroup.", ex);
        }
    }

    private static string? GetCurrentUid()
    {
        // Prefer the proc filesystem (always accurate on Linux).
        try
        {
            foreach (var line in File.ReadLines("/proc/self/status"))
            {
                if (line.StartsWith("Uid:", StringComparison.Ordinal))
                {
                    // Format: "Uid:\t<real>\t<effective>\t<saved>\t<fs>"
                    var parts = line.AsSpan(4).Trim().ToString().Split('\t');
                    if (parts.Length > 0 && !string.IsNullOrWhiteSpace(parts[0]))
                    {
                        return parts[0];
                    }
                }
            }
        }
        catch
        {
            // Fall through to environment-based lookup.
        }

        // Fallback: XDG_RUNTIME_DIR is typically /run/user/{uid}.
        var runtimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (runtimeDir is not null)
        {
            var lastSegment = Path.GetFileName(runtimeDir.AsSpan());
            if (!lastSegment.IsEmpty && int.TryParse(lastSegment, out _))
            {
                return lastSegment.ToString();
            }
        }

        return null;
    }

    private static bool ShouldPlaceInDaisyOsMemoryBoundary() =>
        OperatingSystem.IsLinux() &&
        string.Equals(
            Environment.GetEnvironmentVariable("DAISYOS_MEMORY_APP_SLICE"),
            DaisyOsAppMemorySlice,
            StringComparison.Ordinal) &&
        Directory.Exists("/sys/fs/cgroup");

    private static void EnsureDisplayEnvironment(ProcessStartInfo startInfo)
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        if ((Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
            .Split(':').Contains("DaisyOS", StringComparer.OrdinalIgnoreCase))
        {
            startInfo.Environment["GTK_USE_PORTAL"] = "1";
            startInfo.Environment["GIO_USE_PORTALS"] = "1";
            startInfo.Environment["QT_QPA_PLATFORMTHEME"] = "xdgdesktopportal";
        }
        var waylandDisplay = Environment.GetEnvironmentVariable("WAYLAND_DISPLAY");
        var display = Environment.GetEnvironmentVariable("DISPLAY");
        var authority = Environment.GetEnvironmentVariable("XAUTHORITY");
        if (!string.IsNullOrWhiteSpace(waylandDisplay)
            && GraphicalSessionEnvironment.ResolveXWayland(waylandDisplay) is { } session)
        {
            display = session.Display;
            authority = session.Authority;
            // An inherited Xauthority may belong to a different display-manager session.
            startInfo.Environment.Remove("XAUTHORITY");
        }
        if (!string.IsNullOrWhiteSpace(display)) startInfo.Environment["DISPLAY"] = display;
        if (!string.IsNullOrWhiteSpace(authority)) startInfo.Environment["XAUTHORITY"] = authority;
        if (!string.IsNullOrWhiteSpace(waylandDisplay)) startInfo.Environment["WAYLAND_DISPLAY"] = waylandDisplay;
    }

}
