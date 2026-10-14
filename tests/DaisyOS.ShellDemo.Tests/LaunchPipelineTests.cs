using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LaunchPipelineTests
{
    [Fact]
    public void ShortcutLabelsDoNotChangeLaunchIdentityButExecOverridesDo()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var installed = Path.Combine(directory, "installed.desktop");
            var shortcut = Path.Combine(directory, "shortcut.desktop");
            File.WriteAllText(installed, "[Desktop Entry]\nType=Application\nName=Files\nName[fi]=Tiedostot\nExec=app %U\nTerminal=false\n");
            File.WriteAllText(shortcut, "[Desktop Entry]\nName=My Files\nType=Application\nExec=app %U\nTerminal=false\n");
            Assert.Equal(DaisyOS.Shell.App.DesktopLaunchMetadata(installed), DaisyOS.Shell.App.DesktopLaunchMetadata(shortcut));
            File.AppendAllText(shortcut, "Path=/custom-working-directory\n");
            Assert.NotEqual(DaisyOS.Shell.App.DesktopLaunchMetadata(installed), DaisyOS.Shell.App.DesktopLaunchMetadata(shortcut));
            File.WriteAllText(shortcut, "[Desktop Entry]\nType=Application\nExec=other-app --custom\nTerminal=false\n");
            Assert.NotEqual(DaisyOS.Shell.App.DesktopLaunchMetadata(installed), DaisyOS.Shell.App.DesktopLaunchMetadata(shortcut));
        }
        finally { Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public async Task SyntheticInputDoesNotFabricateActivationTokens()
    {
        Assert.Equal("", await Avalonia.Wayland.WaylandActivation.RequestAsync(new KeyEventArgs { Key = Key.Enter }, "fixture"));
        Assert.Equal("", await Avalonia.Wayland.WaylandActivation.RequestAsync(new KeyEventArgs
            { Key = Key.Enter, PlatformInputEventCookie = new object() }, "fixture"));
    }

    [AvaloniaFact]
    public void LocalizedKeywordsParticipateInSearchWithoutChangingDesktopIdentity()
    {
        var app = new AppEntry("org.example.editor.desktop", "Editor", "Edit text", "", false,
            AppLaunchKind.DesktopEntry, SearchTerms: ["muistiinpanot", "Text processor"]);
        var item = new LauncherItemViewModel(app);
        Assert.Contains("MUISTIINPANOT", item.SearchKey);
        item.UpdateApp(app with { Name = "Muokkain", SearchTerms = ["documents"] });
        Assert.Equal("org.example.editor.desktop", item.App.Id);
        Assert.Contains("DOCUMENTS", item.SearchKey);
        Assert.DoesNotContain("MUISTIINPANOT", item.SearchKey);
    }
}
