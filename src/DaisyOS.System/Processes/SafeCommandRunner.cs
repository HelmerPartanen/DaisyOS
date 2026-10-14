using System.Diagnostics;
using System.Text;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;

namespace DaisyOS.System.Processes;

public sealed class SafeCommandRunner : ICommandRunner
{
    private readonly ILogService _log;
    private readonly int _maxCapturedCharacters;

    public SafeCommandRunner(ILogService? log = null, int maxCapturedCharacters = 8 * 1024 * 1024)
    {
        _log = log ?? NullLogService.Instance;
        ArgumentOutOfRangeException.ThrowIfLessThan(maxCapturedCharacters, 1);
        _maxCapturedCharacters = maxCapturedCharacters;
    }

    public async Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };

        // Service parsers consume a stable machine-facing language, regardless
        // of the user-selected GUI locale. Keep Unicode names intact.
        startInfo.Environment["LC_ALL"] = "C.UTF-8";
        startInfo.Environment["LANGUAGE"] = "C";

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = new Process { StartInfo = startInfo };

        try
        {
            process.Start();
        }
        catch (Exception ex) when (ex is InvalidOperationException or global::System.ComponentModel.Win32Exception)
        {
            _log.Log(LogLevel.Warning, $"Could not start command: {FormatCommand(fileName, arguments)}", ex);
            return new CommandResult(127, string.Empty, ex.Message, TimedOut: false);
        }

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(timeout);
        var outputLimitExceeded = 0;
        async Task<string> ReadOutputAsync(StreamReader reader)
        {
            var output = new StringBuilder();
            var buffer = new char[4096];
            while (true)
            {
                var count = await reader.ReadAsync(buffer.AsMemory(), timeoutCts.Token);
                if (count == 0) return output.ToString();
                if (count > _maxCapturedCharacters - output.Length)
                {
                    Interlocked.Exchange(ref outputLimitExceeded, 1);
                    timeoutCts.Cancel();
                    return string.Empty;
                }
                output.Append(buffer, 0, count);
            }
        }
        var stdoutTask = ReadOutputAsync(process.StandardOutput);
        var stderrTask = ReadOutputAsync(process.StandardError);

        try
        {
            await process.WaitForExitAsync(timeoutCts.Token);
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            if (Volatile.Read(ref outputLimitExceeded) != 0)
                return new CommandResult(125, string.Empty, "Command output exceeded the capture limit.", TimedOut: false);
            var result = new CommandResult(process.ExitCode, stdout, stderr, TimedOut: false);
            if (!result.Succeeded)
            {
                _log.Log(
                    LogLevel.Warning,
                    $"Command failed with exit code {result.ExitCode}: {FormatCommand(fileName, arguments)}{FormatError(result.StandardError)}");
            }

            return result;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            KillProcess(process);

            if (Volatile.Read(ref outputLimitExceeded) != 0)
            {
                _log.Log(LogLevel.Warning, $"Command exceeded its output limit: {FormatCommand(fileName, arguments)}");
                return new CommandResult(125, string.Empty, "Command output exceeded the capture limit.", TimedOut: false);
            }

            _log.Log(LogLevel.Warning, $"Command timed out after {timeout}: {FormatCommand(fileName, arguments)}");
            return new CommandResult(124, string.Empty, "Command timed out.", TimedOut: true);
        }
        catch (OperationCanceledException)
        {
            KillProcess(process);
            throw;
        }
        finally
        {
            // Observe cancelled pipe readers before their process/CTS are disposed.
            try { await Task.WhenAll(stdoutTask, stderrTask); }
            catch (OperationCanceledException) { }
        }
    }

    private static void KillProcess(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
        }
        catch (global::System.ComponentModel.Win32Exception)
        {
            // The child may have exited between HasExited and Kill.
        }
    }

    private static string FormatCommand(string fileName, IReadOnlyList<string> arguments)
    {
        return arguments.Count == 0 ? fileName : $"{fileName} ({arguments.Count} args)";
    }

    private static string FormatError(string standardError)
    {
        var error = standardError.Trim();
        return string.IsNullOrEmpty(error) ? string.Empty : $" Error: {error}";
    }
}
