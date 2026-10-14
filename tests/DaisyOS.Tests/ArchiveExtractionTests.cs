using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class ArchiveExtractionTests
{
    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, false, false)]
    [InlineData(0, true, false)]
    public async Task ExtractionReportsActualCommandOutcome(int exitCode, bool timeout, bool expected)
    {
        var destination = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var result = await ArchiveExtractionService.ExtractAsync("example.tar.gz", destination,
                new Runner(new CommandResult(exitCode, "", "", timeout)));
            Assert.Equal(expected, result);
        }
        finally { Directory.Delete(destination, true); }
    }

    [Fact]
    public void PermissionFailureHasUsefulRecoveryWithoutRawExceptionDetails()
    {
        var message = FilesErrorMessages.Describe("Couldn’t open this folder", new UnauthorizedAccessException("internal detail"));
        Assert.Contains("permission", message);
        Assert.DoesNotContain("internal detail", message);
    }

    private sealed class Runner(CommandResult result) : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
            => Task.FromResult(result);
    }
}
