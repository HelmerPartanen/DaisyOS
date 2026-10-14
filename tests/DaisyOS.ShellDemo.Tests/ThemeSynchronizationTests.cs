using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ThemeSynchronizationTests
{
    [AvaloniaFact]
    public async Task DataBoundSemanticIconKeysFollowThemeAndAccentChanges()
    {
        var icon = new DaisyIcon { Icon = "house" };
        ThemeResourceBrush.SetForegroundKey(icon, "ActionPrimaryBrush");
        var window = new Window { Content = icon, Width = 100, Height = 100 };
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                await Task.Delay(300);
                Assert.True(Application.Current!.Resources.TryGetResource("ActionPrimaryBrush", theme, out var value));
                Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(value).Color, icon.CurrentColor);
            }
            window.Resources["ActionPrimaryBrush"] = Brushes.Coral;
            await Task.Delay(300);
            Assert.Equal(Colors.Coral, icon.CurrentColor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task SharedTransitionsPreserveInteractionMotionAndSettleOnNewColor()
    {
        var opacity = new DoubleTransition { Property = Border.OpacityProperty, Duration = TimeSpan.FromMilliseconds(75) };
        var border = new Border { Background = Brushes.Red, Transitions = new Transitions { opacity } };
        var window = new Window { Content = border, Width = 100, Height = 100 };
        window.Resources["MotionDurationBase"] = TimeSpan.FromMilliseconds(200);
        window.Show();
        try
        {
            await Task.Delay(250);
            Assert.Contains(opacity, border.Transitions!);
            Assert.Single(border.Transitions!.Where(t => t.Property == Border.BackgroundProperty));
            border.Background = Brushes.Blue;
            await Task.Delay(60);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var intermediate = Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color;
            Assert.NotEqual(Colors.Red, intermediate);
            Assert.NotEqual(Colors.Blue, intermediate);
            await Task.Delay(300);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(Colors.Blue, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
            window.Resources["MotionDurationBase"] = TimeSpan.Zero;
            border.Background = Brushes.Green;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(Colors.Green, Assert.IsAssignableFrom<ISolidColorBrush>(border.Background).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ImageCrossfadeRetargetsAndRestoresTheResourceBinding()
    {
        var red = new PaintedImage(Brushes.Red);
        var blue = new PaintedImage(Brushes.Blue);
        var green = new PaintedImage(Brushes.Green);
        var image = new Image();
        var window = new Window { Content = image, Width = 100, Height = 100 };
        window.Resources["MotionDurationBase"] = TimeSpan.FromMilliseconds(200);
        window.Resources["TestImage"] = red;
        image.Bind(Image.SourceProperty, image.GetResourceObservable("TestImage"));
        window.Show();
        try
        {
            await Task.Delay(250);
            window.Resources["TestImage"] = blue;
            await Task.Delay(60);
            Assert.NotSame(red, image.Source);
            Assert.NotSame(blue, image.Source);
            window.Resources["TestImage"] = green;
            await Task.Delay(350);
            Assert.Same(green, image.Source);
            window.Resources["MotionDurationBase"] = TimeSpan.Zero;
            window.Resources["TestImage"] = red;
            Assert.Same(red, image.Source);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AppearanceWatcherHandlesAtomicReplacementAndStopsAfterDisposal()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-appearance-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "preferences.json");
        await File.WriteAllTextAsync(path, "{}");
        var updates = 0;
        using var watcher = new AppearanceFileObserver(path, () => updates++);
        try
        {
            var replacement = Path.Combine(directory, "replacement.json");
            await File.WriteAllTextAsync(replacement, "{\"theme\":\"dark\"}");
            File.Move(replacement, path, overwrite: true);
            await Task.Delay(300);
            Assert.True(updates > 0);
            watcher.Dispose();
            var previous = updates;
            await File.WriteAllTextAsync(path, "{}");
            await Task.Delay(100);
            Assert.Equal(previous, updates);
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private sealed class PaintedImage(IBrush brush) : IImage
    {
        public Size Size => new(16, 16);
        public void Draw(DrawingContext context, Rect sourceRect, Rect destRect) => context.DrawRectangle(brush, null, destRect);
    }

    [AvaloniaFact]
    public async Task IconsFollowMutableBrushColors()
    {
        var brush = new SolidColorBrush(Colors.White);
        var icon = new DaisyIcon { Icon = "info", Foreground = brush };
        var window = new Window { Content = icon, Width = 100, Height = 100 };
        window.Show();
        try
        {
            await Task.Delay(100);
            brush.Color = Colors.CornflowerBlue;
            Assert.Equal(brush.Color, icon.CurrentColor);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task MenuIconsFollowRepeatedThemeChangesAndReopening()
    {
        var icon = new DaisyIcon { Icon = "info", Classes = { "MenuIcon" } };
        var item = new MenuItem { Header = "Properties", Icon = icon };
        var menu = new ContextMenu { ItemsSource = new[] { item } };
        var button = new Button { Content = "Open", ContextMenu = menu };
        var window = new Window { Content = button, Width = 200, Height = 100 };
        window.Show();
        try
        {
            foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light, ThemeVariant.Dark })
            {
                window.RequestedThemeVariant = theme;
                menu.Open(button);
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                Assert.True(Application.Current!.Resources.TryGetResource("ContentPrimaryBrush", theme, out var value));
                var expected = Assert.IsAssignableFrom<ISolidColorBrush>(value).Color;
                Assert.Equal(expected, icon.CurrentColor);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var rendered = new RenderTargetBitmap(new PixelSize(16, 16), new Vector(96, 96));
                rendered.Render(icon);
                using var encoded = new MemoryStream();
                rendered.Save(encoded);
                using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
                Assert.Contains(pixels.Pixels, pixel => pixel.Alpha > 200 &&
                    Math.Abs(pixel.Red - expected.R) < 8 && Math.Abs(pixel.Green - expected.G) < 8 &&
                    Math.Abs(pixel.Blue - expected.B) < 8);
                menu.Close();
            }
        }
        finally { menu.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task MenuIconResourceBindingUpdatesWhilePopupRemainsOpen()
    {
        var icon = new DaisyIcon { Icon = "folder", Classes = { "MenuIcon" } };
        var item = new MenuItem { Header = "Open in Files", Icon = icon };
        var menu = new ContextMenu { ItemsSource = new[] { item } };
        var button = new Button { Content = "Open", ContextMenu = menu };
        var window = new Window { Content = button, Width = 200, Height = 100 };
        window.Show();
        try
        {
            window.RequestedThemeVariant = ThemeVariant.Light;
            menu.Open(button);
            await Task.Delay(200);

            window.RequestedThemeVariant = ThemeVariant.Dark;
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();

            Assert.True(Application.Current!.Resources.TryGetResource("ContentPrimaryBrush", ThemeVariant.Dark, out var value));
            Assert.Equal(Assert.IsAssignableFrom<ISolidColorBrush>(value).Color, icon.CurrentColor);
        }
        finally { menu.Close(); window.Close(); }
    }
}
