using DaisyOS.System.Processes;

namespace DaisyOS.Tests;

internal sealed class ScriptedCommandRunner : ICommandRunner
{
    private readonly Queue<CommandResult> _results;
    private readonly CommandResult? _fallback;

    public ScriptedCommandRunner(params CommandResult[] results)
    {
        _results = new Queue<CommandResult>(results);
        if (results.Length == 1)
        {
            _fallback = results[0];
        }
    }

    public List<CommandCall> Calls { get; } = [];

    public Task<CommandResult> RunAsync(
        string fileName,
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Calls.Add(new CommandCall(fileName, arguments.ToArray(), timeout));

        if (_results.Count == 0)
        {
            if (_fallback is not null)
            {
                return Task.FromResult(_fallback);
            }
            throw new InvalidOperationException($"No scripted result is available for {fileName}.");
        }

        return Task.FromResult(_results.Dequeue());
    }

    internal sealed record CommandCall(string FileName, IReadOnlyList<string> Arguments, TimeSpan Timeout);
}
