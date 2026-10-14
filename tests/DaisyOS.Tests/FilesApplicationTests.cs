using DaisyOS.Files;
using Xunit;

namespace DaisyOS.Tests;

public sealed class FilesApplicationTests
{
    [Fact]
    public void ActivationPreservesSpacesAndSelectionAndRejectsRemoteUris()
    {
        var path = Path.Combine(Path.GetTempPath(), "folder with spaces", "file.txt");
        var request = Program.ParseActivation(["--select", new Uri(path).AbsoluteUri, "https://example.com/file"]);
        Assert.True(request.SelectItems);
        Assert.Equal([path], request.Paths);
    }

    [Fact]
    public void EmptyActivationOpensDefaultLocationWithoutSelection()
    {
        var request = Program.ParseActivation([]);
        Assert.Empty(request.Paths);
        Assert.False(request.SelectItems);
    }
}
