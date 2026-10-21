using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Desktop;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class TrashStateObserverTests
{
    [AvaloniaFact]
    public async Task MissingTrashCreationRenameAndDeletionUpdateWithoutPolling()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-trash-watch-" + Guid.NewGuid());
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "Trash", "files");
        var states = new List<bool>();
        using var observer = new TrashStateObserver(path, states.Add);
        try
        {
            await WaitFor(() => states.Count == 1);
            Assert.False(states[^1]);
            Directory.CreateDirectory(path);
            File.WriteAllText(Path.Combine(path, "one"), "test");
            await WaitFor(() => states.Count == 2);
            Assert.True(states[^1]);
            File.Move(Path.Combine(path, "one"), Path.Combine(root, "removed"));
            await WaitFor(() => states.Count == 3);
            Assert.False(states[^1]);
            observer.Dispose();
            File.WriteAllText(Path.Combine(path, "two"), "test");
            await Task.Delay(250);
            Assert.Equal(3, states.Count);
        }
        finally { observer.Dispose(); Directory.Delete(root, true); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(20);
            Dispatcher.UIThread.RunJobs();
        }
        Assert.True(condition(), "Trash watcher did not publish the expected state.");
    }
}
