using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class TaskbarAppImageIdentityTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunningWindowPinPersistsInstalledLauncherAndReopens(bool hasWmClass)
    {
        WithTaskbar(hasWmClass, view =>
        {
            view.ApplyOrder([]);
            view.SetCompositorWindows([Epic()]);
            view.PinApp("kwin:epic-window");
            Assert.Equal("epic-games-launcher", Assert.Single(view.GetCurrentPinnedOrder()));
            view.SetCompositorWindows([]);
            var button = Assert.Single(Tiles(view));
            Assert.Equal("epic-games-launcher", button.Tag);
            string? launched = null;
            view.AppIconClicked += (_, id) => launched = id;
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal("epic-games-launcher", launched);
            view.SetCompositorWindows([Epic() with { Id = "reopened" }]);
            Assert.Single(Tiles(view));
            Assert.True(view.TryGetCompositorWindowId("epic-games-launcher", out var id));
            Assert.Equal("reopened", id);
        });
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DesktopPinMergesExistingRunningTileAndLegacyPins(bool pinBeforeLaunch)
    {
        WithTaskbar(false, view =>
        {
            view.ApplyOrder([]);
            if (pinBeforeLaunch) view.ApplyOrder(["epic-games-launcher"]);
            view.SetCompositorWindows([Epic()]);
            if (!pinBeforeLaunch)
            {
                view.ApplyOrder(["epic-games-launcher"]);
                view.SetCompositorWindows([Epic()]);
            }
            Assert.Single(Tiles(view));
            Assert.Equal("epic-games-launcher", Assert.Single(Tiles(view)).Tag);
            view.ApplyOrder(["EpicGamesLauncher", "epic-games-launcher.desktop"]);
            Assert.Equal("epic-games-launcher", Assert.Single(view.GetCurrentPinnedOrder()));
            Assert.Single(Tiles(view));
        });
    }

    private static Button[] Tiles(TaskbarView view) => view.FindControl<Canvas>("AppIconsCanvas")!.Children.OfType<Button>().ToArray();
    private static CompositorWindowRecord Epic() => new("epic-window", "EpicGamesLauncher", "Epic Games Launcher",
        "EpicGamesLauncher", null, null, 1, false, false, false, false, true);

    private static void WithTaskbar(bool hasWmClass, Action<TaskbarView> test)
    {
        var directory = Directory.CreateTempSubdirectory("daisy-taskbar-appimage-");
        var desktopFile = Path.Combine(directory.FullName, "epic-games-launcher.desktop");
        File.WriteAllText(desktopFile, "[Desktop Entry]\nType=Application\nName=Epic Games Launcher\n" +
            "Exec=\"/apps/epic-games-launcher.AppImage\"\nX-DaisyOS-AppImage=epic-games-launcher\n" +
            (hasWmClass ? "StartupWMClass=EpicGamesLauncher\n" : ""));
        var view = new TaskbarView
        {
            DesktopEntryResolver = identities => DesktopItemLoader.FindDesktopEntryFilePath(identities, [desktopFile])
        };
        var window = new Window { Content = view, Width = 900, Height = 180 };
        window.Show();
        try { test(view); }
        finally { window.Close(); directory.Delete(true); }
    }
}
