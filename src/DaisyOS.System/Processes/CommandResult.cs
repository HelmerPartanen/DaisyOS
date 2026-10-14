namespace DaisyOS.System.Processes;

public sealed record CommandResult(int ExitCode, string StandardOutput, string StandardError, bool TimedOut)
{
    public bool Succeeded => ExitCode == 0 && !TimedOut;

    public static CommandResult Success(string stdout = "", int exitCode = 0) =>
        new(exitCode, stdout, string.Empty, TimedOut: false);

    public static CommandResult Failed(string stderr = "", int exitCode = 1, string stdout = "") =>
        new(exitCode, stdout, stderr, TimedOut: false);
}

