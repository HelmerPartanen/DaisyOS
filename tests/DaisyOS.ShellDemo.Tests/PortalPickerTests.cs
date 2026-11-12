using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files.Picker;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class PortalPickerTests
{
    [AvaloniaFact]
    public void PickerUsesSharedWindowChromeAcrossWindowStates()
    {
        var picker = new DaisyFilePickerWindow();
        try
        {
            picker.Show();
            Assert.Equal(WindowDecorations.None, picker.WindowDecorations);
            var titleBar = picker.FindControl<DaisyOS.Shell.Controls.AppWindowTitleBar>("PickerTitleBar")!;
            var frame = Assert.IsType<Border>(picker.Content);
            Assert.True(titleBar.IsVisible);
            Assert.Equal(new CornerRadius(16), frame.CornerRadius);
            Assert.Equal(default, frame.BorderThickness);
            picker.WindowState = WindowState.Maximized;
            Assert.True(titleBar.IsVisible);
            Assert.Equal(default, frame.CornerRadius);
            picker.WindowState = WindowState.FullScreen;
            Assert.False(titleBar.IsVisible);
            picker.WindowState = WindowState.Normal;
            Assert.True(titleBar.IsVisible);
            Assert.Equal(new CornerRadius(16), frame.CornerRadius);
        }
        finally { picker.Close(); }
    }

    [AvaloniaFact]
    public void FolderModeConfirmsAnEmptyDirectory()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-portal-picker-");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose folder", false, false, [], InitialFolder: folder.FullName, SelectDirectory: true));
        try
        {
            picker.Show();
            var confirm = picker.FindControl<Button>("ConfirmButton")!;
            Assert.True(confirm.IsEnabled);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(picker.WasConfirmed);
            Assert.Equal(folder.FullName, Assert.Single(picker.SelectedPaths));
        }
        finally { picker.Close(); folder.Delete(true); }
    }

    [AvaloniaFact]
    public void SaveModeRejectsTraversalAndReturnsAnUncreatedDestination()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-portal-picker-");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Save", false, false, [], InitialFolder: folder.FullName, SaveFile: true, SuggestedName: "New.txt"));
        try
        {
            picker.Show();
            var name = picker.GetVisualDescendants().OfType<TextBox>().Single(control => control.PlaceholderText == "File name");
            var confirm = picker.FindControl<Button>("ConfirmButton")!;
            name.Text = "../escape.txt";
            Assert.False(confirm.IsEnabled);
            name.Text = "New.txt";
            Assert.True(confirm.IsEnabled);
            confirm.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(picker.WasConfirmed);
            var destination = Assert.Single(picker.SelectedPaths);
            Assert.Equal(Path.Combine(folder.FullName, "New.txt"), destination);
            Assert.False(File.Exists(destination));
        }
        finally { picker.Close(); folder.Delete(true); }
    }

    [AvaloniaFact]
    public void ClosingFolderSelectionDoesNotConfirmPermission()
    {
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose folder", false, false, [], SelectDirectory: true));
        picker.Show(); picker.Close();
        Assert.False(picker.WasConfirmed);
        Assert.Empty(picker.SelectedPaths);
    }

    [AvaloniaFact]
    public async Task LargeFolderUsesBoundedContainersAndNativeSelection()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-picker-large-");
        for (var i = 0; i < 1200; i++) File.WriteAllText(Path.Combine(folder.FullName, $"File-{i:D4}.txt"), "test");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose", true, false, ["*.txt"], InitialFolder: folder.FullName));
        var originalGridView = picker.FindControl<ListBox>("EntriesGridView")!.IsVisible;
        try
        {
            picker.Show();
            var grid = picker.FindControl<ListBox>("EntriesGridView")!;
            var details = picker.FindControl<ListBox>("EntriesListView")!;
            await WaitFor(() => grid.ItemCount == 1200);
            picker.UpdateLayout();
            var list = grid.IsVisible ? grid : details;
            Assert.InRange(list.GetRealizedContainers().Count(), 1, 150);
            picker.CaptureRenderedFrame()?.Save("/tmp/daisy-picker-large.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            list.SelectedItems!.Add(list.Items[0]);
            list.SelectedItems.Add(list.Items[1]);
            Assert.Equal(2, picker.SelectedPaths.Count);
            list.SelectedItems.Remove(list.Items[0]);
            Assert.Single(picker.SelectedPaths);
            var search = picker.FindControl<DaisyOS.Shell.Controls.SearchBar>("SearchBox")!;
            search.Text = "File-0001";
            await WaitFor(() => list.ItemCount == 1);
            Assert.Single(picker.SelectedPaths);
            search.Text = "";
            await WaitFor(() => list.ItemCount == 1200);
            Assert.Single(list.SelectedItems);
            picker.Width = 760;
            var viewIcon = picker.FindControl<DaisyOS.Shell.Controls.DaisyIcon>("ViewModeIcon")!;
            if (grid.IsVisible) ((Button)viewIcon.Parent!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            picker.UpdateLayout();
            Assert.InRange(details.GetRealizedContainers().Count(), 1, 150);
            var tray = picker.FindControl<Border>("SelectionTrayScroller")!;
            var actions = picker.FindControl<StackPanel>("PickerActions")!;
            var trayTop = tray.TranslatePoint(default, picker)!.Value.Y;
            var actionsTop = actions.TranslatePoint(default, picker)!.Value.Y;
            Assert.True(trayTop + tray.Bounds.Height <= actionsTop);
            Assert.All(actions.Children.OfType<Button>(), button => Assert.Contains("Small", button.Classes));
            picker.CaptureRenderedFrame()?.Save("/tmp/daisy-picker-details.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally
        {
            if (picker.FindControl<ListBox>("EntriesGridView")!.IsVisible != originalGridView)
                ((Button)picker.FindControl<DaisyOS.Shell.Controls.DaisyIcon>("ViewModeIcon")!.Parent!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            picker.Close(); folder.Delete(true);
        }
    }

    [AvaloniaFact]
    public async Task ChangingFileTypeCancelsPreviousLoadAndKeepsFolders()
    {
        var folder = Directory.CreateTempSubdirectory("daisy-picker-types-");
        Directory.CreateDirectory(Path.Combine(folder.FullName, "Child"));
        File.WriteAllText(Path.Combine(folder.FullName, "Document.txt"), "text");
        File.WriteAllText(Path.Combine(folder.FullName, "Picture.png"), "image");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose", false, false, [], InitialFolder: folder.FullName,
            Filters: [new("Documents", ["*.txt"]), new("Images", ["*.png"])]));
        try
        {
            picker.Show();
            var list = picker.FindControl<ListBox>("EntriesGridView")!;
            await WaitFor(() => list.ItemCount == 2);
            Assert.Contains(list.Items.Cast<PickerEntry>(), entry => entry.Name == "Document.txt");
            picker.FindControl<ComboBox>("FileTypeFilter")!.SelectedIndex = 1;
            await WaitFor(() => list.Items.Cast<PickerEntry>().Any(entry => entry.Name == "Picture.png"));
            Assert.Equal(2, list.ItemCount);
            Assert.Contains(list.Items.Cast<PickerEntry>(), entry => entry.IsDirectory);
            Assert.DoesNotContain(list.Items.Cast<PickerEntry>(), entry => entry.Name == "Document.txt");
            var activeList = list.IsVisible ? list : picker.FindControl<ListBox>("EntriesListView")!;
            var directory = activeList.Items.Cast<PickerEntry>().Single(entry => entry.IsDirectory);
            activeList.SelectedItem = directory;
            Assert.Same(directory, activeList.SelectedItem);
            Assert.Empty(picker.SelectedPaths);
        }
        finally { picker.Close(); folder.Delete(true); }
    }

    private static async Task WaitFor(Func<bool> condition)
    {
        for (var i = 0; i < 300 && !condition(); i++) await Task.Delay(10);
        Assert.True(condition(), "Picker did not finish loading.");
    }
}
