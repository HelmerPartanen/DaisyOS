using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class DefaultBlueAccentTests
{
    [AvaloniaTheory]
    [InlineData(false, "#1598FF", "#138CEB", "#1283DB")]
    [InlineData(true, "#148BE8", "#2794EA", "#359BEB")]
    public async Task PrimaryButtonsUseRequestedBlueWithReadableHoverAndPressedStates(bool dark, string normal, string hover, string pressed)
    {
        var button = new Button { Content = "Continue", Classes = { "Primary" }, Width = 180, Height = 40 };
        var window = new Window { Content = button, Width = 300, Height = 100, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(x => x.Name == "PART_ContentPresenter");
            Assert.Equal(Color.Parse(normal), ((ISolidColorBrush)presenter.Background!).Color);
            Assert.Equal(Colors.White, ((ISolidColorBrush)button.Foreground!).Color);
            var point = button.TranslatePoint(new Point(90, 20), window)!.Value;
            window.MouseMove(point); await Task.Delay(250); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(Color.Parse(hover), ((ISolidColorBrush)presenter.Background!).Color);
            window.MouseDown(point, MouseButton.Left); await Task.Delay(250); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(Color.Parse(pressed), ((ISolidColorBrush)presenter.Background!).Color);
            window.MouseUp(point, MouseButton.Left);
        }
        finally { window.Close(); }
    }
}
