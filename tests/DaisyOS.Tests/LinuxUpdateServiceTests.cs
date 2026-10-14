using DaisyOS.System.Updates;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxUpdateServiceTests
{
    [Theory]
    [InlineData("avalonia 12.0.4-1 -> 12.0.5-1", "avalonia", "12.0.4-1", "12.0.5-1")]
    [InlineData("linux-firmware 2026.01-2 -> 2026.02-1", "linux-firmware", "2026.01-2", "2026.02-1")]
    public void TryParsePackageLineParsesPacmanOutput(
        string line,
        string expectedName,
        string expectedCurrent,
        string expectedAvailable)
    {
        var package = LinuxUpdateService.TryParsePackageLine(line);

        Assert.NotNull(package);
        Assert.Equal(expectedName, package.Name);
        Assert.Equal(expectedCurrent, package.CurrentVersion);
        Assert.Equal(expectedAvailable, package.AvailableVersion);
        Assert.Equal(0, package.DownloadSize);
    }

    [Theory]
    [InlineData("")]
    [InlineData("invalid")]
    [InlineData("package -> version")]
    public void TryParsePackageLineRejectsMalformedOutput(string line)
    {
        Assert.Null(LinuxUpdateService.TryParsePackageLine(line));
    }
}
