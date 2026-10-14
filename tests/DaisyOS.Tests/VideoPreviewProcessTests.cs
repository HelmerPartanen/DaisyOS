using System.Diagnostics;
using DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;
using Xunit;

namespace DaisyOS.Tests;

public sealed class VideoPreviewProcessTests
{
    [Fact]
    public async Task CancellationStopsOnlyItsHelperAndRemovesTemporaryPixels()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("daisy-preview-test-");
        try
        {
            var helper = Path.Combine(directory.FullName, "helper");
            var record = Path.Combine(directory.FullName, "child record");
            await File.WriteAllTextAsync(helper, "#!/bin/sh\nprintf '%s\\n%s\\n' \"$$\" \"$4\" > \"$1\"\nexec sleep 30\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            using var stop = new CancellationTokenSource();
            var task = MpvWallpaperSession.PreviewWithHelperAsync(helper, record, 16, 16, stop.Token);
            var deadline = Stopwatch.StartNew();
            while (!File.Exists(record) || (await File.ReadAllLinesAsync(record)).Length < 2)
            {
                Assert.True(deadline.Elapsed < TimeSpan.FromSeconds(5), "Preview helper did not start.");
                await Task.Delay(10);
            }
            var values = await File.ReadAllLinesAsync(record);
            using var child = Process.GetProcessById(int.Parse(values[0]));
            stop.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.True(child.HasExited);
            Assert.False(Directory.Exists(Path.GetDirectoryName(values[1])));
        }
        finally { directory.Delete(recursive: true); }
    }

    [Fact]
    public async Task TruncatedHelperOutputIsRejectedAndCleanedUp()
    {
        if (!OperatingSystem.IsLinux()) return;
        var directory = Directory.CreateTempSubdirectory("daisy-preview-test-");
        try
        {
            var helper = Path.Combine(directory.FullName, "helper");
            var record = Path.Combine(directory.FullName, "output record");
            await File.WriteAllTextAsync(helper, "#!/bin/sh\nprintf '%s' \"$4\" > \"$1\"\nprintf 'x' > \"$4\"\n");
            File.SetUnixFileMode(helper, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Assert.Null(await MpvWallpaperSession.PreviewWithHelperAsync(helper, record, 16, 16, CancellationToken.None));
            Assert.False(Directory.Exists(Path.GetDirectoryName(await File.ReadAllTextAsync(record))));
        }
        finally { directory.Delete(recursive: true); }
    }
}
