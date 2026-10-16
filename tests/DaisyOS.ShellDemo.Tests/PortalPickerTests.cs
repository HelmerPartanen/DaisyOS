using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files.Picker;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class PortalPickerTests
{
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
}
