using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxAppLauncherServiceTests
{
    [Fact]
    public void MailIsDiscoverableAsANativeSystemApplication()
    {
        var mail = Assert.Single(new LinuxAppLauncherService().GetAvailableApps(), a => a.Id == "org.daisyos.mail.desktop");
        Assert.Equal("Mail", mail.Name);
        Assert.Equal(DaisyOS.Core.Models.AppLaunchKind.Internal, mail.LaunchKind);
        Assert.Contains("Gmail", mail.SearchTerms!);
    }

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
            // KIO correctly refuses to execute untrusted desktop files outside
            // application directories; model a user-trusted desktop shortcut.
            if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            var result = await new LinuxAppLauncherService().LaunchDesktopEntryFileAsync(path);

            Assert.True(result.Succeeded, result.Message + " (" + result.ErrorCode + ")");
        }
        finally
        {
            File.Delete(path);
        }
    }
}
