using DaisyOS.System.Power;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxPowerServiceTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LogOutAsync_StartsIndependentGuardAndDoesNotFallBackAfterServiceFailure(bool success)
    {
        var previousShell = Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION");
        var previousSupervisor = Environment.GetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID");
        var previousRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var runtime = Path.Combine(Path.GetTempPath(), "daisy-logout-" + Guid.NewGuid());
        var state = Path.Combine(runtime, "daisyos-native-development");
        Directory.CreateDirectory(state);
        try
        {
            foreach (var pair in new[] { ("supervisor.pid", "4242"), ("shell.pid", "4243"), ("kwin.pid", "4244") })
                await File.WriteAllTextAsync(Path.Combine(state, pair.Item1), pair.Item2);
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", "1");
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", "4242");
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtime);
            Assert.True(File.Exists(Path.Combine(AppContext.BaseDirectory, "daisyos-session-logout")));
            var runner = new ScriptedCommandRunner(success ? CommandResult.Success() : CommandResult.Failed("fixture"));
            var result = await new LinuxPowerService(runner).LogOutAsync();
            Assert.Equal(success, result.Succeeded);
            var call = Assert.Single(runner.Calls);
            Assert.Equal("systemd-run", call.FileName);
            Assert.Contains("--property=Type=exec", call.Arguments);
            Assert.Equal(new[] { "4242", "4243", "4244" }, call.Arguments.TakeLast(3));
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", previousShell);
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", previousSupervisor);
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", previousRuntime);
            Directory.Delete(runtime, recursive: true);
        }
    }

    [Fact]
    public async Task LogOutAsync_SignalsDaisySessionSupervisor()
    {
        var oldSession = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        var oldShellSession = Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION");
        var oldSupervisor = Environment.GetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_ID", null);
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", "1");
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", "4242");
            var runner = new ScriptedCommandRunner(CommandResult.Success());

            var result = await new LinuxPowerService(runner).LogOutAsync();

            Assert.True(result.Succeeded);
            var call = Assert.Single(runner.Calls);
            Assert.Equal("kill", call.FileName);
            Assert.Equal(["-TERM", "4242"], call.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_ID", oldSession);
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", oldShellSession);
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", oldSupervisor);
        }
    }

    [Fact]
    public async Task LogOutAsync_FailsSafely_WhenNoDisplaySessionCanBeResolved()
    {
        var oldSession = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        var oldShellSession = Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION");
        var oldSupervisor = Environment.GetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_ID", null);
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", null);
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", null);
            var runner = new ScriptedCommandRunner(CommandResult.Success());

            var result = await new LinuxPowerService(runner).LogOutAsync();

            Assert.False(result.Succeeded);
            Assert.Empty(runner.Calls);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_ID", oldSession);
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", oldShellSession);
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", oldSupervisor);
        }
    }

    [Fact]
    public async Task LogOutAsync_UsesOnlyExplicitLogindSessionOutsideDaisySession()
    {
        var oldSession = Environment.GetEnvironmentVariable("XDG_SESSION_ID");
        var oldShellSession = Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION");
        try
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_ID", "7");
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", null);
            var runner = new ScriptedCommandRunner(CommandResult.Success());

            var result = await new LinuxPowerService(runner).LogOutAsync();

            Assert.True(result.Succeeded);
            var call = Assert.Single(runner.Calls);
            Assert.Equal("loginctl", call.FileName);
            Assert.Equal(["terminate-session", "7"], call.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_SESSION_ID", oldSession);
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", oldShellSession);
        }
    }

    [Fact]
    public async Task LogOutAsync_UsesPublishedSupervisorFileForInstalledSessionCompatibility()
    {
        var oldShellSession = Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION");
        var oldSupervisor = Environment.GetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID");
        var oldRuntime = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        var runtime = Path.Combine(Path.GetTempPath(), $"daisyos-power-tests-{Guid.NewGuid():N}");
        try
        {
            var sessionRuntime = Path.Combine(runtime, "daisyos-native-development");
            Directory.CreateDirectory(sessionRuntime);
            await File.WriteAllTextAsync(Path.Combine(sessionRuntime, "supervisor.pid"), "5252\n");
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", "1");
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", null);
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", runtime);
            var runner = new ScriptedCommandRunner(CommandResult.Success());

            var result = await new LinuxPowerService(runner).LogOutAsync();

            Assert.True(result.Succeeded);
            var call = Assert.Single(runner.Calls);
            Assert.Equal("kill", call.FileName);
            Assert.Equal(["-TERM", "5252"], call.Arguments);
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", oldShellSession);
            Environment.SetEnvironmentVariable("DAISYOS_SESSION_SUPERVISOR_PID", oldSupervisor);
            Environment.SetEnvironmentVariable("XDG_RUNTIME_DIR", oldRuntime);
            Directory.Delete(runtime, recursive: true);
        }
    }
}
