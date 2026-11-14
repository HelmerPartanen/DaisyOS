using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Desktop;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopDeleteTests
{
    [AvaloniaFact]
    public async Task SelectedFilesAndFoldersGoToTrashWhileTrashItselfIsProtected()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-desktop-delete-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var oldData = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        var oldLayout = Environment.GetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", directory);
        Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", Path.Combine(directory, "layout.json"));
        try
        {
            var desktop = new DesktopView();
            var model = desktop.ViewModel;
            model.ClearItems();
            var file = Path.Combine(directory, "selected.txt");
            var folder = Path.Combine(directory, "selected-folder");
            File.WriteAllText(file, "preserved contents"); Directory.CreateDirectory(folder);
            File.WriteAllText(Path.Combine(folder, "nested.txt"), "nested contents");
            model.AddDesktopPath(file); model.AddDesktopPath(folder);
            var trash = new DesktopItemViewModel(new DesktopItemState(DesktopItemViewModel.TrashItemId, "primary", 0, 0, 0));
            model.AddItem(trash);
            foreach (var item in model.Items) item.IsSelected = true;
            await desktop.DeleteSelectedItemsAsync();
            Assert.False(File.Exists(file)); Assert.False(Directory.Exists(folder));
            Assert.Equal("preserved contents", File.ReadAllText(Path.Combine(directory, "Trash/files/selected.txt")));
            Assert.Equal("nested contents", File.ReadAllText(Path.Combine(directory, "Trash/files/selected-folder/nested.txt")));
            Assert.True(File.Exists(Path.Combine(directory, "Trash/info/selected.txt.trashinfo")));
            Assert.Contains(trash, model.Items);
            Assert.Single(model.Items);
        }
        finally
        {
            Environment.SetEnvironmentVariable("XDG_DATA_HOME", oldData);
            Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", oldLayout);
            Directory.Delete(directory, recursive: true);
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DeleteRemovesSelectedWidgetButLeavesRenameInputAlone(bool renaming)
    {
        var path = Path.Combine(Path.GetTempPath(), "daisy-delete-layout-" + Guid.NewGuid().ToString("N") + ".json");
        var oldLayout = Environment.GetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH");
        Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", path);
        try
        {
            var desktop = new DesktopView();
            var window = new Window { Width = 800, Height = 600, Content = desktop };
            window.Show(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            desktop.ViewModel.ClearItems();
            var widget = new DesktopItemViewModel(new DesktopItemState("widget:delete-test", "primary", 0, 0, 0))
                { IsWidgetCard = true, IsSelected = true, IsRenaming = renaming };
            desktop.ViewModel.AddItem(widget);
            var surface = desktop.FindControl<Grid>("InteractionSurface")!;
            var key = new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Delete };
            surface.RaiseEvent(key);
            Assert.Equal(!renaming, key.Handled);
            Assert.Equal(renaming, desktop.ViewModel.Items.Contains(widget));
            window.Close();
        }
        finally
        {
            Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", oldLayout);
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void EmptyDesktopAndItemMenusOfferDesktopSettings()
    {
        var layout = Path.Combine(Path.GetTempPath(), "daisy-menu-layout-" + Guid.NewGuid().ToString("N") + ".json");
        var oldLayout = Environment.GetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH");
        Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", layout);
        var desktop = new DesktopView();
        var itemView = new DesktopItemView { Width = 100, Height = 100,
            DataContext = new DesktopItemViewModel(new DesktopItemState("test-item", "primary", 0, 0, 0)) { Label = "Test item" } };
        var root = new Grid { Children = { desktop, itemView } };
        var window = new Window { Width = 800, Height = 600, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var surface = desktop.FindControl<Grid>("InteractionSurface")!;
            var menu = surface.ContextMenu!;
            menu.Open(surface); Dispatcher.UIThread.RunJobs();
            Assert.Contains(menu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == Loc.Get("Desktop settings"));
            menu.Close();
            var itemOwner = itemView.FindControl<Panel>("RootPanel")!;
            var itemMenu = itemOwner.ContextMenu!;
            itemMenu.Open(itemOwner); Dispatcher.UIThread.RunJobs();
            Assert.Contains(itemMenu.Items.OfType<MenuItem>(), item => item.Header?.ToString() == Loc.Get("Desktop settings"));
            itemMenu.Close();
        }
        finally
        {
            window.Close();
            Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", oldLayout);
            if (File.Exists(layout)) File.Delete(layout);
        }
    }
}
