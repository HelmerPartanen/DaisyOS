using DaisyOS.Shell.Apps.Files.Services;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class FileManagerPathTests
{
    [Fact]
    public void LocalFileUri_IsDecodedToAnAbsolutePath()
    {
        Assert.True(FileManagerPath.TryGetLocalPath("file:///tmp/one%20two.txt", out var path));
        Assert.Equal("/tmp/one two.txt", path);
    }

    [Theory]
    [InlineData("https://example.com/file.txt")]
    [InlineData("")]
    [InlineData("  ")]
    public void NonLocalOrEmptyValues_AreRejected(string value)
    {
        Assert.False(FileManagerPath.TryGetLocalPath(value, out _));
    }
}
