using DaisyOS.Core.Helpers;
using Xunit;

namespace DaisyOS.Tests;

public sealed class AsyncRelayCommandTests
{
    [Fact]
    public async Task ExecutePreventsOverlappingRuns()
    {
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var runCount = 0;
        using var command = new AsyncRelayCommand(async () =>
        {
            runCount++;
            started.SetResult();
            await release.Task;
        });

        command.Execute(null);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(1));
        command.Execute(null);

        Assert.True(command.IsRunning);
        Assert.False(command.CanExecute(null));
        Assert.Equal(1, runCount);

        release.SetResult();
        await WaitUntilAsync(() => !command.IsRunning);
        Assert.True(command.CanExecute(null));
    }

    [Fact]
    public async Task ExecutionFailureIsReportedWithoutEscapingAsyncVoid()
    {
        using var command = new AsyncRelayCommand(() => Task.FromException(new InvalidOperationException("failed")));
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        command.ExecutionFailed += (_, exception) => failure.SetResult(exception);

        command.Execute(null);

        var exception = await failure.Task.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.Equal("failed", exception.Message);
        await WaitUntilAsync(() => !command.IsRunning);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var timeout = DateTime.UtcNow.AddSeconds(1);
        while (!condition() && DateTime.UtcNow < timeout)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }
}
