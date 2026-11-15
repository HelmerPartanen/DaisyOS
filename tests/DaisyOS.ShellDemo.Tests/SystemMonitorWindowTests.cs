using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.System.Diagnostics;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SystemMonitorWindowTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemMonitorCanBeConstructedShownAndClosed(bool dark)
    {
        var window = new TaskManagerWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(window.IsVisible);
            var model = Assert.IsType<TaskManagerViewModel>(window.DataContext);
            model.SetActive(false);
            foreach (var section in new[] { 0, 1, 2 })
            {
                model.Navigate(section);
                model.FilteredProcesses.Add(new ProcessRowViewModel(new ProcessItemInfo(12345, "Test application", "/test/application", 0, 1024, "test")));
                window.UpdateLayout();
                Assert.True(window.IsVisible);
            }
        }
        finally { window.Close(); }
    }
}
