using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxAppLauncherServiceTests
{
    [Fact]
    public async Task LaunchDesktopEntryFileAsync_LaunchesAValidDesktopShortcut()
    {
        var path = Path.Combine(Path.GetTempPath(), $"desktop-shortcut-{Guid.NewGuid():N}.desktop");
        await File.WriteAllTextAsync(path, """
            [Desktop Entry]
            Type=Application
            Name=Launch test
            Exec=/usr/bin/true
            Icon=utilities-terminal
            """);

        try
        {
            var result = await new LinuxAppLauncherService().LaunchDesktopEntryFileAsync(path);

            Assert.True(result.Succeeded, result.Message);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
