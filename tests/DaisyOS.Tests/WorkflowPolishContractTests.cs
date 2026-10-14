using Xunit;
using System.Text.RegularExpressions;

namespace DaisyOS.Tests;

public sealed class WorkflowPolishContractTests
{
    [Fact]
    public void NativeBridgeOwnsCommonOneStepShortcuts()
    {
        var root = RepoRoot();
        var bridge = File.ReadAllText(Path.Combine(root, "packaging/kwin/daisyos-window-bridge/contents/code/main.js"));
        var service = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Services/Compositor/KWinWindowBridge.cs"));
        var app = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/App.axaml.cs"));

        foreach (var gesture in new[] { "Meta+E", "Meta+I", "Meta+N", "Meta+V", "Meta+Shift+N", "Meta+Shift+S", "Ctrl+Shift+Esc" })
            Assert.Contains($"\"{gesture}\"", bridge);
        Assert.Contains("RequestShellAction", service);
        Assert.Contains("ShellActionRequested", app);
        Assert.Contains("case \"launcher\":", app);
        Assert.Contains("ShowNewNote", app);
    }

    [Fact]
    public void FilesExposesLocalSendWithoutANestedShareMenu()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Apps/Files/FilesWindow.axaml"));

        Assert.Equal(2, Regex.Matches(source, "Header=\"Send with LocalSend\"").Count);
        Assert.DoesNotContain("<MenuItem Header=\"Share\">", source);
    }

    [Fact]
    public void LocalSendSupportsFastAndRecoverableKeyboardFlow()
    {
        var root = RepoRoot();
        var view = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Apps/LocalSend/LocalSendWindow.axaml"));
        var code = File.ReadAllText(Path.Combine(root, "src/DaisyOS.Shell/Apps/LocalSend/LocalSendWindow.axaml.cs"));

        Assert.Contains("DoubleTapped=\"OnDeviceDoubleTapped\"", view);
        Assert.Contains("KeyDown=\"OnDevicesKeyDown\"", view);
        Assert.Contains("Click=\"OnChooseFilesClicked\"", view);
        Assert.Contains("Click=\"OnRefreshDevicesClicked\"", view);
        Assert.Contains("devices.Count == 1", code);
        Assert.Contains("_isSending", code);
    }

    [Fact]
    public void SettingsSearchMovesFocusToTheRequestedControl()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Apps/System/Settings/SettingsView.axaml.cs"));

        Assert.Contains("_pendingSettingTitle = entry.Title", source);
        Assert.Contains("FocusAppearanceSearchTarget", source);
        Assert.Contains("control.Focusable && control.IsEffectivelyEnabled", source);
        Assert.Contains("target.Focus(NavigationMethod.Directional)", source);
    }

    [Fact]
    public void DetachedLauncherContextMenuRetainsNamedChildReferences()
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell/Views/Components/Launcher/LauncherView.axaml.cs"));

        Assert.Contains("_appContextUninstallItem = this.FindControl<MenuItem>(\"LauncherUninstallMenuItem\")", source);
        Assert.Contains("if (_appContextUninstallItem is { } uninstall)", source);
        Assert.DoesNotContain("menu.FindControl<MenuItem>(\"LauncherUninstallMenuItem\")", source);
    }

    [Theory]
    [InlineData("Apps/Calendar/CalendarWindow.axaml")]
    [InlineData("Apps/Calculator/CalculatorWindow.axaml")]
    [InlineData("Apps/AppStore/AppStoreWindow.axaml")]
    [InlineData("Apps/System/Diagnostics/TaskManagerWindow.axaml")]
    [InlineData("Apps/UIKit/UIKitWindow.axaml")]
    [InlineData("Apps/Files/Picker/DaisyFilePickerWindow.axaml")]
    public void StandardAppsUseKWinWindowDecorations(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(
            RepoRoot(), "src/DaisyOS.Shell", relativePath));

        Assert.Contains("WindowDecorations=\"Full\"", source);
        Assert.DoesNotContain("WindowTitleBar", source);
        Assert.DoesNotContain("ResizeTop", source);
    }

    private static string RepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "DaisyOS.sln")))
            directory = directory.Parent;
        return directory!.FullName;
    }
    [Theory]
    [InlineData("Apps/Files/FilesWindow.axaml")]
    [InlineData("Apps/Notes/NotesWindow.axaml")]
    [InlineData("Apps/System/Settings/SettingsWindow.axaml")]
    public void ClientTitlebarAppsLeaveTheFrameToKWin(string relativePath)
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "src/DaisyOS.Shell", relativePath));
        Assert.Contains("WindowDecorations=\"None\"", source);
        Assert.DoesNotContain("AppWindowFrame", source);
        Assert.DoesNotContain("ShellFrameOuterEdgeBrush", source);
        Assert.DoesNotContain("TransparencyLevelHint=\"Transparent\"", source);
    }
}
