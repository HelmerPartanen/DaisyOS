using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.System.Settings;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class CaptionCloseHoverTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseHoverUsesDangerColorImmediatelyAndAfterPointerReentry(bool dark)
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-caption-hover-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        try
        {
            foreach (var window in new Window[] { new SettingsWindow(), new FilesWindow(folder, false), new Window { Width = 360, Height = 100, Content = new DaisyOS.Shell.Apps.Calculator.CalculatorWindowTitleBar() }, new Window { Width = 360, Height = 100, Content = new DaisyOS.Shell.Apps.Calendar.CalendarWindowTitleBar() } })
            {
                window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
                window.Show();
                try
                {
                    await Task.Delay(250);
                    Dispatcher.UIThread.RunJobs();
                    var close = window.GetVisualDescendants().OfType<Button>().Single(b => b.Classes.Contains("CaptionClose"));
                    var presenter = close.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter");
                    Assert.True(close.TryFindResource("ActionDangerHoverBrush", close.ActualThemeVariant, out var expected));
                    var point = close.TranslatePoint(new Point(close.Bounds.Width / 2, close.Bounds.Height / 2), window)!.Value;
                    for (var entry = 0; entry < 3; entry++)
                    {
                        window.MouseMove(new Point(1, window.Bounds.Height - 1));
                        window.MouseMove(point);
                        Dispatcher.UIThread.RunJobs();
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Assert.True(close.IsPointerOver);
                        var glyphBrush = close.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.WindowCaptionGlyph>().SingleOrDefault()?.Foreground
                            ?? close.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.DaisyIcon>().Single().Foreground;
                        Assert.True(((ISolidColorBrush)glyphBrush!).Color == Colors.White, $"{window.GetType().Name}: close glyph should be white, got {glyphBrush}");
                        Assert.Equal(((ISolidColorBrush)expected!).Color, ((ISolidColorBrush)presenter.Background!).Color);
                        await Task.Delay(20);
                        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                        Assert.Equal(((ISolidColorBrush)expected!).Color, ((ISolidColorBrush)presenter.Background!).Color);
                    }
                }
                finally { window.Close(); }
            }
        }
        finally { Directory.Delete(folder, true); }
    }
}
