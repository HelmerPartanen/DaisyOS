using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherPanelTests
{
    [AvaloniaTheory]
    [InlineData(1280, 800, false, 100)]
    [InlineData(1280, 800, true, 100)]
    [InlineData(1920, 1080, false, 100)]
    [InlineData(1920, 1080, true, 100)]
    [InlineData(800, 600, false, 100)]
    [InlineData(800, 600, true, 100)]
    [InlineData(640, 480, true, 100)]
    [InlineData(1280, 800, false, 150)]
    [InlineData(1280, 800, true, 150)]
    public void PanelIsCenteredBoundedAndStableDuringSearch(int width, int height, bool dark, int textScale)
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"Application {i:00}", "", "", false)).ToArray());
        TypographyPolicy.Apply(Application.Current!.Resources, TypographyPolicy.DefaultFamilyName, textScale);
        var launcher = new LauncherView(model);
        var window = new Window { Width = width, Height = height, Content = launcher,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            launcher.AnimateIn(interactive: true);
            window.UpdateLayout();
            var panel = launcher.FindControl<Border>("LauncherRootBorder")!;
            var position = panel.TranslatePoint(default, window)!.Value;
            Assert.Equal(Math.Min(900, width - 48), panel.Bounds.Width, 1);
            Assert.Equal(Math.Min(600, height - 48), panel.Bounds.Height, 1);
            Assert.Equal((width - panel.Bounds.Width) / 2, position.X, 1);
            Assert.Equal((height - panel.Bounds.Height) / 2, position.Y, 1);
            Assert.False(launcher.IsWithinInteractiveSurface(launcher));
            Assert.True(launcher.IsWithinInteractiveSurface(panel));
            Assert.True(model.PageGridWidth <= panel.Bounds.Width - 48);
            var bounds = panel.Bounds;
            model.SearchText = "no matching applications";
            model.ResolveSearchResult();
            window.UpdateLayout();
            Assert.Equal(bounds, panel.Bounds);
            Assert.False(launcher.FindControl<StackPanel>("LauncherPageIndicators")!.IsEnabled);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            var artifacts = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                model.SearchText = "";
                model.ResolveSearchResult();
                window.UpdateLayout();
                using var populated = window.CaptureRenderedFrame();
                populated!.Save(Path.Combine(artifacts, $"launcher-{width}x{height}-{(dark ? "dark" : "light")}-{textScale}.png"),
                    Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally
        {
            launcher.CloseImmediate(); window.Close();
            TypographyPolicy.Apply(Application.Current!.Resources, TypographyPolicy.DefaultFamilyName, 100);
        }
    }

    [AvaloniaFact]
    public void LargeCatalogKeepsPageIndicatorsBoundedAndIncludesTheCurrentPage()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 2000)
            .Select(i => new AppEntry($"app-{i}", $"Application {i:0000}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        var window = new Window { Width = 800, Height = 600, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            model.GoToPage(model.PageCount - 1);
            var indicators = launcher.FindControl<StackPanel>("LauncherPageIndicators")!;
            Assert.Equal(7, indicators.Children.Count);
            Assert.Contains(indicators.Children.OfType<Button>(), button => Equals(button.Tag, model.PageIndex));
            model.GoToPage(0);
            Assert.Contains(indicators.Children.OfType<Button>(), button => Equals(button.Tag, 0));
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public void ResizingKeepsTheSelectedApplicationOnTheVisiblePage()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"Application {i:00}", "", "", false)).ToArray());
        model.SetPageLayout(6, 3);
        for (var i = 0; i < 30; i++) model.MoveSelection(1, 0);
        var selected = model.SelectedItem;
        Assert.NotNull(selected);
        model.SetPageLayout(4, 2);
        Assert.Same(selected, model.SelectedItem);
        Assert.Contains(selected, model.PageApps);
        model.SetPageLayout(6, 3);
        Assert.Contains(selected, model.PageApps);
    }
}
