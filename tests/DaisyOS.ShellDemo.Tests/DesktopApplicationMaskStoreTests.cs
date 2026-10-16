using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopApplicationMaskStoreTests
{
    [Theory]
    [InlineData("diagnostics", "taskmanager")]
    [InlineData("org.daisyos.taskmanager.desktop", "taskmanager")]
    [InlineData("ui-kit", "uikit")]
    [InlineData("org.daisyos.files.desktop", "files")]
    [InlineData("org.example.App.desktop", "org.example.App")]
    [InlineData("org.example.App", "org.example.App")]
    [InlineData("", "")]
    public void ShortIdsCanonicalizeAliasesAndPreserveExternalApplicationIds(string id, string expected)
    {
        Assert.Equal(expected, DesktopApplicationMaskStore.NormalizeShortId(id));
        Assert.Equal(DesktopApplicationMaskStore.NormalizeDesktopId(id),
            DesktopApplicationMaskStore.NormalizeDesktopId(expected));
    }
}
