using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Power;

public sealed class LinuxPowerService : IPowerService
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(10);
    private readonly ICommandRunner _commandRunner;

    public LinuxPowerService(ICommandRunner commandRunner)
    {
        _commandRunner = commandRunner;
    }

    public Task<PowerActionResult> ShutdownAsync(CancellationToken cancellationToken = default)
    {
        return RunSystemctlAsync("poweroff", cancellationToken);
    }

    public Task<PowerActionResult> RebootAsync(CancellationToken cancellationToken = default)
    {
        return RunSystemctlAsync("reboot", cancellationToken);
    }

    public Task<PowerActionResult> SuspendAsync(CancellationToken cancellationToken = default)
    {
        return RunSystemctlAsync("suspend", cancellationToken);
    }

    public async Task<PowerActionResult> LogOutAsync(CancellationToken cancellationToken = default)
    {
        if (string.Equals(
                Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION"),
                "1",
                StringComparison.Ordinal))
        {
            var supervisorPid = ResolveDaisySessionSupervisorPid();
            if (supervisorPid is null)
                return new PowerActionResult(false, "Could not determine the DaisyOS session supervisor.");

            var helper = Path.Combine(AppContext.BaseDirectory, "daisyos-session-logout");
            var runtime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
            if (File.Exists(helper) && !string.IsNullOrWhiteSpace(runtime) &&
                ReadRuntimePid(runtime, "supervisor.pid") == supervisorPid &&
                ReadRuntimePid(runtime, "shell.pid") is { } shellPid &&
                ReadRuntimePid(runtime, "kwin.pid") is { } compositorPid)
            {
                // Independent of the shell being stopped; it also guards older
                // installed supervisors that can block while waiting for SIGTERM.
                var guardedResult = await _commandRunner.RunAsync("systemd-run",
                    ["--user", "--quiet", "--collect", "--slice=daisyos-core.slice", "--property=Type=exec", "--",
                     "bash", helper, supervisorPid.Value.ToString(), shellPid.ToString(), compositorPid.ToString()],
                    CommandTimeout, cancellationToken);
                return new PowerActionResult(guardedResult.Succeeded, guardedResult.Succeeded
                    ? "Requested guarded DaisyOS session logout."
                    : $"Could not request DaisyOS session logout: {guardedResult.StandardError}");
            }

            // The session supervisor owns both KWin and the shell. Ask it to
            // unwind those processes in order and return control to the display
            // manager instead of abruptly terminating a guessed logind session.
            var stopResult = await _commandRunner.RunAsync(
                "kill", ["-TERM", supervisorPid.Value.ToString()], CommandTimeout, cancellationToken);

            var stopMessage = stopResult.Succeeded
                ? "Requested DaisyOS session logout."
                : $"Could not request DaisyOS session logout: {stopResult.StandardError}";

            return new PowerActionResult(stopResult.Succeeded, stopMessage.Trim());
        }

        var sessionId = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        if (string.IsNullOrWhiteSpace(sessionId))
            return new PowerActionResult(false, "No explicit graphical session is available to log out.");

        var result = await _commandRunner.RunAsync(
            "loginctl", ["terminate-session", sessionId], CommandTimeout, cancellationToken);

        var message = result.Succeeded
            ? "Requested session logout."
            : $"Could not request session logout: {result.StandardError}";

        return new PowerActionResult(result.Succeeded, message.Trim());
    }

    private static int? ResolveDaisySessionSupervisorPid()
    {
        var value = Environment.GetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID");
        if (TryParseProcessId(value, out var processId))
            return processId;

        // Compatibility with already-installed session launchers: they publish
        // this owner-only runtime file even before the explicit environment
        // variable was introduced.
        var runtimeDirectory = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (string.IsNullOrWhiteSpace(runtimeDirectory))
            return null;

        try
        {
            value = File.ReadAllText(Path.Combine(
                runtimeDirectory, "daisyos-native-development", "supervisor.pid"));
            return TryParseProcessId(value, out processId) ? processId : null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static int? ReadRuntimePid(string runtime, string name)
    {
        try
        {
            return TryParseProcessId(File.ReadAllText(Path.Combine(runtime, "daisyos-native-development", name)), out var pid) ? pid : null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException) { return null; }
    }

    private static bool TryParseProcessId(string? value, out int processId)
    {
        return int.TryParse(value?.Trim(), out processId) && processId > 1;
    }

    public async Task<string> GetPowerProfileAsync(CancellationToken cancellationToken = default)
    {
        var result = await _commandRunner.RunAsync("powerprofilesctl", ["get"], TimeSpan.FromSeconds(2), cancellationToken);
        if (result.Succeeded && !string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            var mode = result.StandardOutput.Trim().ToLowerInvariant();
            return mode switch
            {
                "power-saver" => "Power saver",
                "performance" => "Performance",
                _ => "Balanced"
            };
        }
        return "Balanced";
    }

    public async Task<bool> SetPowerProfileAsync(string profile, CancellationToken cancellationToken = default)
    {
        var mode = profile.ToLowerInvariant() switch
        {
            "power saver" or "power-saver" => "power-saver",
            "performance" => "performance",
            _ => "balanced"
        };
        var result = await _commandRunner.RunAsync("powerprofilesctl", ["set", mode], TimeSpan.FromSeconds(3), cancellationToken);
        return result.Succeeded;
    }

    private async Task<PowerActionResult> RunSystemctlAsync(string action, CancellationToken cancellationToken)
    {
        var result = await _commandRunner.RunAsync("systemctl", [action], CommandTimeout, cancellationToken);
        var message = result.Succeeded
            ? $"Requested system {action}."
            : $"Could not request system {action}: {result.StandardError}";

        return new PowerActionResult(result.Succeeded, message.Trim());
    }
}
