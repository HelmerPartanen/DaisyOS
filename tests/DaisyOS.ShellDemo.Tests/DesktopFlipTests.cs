using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Desktop;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopFlipTests
{
    [AvaloniaFact]
    public async Task RetargetingAnActiveFlipKeepsTheVisiblePositionContinuous()
    {
        var vm = new DesktopViewModel(new DesktopLayoutStore(Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".json")));
        vm.ClearItems();
        var metrics = new DesktopGridMetrics("test", new Rect(0, 0, 600, 400), 96, 100, 100);
        var item = new DesktopItemViewModel(new DesktopItemState("test-icon", "test", 0, 0));
        vm.AddItem(item); vm.CalculateLayout(metrics);
        var child = new Border { DataContext = item };
        var panel = new DesktopReorderPanel { Metrics = metrics, ViewModel = vm, Children = { child } };
        var window = new Window { Width = 600, Height = 400, Content = panel };
        window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            vm.ReorderItem(item.Id, new GridCell(1, 0), metrics);
            panel.InvalidateArrange(); window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            await Task.Delay(70); Dispatcher.UIThread.RunJobs();
            var before = child.Bounds.X + (child.RenderTransform?.Value.M31 ?? 0);
            vm.ReorderItem(item.Id, new GridCell(2, 0), metrics);
            panel.InvalidateArrange(); window.UpdateLayout();
            var after = child.Bounds.X + (child.RenderTransform?.Value.M31 ?? 0);
            Assert.InRange(Math.Abs(after - before), 0, 0.1);
            Dispatcher.UIThread.RunJobs();
            await Task.Delay(300); Dispatcher.UIThread.RunJobs();
            Assert.Equal(200, child.Bounds.X);
            Assert.InRange(Math.Abs(child.RenderTransform?.Value.M31 ?? 0), 0, 0.1);
        }
        finally { window.Close(); }
    }
}
