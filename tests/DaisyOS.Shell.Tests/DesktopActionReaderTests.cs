using System;
using System.IO;
using DaisyOS.Shell.Services;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class DesktopActionReaderTests
{
    [Fact]
    public void GetActions_ParsesDesktopActionsFromMetadataCorrectly()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"test-app-{Guid.NewGuid():N}.desktop");
        File.WriteAllText(tempFile, """
            [Desktop Entry]
            Name=Google Chrome
            Exec=/usr/bin/google-chrome-stable %U
            Actions=new-window;new-private-window;

            [Desktop Action new-window]
            Name=New Window
            Exec=/usr/bin/google-chrome-stable

            [Desktop Action new-private-window]
            Name=New Incognito Window
            Exec=/usr/bin/google-chrome-stable --incognito
            """);

        try
        {
            var actions = DesktopActionReader.GetActions(tempFile);

            Assert.Equal(2, actions.Count);
            Assert.Equal("new-window", actions[0].Id);
            Assert.Equal("New Window", actions[0].Name);
            Assert.Equal("/usr/bin/google-chrome-stable", actions[0].Exec);

            Assert.Equal("new-private-window", actions[1].Id);
            Assert.Equal("New Incognito Window", actions[1].Name);
            Assert.Equal("/usr/bin/google-chrome-stable --incognito", actions[1].Exec);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public void GetActions_ReturnsEmpty_WhenNoActionsDefined()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"simple-app-{Guid.NewGuid():N}.desktop");
        File.WriteAllText(tempFile, """
            [Desktop Entry]
            Name=Simple App
            Exec=/usr/bin/simple-app
            """);

        try
        {
            var actions = DesktopActionReader.GetActions(tempFile);
            Assert.Empty(actions);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
