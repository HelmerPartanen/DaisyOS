using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Windows;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class NativeTaskbarIdentityTests
{
    [AvaloniaTheory]
    [InlineData("appstore", "appstore")]
    [InlineData("appstore", "org.daisyos.appstore")]
    [InlineData("appstore", "org.daisyos.appstore.desktop")]
    [InlineData("notes", "notes")]
    [InlineData("notes", "org.daisyos.notes.desktop")]
    public void DesktopIdPinsReceiveWindowIndicatorsAndCompositorRestoreIdentity(string app, string pin)
    {
        var taskbar = new TaskbarView(); taskbar.ApplyOrder([pin, app]);
        var host = new Window { Content = taskbar, Width = 800, Height = 300 }; host.Show();
        try
        {
            var button = Assert.Single(taskbar.GetVisualDescendants().OfType<Button>().Where(b => Equals(b.Tag, app)));
            Assert.DoesNotContain(taskbar.GetVisualDescendants().OfType<Button>(), b => Equals(b.Tag, "org.daisyos." + app));
            var indicator = button.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("TaskbarWindowIndicator"));
            var record = new CompositorWindowRecord("first", "org.daisyos." + app, "Localized title", "org.daisyos." + app,
                null, null, 1, false, false, false, false, true, DaisyAppId: app);
            taskbar.SetCompositorWindows([record]);
            Assert.Contains("WindowRunning", indicator.Classes);
            taskbar.SetCompositorWindows([record with { IsMinimized = true }]);
            Assert.Contains("WindowMinimized", indicator.Classes);
            Assert.True(taskbar.TryGetCompositorWindowId(pin, out var windowId)); Assert.Equal("first", windowId);
            taskbar.SetCompositorWindows([record with { IsActive = true }]);
            Assert.Contains("WindowActive", indicator.Classes);
            taskbar.SetAppWindowState(pin, NativeAppWindowState.NotRunning);
            Assert.DoesNotContain("WindowRunning", indicator.Classes);
            taskbar.SetCompositorWindows([record with { Id = "reopened", IsActive = true }]);
            Assert.Contains("WindowRunning", indicator.Classes);
            Assert.Contains("WindowActive", indicator.Classes);
        }
        finally { host.Close(); }
    }
}
