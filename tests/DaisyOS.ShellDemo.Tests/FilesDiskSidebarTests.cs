using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class FilesDiskSidebarTests
{
    [AvaloniaFact]
    public async Task SidebarDisplaysDisksAndTheDataTravelerName()
    {
        var window = new FilesWindow(Path.GetTempPath(), false) { Width = 1100, Height = 760 };
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            var vm = Assert.IsType<FilesViewModel>(window.DataContext);
            await vm.RefreshVolumesAsync();
            vm.Volumes.Clear();
            vm.Volumes.Add(new VolumeInfo("DataTraveler 3.0", "/dev/testusb1", "vfat", true) { DevicePath = "/dev/testusb1" });
            window.UpdateLayout();
            using var frame = window.CaptureRenderedFrame();
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "Disks");
            Assert.Contains(window.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == "DataTraveler 3.0");
            var button = window.GetVisualDescendants().OfType<Button>().Single(x => x.DataContext is VolumeInfo { DevicePath: "/dev/testusb1" } && x.Classes.Contains("SettingsNavItem"));
            Assert.Equal("/dev/testusb1", button.Tag);
            Assert.True(button.IsEnabled);
            Assert.False(DragDrop.GetAllowDrop(button));
            frame!.Save("/tmp/daisy-files-disks-sidebar.png");
        }
        finally { window.Close(); }
    }
}
