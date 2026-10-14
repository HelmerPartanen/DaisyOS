using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AdaptiveStripTests
{
    [AvaloniaFact]
    public void FilesTabsShrinkAndExpandWithoutLosingCloseControls()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-tabs-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var window = new FilesWindow(directory, false);
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var tabs = window.FindControl<ItemsControl>("TabsControl")!;
            double OriginalWidth() => tabs.GetVisualDescendants().OfType<Button>().First(b => b.Classes.Contains("FileTab")).Bounds.Width;
            var initialWidth = OriginalWidth();
            for (var i = 0; i < 11; i++)
            {
                window.KeyPressQwerty(PhysicalKey.T, RawInputModifiers.Control);
                window.KeyReleaseQwerty(PhysicalKey.T, RawInputModifiers.Control);
            }
            Dispatcher.UIThread.RunJobs();
            var buttons = tabs.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("FileTab")).ToArray();
            Assert.Equal(12, buttons.Length);
            Assert.True(OriginalWidth() < initialWidth);
            foreach (var tab in buttons)
            {
                var close = tab.GetVisualDescendants().OfType<Button>().Single();
                var point = close.TranslatePoint(new Avalonia.Point(), tab)!.Value;
                Assert.True(point.X >= 0 && point.X + close.Bounds.Width <= tab.Bounds.Width + 1);
            }
            for (var i = 0; i < 11; i++)
            {
                window.KeyPressQwerty(PhysicalKey.W, RawInputModifiers.Control);
                window.KeyReleaseQwerty(PhysicalKey.W, RawInputModifiers.Control);
            }
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(initialWidth, OriginalWidth(), precision: 1);
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaFact]
    public void CrowdedTaskbarStaysInsideScreenAndRestoresNormalSizes()
    {
        var bar = new TaskbarView();
        var window = new Window { Width = 800, Height = 160, Content = bar };
        window.Show();
        try
        {
            for (var i = 0; i < 40; i++) bar.PinApp("fit-test-" + i);
            Dispatcher.UIThread.RunJobs();
            var canvas = bar.FindControl<Canvas>("AppIconsCanvas")!;
            var buttons = canvas.Children.OfType<Button>().ToArray();
            Assert.True(bar.Bounds.Width <= window.Bounds.Width - 24 + 1);
            Assert.All(buttons, b => Assert.True(b.Bounds.Width < 40));
            Assert.All(buttons, b => Assert.True(Canvas.GetLeft(b) + b.Width <= canvas.Width + 1));
            foreach (var button in buttons.Where(b => ((string)b.Tag!).StartsWith("fit-test-"))) bar.RemoveSpecificApp(button);
            Dispatcher.UIThread.RunJobs();
            Assert.All(canvas.Children.OfType<Button>(), b => Assert.Equal(40, b.Width));
        }
        finally { window.Close(); }
    }
}
