using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class SafeCommandRunnerTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExcessiveOutputStopsTheCommandWithoutReturningPartialParserInput(bool standardError)
    {
        var runner = new SafeCommandRunner(maxCapturedCharacters: 4096);
        var redirect = standardError ? " >&2" : "";
        var result = await runner.RunAsync("/bin/sh", ["-c", "while :; do printf '0123456789abcdef'" + redirect + "; done"], TimeSpan.FromSeconds(5));
        Assert.Equal(125, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Empty(result.StandardOutput);
        Assert.Contains("capture limit", result.StandardError);
    }

    [Fact]
    public async Task ExcessiveFiniteOutputIsAlsoReportedAsFailure()
    {
        var runner = new SafeCommandRunner(maxCapturedCharacters: 16);
        var result = await runner.RunAsync("/bin/sh", ["-c", "printf '012345678901234567890123456789'"], TimeSpan.FromSeconds(5));
        Assert.Equal(125, result.ExitCode);
        Assert.Empty(result.StandardOutput);
    }

    [Fact]
    public async Task RunAsyncCapturesOutputErrorAndExitCode()
    {
        var runner = new SafeCommandRunner();

        var result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "printf output; printf error >&2; exit 7"],
            TimeSpan.FromSeconds(5));

        Assert.Equal(7, result.ExitCode);
        Assert.Equal("output", result.StandardOutput);
        Assert.Equal("error", result.StandardError);
        Assert.False(result.TimedOut);
    }

    [Fact]
    public async Task RunAsyncPassesArgumentsWithoutShellInterpretation()
    {
        const string literal = "$(printf unsafe); semicolon; *";
        var runner = new SafeCommandRunner();

        var result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "printf '%s' \"$1\"", "daisyos-test", literal],
            TimeSpan.FromSeconds(5));

        Assert.True(result.Succeeded);
        Assert.Equal(literal, result.StandardOutput);
    }

    [Fact]
    public async Task RunAsyncReturns127AndLogsWhenExecutableIsMissing()
    {
        var log = new RecordingLogService();
        var runner = new SafeCommandRunner(log);

        var result = await runner.RunAsync(
            "/definitely/missing/daisyos-command",
            [],
            TimeSpan.FromSeconds(1));

        Assert.Equal(127, result.ExitCode);
        Assert.False(result.TimedOut);
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Could not start command"));
    }

    [Fact]
    public async Task RunAsyncKillsAndLogsTimedOutProcess()
    {
        var log = new RecordingLogService();
        var runner = new SafeCommandRunner(log);

        var result = await runner.RunAsync(
            "/bin/sh",
            ["-c", "sleep 10"],
            TimeSpan.FromMilliseconds(100));

        Assert.Equal(124, result.ExitCode);
        Assert.True(result.TimedOut);
        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("timed out"));
    }

    [Fact]
    public async Task RunAsyncLogsNonzeroExit()
    {
        var log = new RecordingLogService();
        var runner = new SafeCommandRunner(log);

        await runner.RunAsync("/bin/sh", ["-c", "exit 2"], TimeSpan.FromSeconds(5));

        Assert.Contains(log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("exit code 2"));
    }

    [Fact]
    public async Task RunAsyncPreservesCallerCancellation()
    {
        var runner = new SafeCommandRunner();
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => runner.RunAsync(
            "/bin/sh",
            ["-c", "sleep 10"],
            TimeSpan.FromSeconds(5),
            cancellation.Token));
    }

    private sealed class RecordingLogService : ILogService
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public void Log(LogLevel level, string message, Exception? exception = null)
        {
            Entries.Add((level, message));
        }
    }
}
