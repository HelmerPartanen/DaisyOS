using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Views.Components.Launcher;
using DaisyOS.System.Power;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class CrashActionTests
{
    [AvaloniaFact]
    public async Task EmptyTrashLeavesTheDispatcherResponsiveDuringBulkDeletion()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-trash-responsive-" + Guid.NewGuid());
        var files = Path.Combine(root, "files");
        await Task.Run(() =>
        {
            Directory.CreateDirectory(files);
            for (var index = 0; index < 5000; index++) File.WriteAllText(Path.Combine(files, index.ToString()), "owned");
        });
        var window = new Window { Content = new Button { Content = "Responsive" }, Width = 100, Height = 100 };
        window.Show();
        try
        {
            var work = FileSystemService.EmptyTrashAsync(root);
            var dispatched = false;
            Dispatcher.UIThread.Post(() => dispatched = true);
            Dispatcher.UIThread.RunJobs();
            Assert.True(dispatched);
            Assert.True(await work);
            Assert.Empty(Directory.EnumerateFileSystemEntries(files));
        }
        finally { window.Close(); Directory.Delete(root, recursive: true); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnexpectedPowerServiceFailuresDoNotEscapeTheMenuHandler(bool synchronous)
    {
        Func<LinuxPowerService, Task<PowerActionResult>> failure = _ => synchronous
            ? throw new InvalidOperationException("Owned power failure")
            : Task.FromException<PowerActionResult>(new InvalidOperationException("Owned power failure"));
        var method = typeof(LauncherView).GetMethod("RunPowerActionAsync", BindingFlags.Static | BindingFlags.NonPublic)!;
        await (Task)method.Invoke(null, [failure])!;
    }
}
