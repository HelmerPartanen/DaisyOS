using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AccentSwatchTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SwatchesStayCircularAndRetainTheirColorOnHover(bool dark)
    {
        var window = new SettingsWindow { Width = 1280, Height = 900,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.SelectCategory("Appearance");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            Assert.Null(view.FindControl<Button>("AccentBlueButton"));
            Assert.Equal(8, view.GetVisualDescendants().OfType<Button>().Count(b => b.Classes.Contains("AccentSwatch")));
            foreach (var button in view.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("AccentSwatch")))
            {
                Assert.Equal(button.Bounds.Width, button.Bounds.Height);
                Assert.Equal(button.Bounds.Height / 2, button.CornerRadius.TopLeft);
                var expected = Assert.IsAssignableFrom<ISolidColorBrush>(button.Background).Color;
                button.Classes.Add("Selected");
                window.UpdateLayout();
                var ring = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "SelectionRing");
                Assert.True(ring.IsVisible);
                Assert.Equal(new Rect(button.Bounds.Size), ring.Bounds);
                Assert.Equal(button.Bounds.Width, ring.Bounds.Width);
                Assert.Equal(2, ring.BorderThickness.Left);
                Assert.Equal(2, (ring.Bounds.Width - 24) / 2 - ring.BorderThickness.Left);
                var selectedFill = button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "SwatchFill");
                Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(selectedFill.Background).Color);
                button.BringIntoView();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                using (window.CaptureRenderedFrame()) { }
                var center = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
                window.MouseMove(center);
                await Task.Delay(220);
                var presenter = button.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "SwatchFill");
                Assert.True(button.IsPointerOver, $"{button.Name}: fill={selectedFill.Bounds}, center={center}");
                Assert.Equal(expected, Assert.IsAssignableFrom<ISolidColorBrush>(presenter.Background).Color);
            }
        }
        finally { window.Close(); }
    }
}
