using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class DiskManagerTaskbarTests
{
    [AvaloniaTheory]
    [InlineData("diskmanager", false)]
    [InlineData("org.daisyos.diskmanager", true)]
    [InlineData("org.daisyos.diskmanager.desktop", false)]
    public void PinsAndCompositorWindowsShareOneIdentityThroughMinimizeRestoreClose(string pin, bool dark)
    {
        var view = new TaskbarView();
        view.ApplyOrder([pin, "diskmanager"]);
        var host = new Window { Content = view, Width = 800, Height = 250, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        host.Show();
        try
        {
            var button = Assert.Single(view.GetVisualDescendants().OfType<Button>(), x => Equals(x.Tag, "diskmanager"));
            var image = Assert.Single(button.GetVisualDescendants().OfType<Image>());
            Assert.NotNull(image.Source);
            var record = new CompositorWindowRecord("disk-one", "org.daisyos.diskmanager", "Disk Manager", "org.daisyos.diskmanager", "org.daisyos.diskmanager", null, 1, true, false, false, false, true, DaisyAppId: "diskmanager");
            view.SetCompositorWindows([record]);
            var indicator = Assert.Single(button.GetVisualDescendants().OfType<Border>(), x => x.Classes.Contains("TaskbarWindowIndicator"));
            Assert.Contains("WindowRunning", indicator.Classes);
            Assert.Contains("WindowActive", indicator.Classes);
            Assert.True(view.TryGetCompositorWindowId(pin, out var id)); Assert.Equal(record.Id, id);
            view.SetCompositorWindows([record with { IsMinimized = true, IsActive = false }]);
            Assert.Contains("WindowMinimized", indicator.Classes);
            view.SetCompositorWindows([record]);
            Assert.Contains("WindowActive", indicator.Classes);
            host.UpdateLayout();
            string? activated = null;
            view.AppIconClicked += (_, app) => activated = app;
            var point = button.TranslatePoint(new Point(20, 20), host)!.Value;
            host.MouseMove(point); host.MouseDown(point, MouseButton.Left); host.MouseUp(point, MouseButton.Left);
            Assert.Equal("diskmanager", activated);
            view.SetCompositorWindows([]);
            Assert.DoesNotContain("WindowRunning", indicator.Classes);
            view.SetCompositorWindows([record with { Id = "disk-two" }]);
            Assert.Contains("WindowRunning", indicator.Classes);
            Assert.True(view.TryGetCompositorWindowId(pin, out id)); Assert.Equal("disk-two", id);
        }
        finally { host.Close(); }
    }
    [AvaloniaTheory]
    [InlineData("diskmanager")]
    [InlineData("notes")]
    [InlineData("settings")]
    public void UnpinnedNativeAppsAppearWhileRunningAndLeaveAfterClosing(string app)
    {
        var view = new TaskbarView(); view.ApplyOrder(["files"]);
        var host = new Window { Content = view, Width = 800, Height = 250 }; host.Show();
        try
        {
            var record = new CompositorWindowRecord("disk", "org.daisyos." + app, "Disk Manager", "org.daisyos." + app, null, null, 1, true, false, false, false, true, DaisyAppId: app);
            view.SetCompositorWindows([record]);
            Assert.Single(view.GetVisualDescendants().OfType<Button>(), x => Equals(x.Tag, app));
            view.SetCompositorWindows([]);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), x => Equals(x.Tag, app));
        }
        finally { host.Close(); }
    }

    [AvaloniaFact]
    public void OwnedSystemMonitorAppearsFromNativeWindowStateWithoutBeingPinned()
    {
        var view = new TaskbarView(); view.ApplyOrder(["files"]);
        var host = new Window { Content = view, Width = 800, Height = 250 }; host.Show();
        try
        {
            view.SetAppWindowState("taskmanager", DaisyOS.Shell.Services.Windows.NativeAppWindowState.Active);
            Assert.Single(view.GetVisualDescendants().OfType<Button>(), x => Equals(x.Tag, "taskmanager"));

            view.SetAppWindowState("taskmanager", DaisyOS.Shell.Services.Windows.NativeAppWindowState.NotRunning);
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<Button>(), x => Equals(x.Tag, "taskmanager"));
        }
        finally { host.Close(); }
    }
}
