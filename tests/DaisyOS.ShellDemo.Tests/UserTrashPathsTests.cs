using DaisyOS.Shell.Apps.Files;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class UserTrashPathsTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("relative/data")]
    public void MissingOrRelativeDataHomeUsesTheUserHome(string? dataHome)
    {
        Assert.Equal("/home/example/.local/share/Trash", UserTrashPaths.ResolveRoot("/home/example", dataHome));
    }

    [Fact]
    public void AbsoluteDataHomeUsesTheSameRootForFilesAndMetadata()
    {
        var root = UserTrashPaths.ResolveRoot("/home/example", "/tmp/example-data/");
        Assert.Equal("/tmp/example-data/Trash", root);
        Assert.Equal("/tmp/example-data/Trash/files", Path.Combine(root, "files"));
        Assert.Equal("/tmp/example-data/Trash/info", Path.Combine(root, "info"));
    }
}
