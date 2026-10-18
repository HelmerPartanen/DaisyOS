using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Screenshot;
using DaisyOS.Shell.Views.Components.Screenshot;
using SkiaSharp;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ScreenshotOverlayTests
{
    [AvaloniaTheory]
    [InlineData(false, 1280, 100)]
    [InlineData(true, 1280, 100)]
    [InlineData(false, 480, 100)]
    [InlineData(false, 480, 200)]
    [InlineData(true, 480, 100)]
    [InlineData(true, 480, 200)]
    public async Task ModesSelectionAndToolbarStayUsableAndRenderInBothThemes(bool dark, int width, int textScale)
    {
        using var fixture = new CaptureFixture();
        var overlay = new ScreenshotOverlay(fixture.Service);
        var window = new Window { Content = overlay, Width = width, Height = 720,
            Background = Brushes.SlateGray, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Resources["TypeSizeBodySmall"] = 13d * textScale / 100;
        window.Resources["TypeSizeCaption"] = 12d * textScale / 100;
        window.Show();
        try
        {
            Assert.True(await overlay.OpenAsync(fixture.Freeze()));
            window.UpdateLayout();
            await Task.Delay(200);
            var toolbar = overlay.FindControl<Border>("ToolbarPill")!;
            Assert.InRange(toolbar.Bounds.Left, 0, width);
            Assert.True(toolbar.Bounds.Right <= width, $"Toolbar overflows at {width}: {toolbar.Bounds}");
            Assert.Equal(new CornerRadius(18), toolbar.CornerRadius);
            Assert.Equal(new CornerRadius(14), overlay.FindControl<Border>("ModeTrack")!.CornerRadius);
            Assert.False(overlay.FindControl<Button>("CaptureButton")!.IsEnabled);
            Save(window, dark, width, "empty");

            window.MouseDown(new Point(40, 90), MouseButton.Left);
            window.MouseMove(new Point(260, 230));
            window.MouseUp(new Point(260, 230), MouseButton.Left);
            Assert.True(overlay.FindControl<Button>("CaptureButton")!.IsEnabled);
            var selection = overlay.FindControl<Border>("SelectionBox")!;
            Assert.Equal(220, selection.Width);
            Assert.Equal(140, selection.Height);
            Key(overlay, Avalonia.Input.Key.Right);
            Key(overlay, Avalonia.Input.Key.Right, KeyModifiers.Alt);
            Assert.Equal(221, selection.Width);
            Save(window, dark, width, "selection");

            Click(overlay, "ModeWindowBtn");
            Assert.True(overlay.FindControl<Button>("ModeWindowBtn")!.Classes.Contains("Active"));
            Assert.False(selection.IsVisible);
            Save(window, dark, width, "window");
            Click(overlay, "ModeScreenBtn");
            Assert.True(overlay.FindControl<Border>("ScreenOutline")!.IsVisible);
            Save(window, dark, width, "screen");
            Click(overlay, "ModeAreaBtn");
            Assert.True(selection.IsVisible);
            Assert.Equal(221, selection.Width);

            var grip = overlay.FindControl<Border>("ToolbarDragGrip")!;
            window.UpdateLayout();
            var point = grip.TranslatePoint(new Point(8, 17), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseMove(point + new Vector(0, -60));
            window.MouseUp(point + new Vector(0, -60), MouseButton.Left);
            Assert.InRange(toolbar.RenderTransform!.Value.M32, -61, -59);
            Key(overlay, Avalonia.Input.Key.Escape);
            Assert.True(overlay.IsOpen); // First Escape clears an existing selection.
            Assert.False(selection.IsVisible);
            Key(overlay, Avalonia.Input.Key.Escape);
            Assert.False(overlay.IsOpen);
        }
        finally { overlay.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task CursorOptionUsesLiveCaptureAndCountdownCanBeSkipped()
    {
        using var fixture = new CaptureFixture();
        var overlay = new ScreenshotOverlay(fixture.Service);
        var window = new Window { Content = overlay, Width = 800, Height = 600 };
        window.Show();
        try
        {
            await overlay.OpenAsync(fixture.Freeze(), ScreenshotCaptureMode.Screen);
            overlay.FindControl<ToggleButton>("CursorToggle")!.IsChecked = true;
            Click(overlay, "TimerBtn");
            var capture = Capture(overlay);
            window.UpdateLayout();
            var label = overlay.FindControl<TextBlock>("CountdownLabel")!;
            var point = label.TranslatePoint(new Point(label.Bounds.Width / 2, label.Bounds.Height / 2), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await capture.WaitAsync(TimeSpan.FromSeconds(3));
            Assert.False(overlay.IsOpen);
            Assert.Contains("-p", File.ReadAllLines(fixture.Request));
            Assert.Single(Directory.GetFiles(fixture.Saved, "*.png"));
        }
        finally { overlay.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task BackendFailureRestoresControlsAndAllowsRetry()
    {
        using var fixture = new CaptureFixture();
        File.WriteAllText(fixture.Fail, "fail");
        var overlay = new ScreenshotOverlay(fixture.Service);
        var window = new Window { Content = overlay, Width = 800, Height = 600 };
        window.Show();
        try
        {
            await overlay.OpenAsync(fixture.Freeze(), ScreenshotCaptureMode.Window);
            await Capture(overlay);
            Assert.True(overlay.IsOpen);
            Assert.True(overlay.FindControl<Border>("ToolbarPill")!.IsVisible);
            Assert.True(overlay.FindControl<Button>("CaptureButton")!.IsEnabled);
            File.Delete(fixture.Fail);
            await Capture(overlay);
            Assert.False(overlay.IsOpen);
            Assert.Single(Directory.GetFiles(fixture.Saved, "*.png"));
        }
        finally { overlay.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task InstantSaveFailureKeepsTheSelectionAndCanRetry()
    {
        using var fixture = new CaptureFixture();
        Directory.Delete(fixture.Saved);
        File.WriteAllText(fixture.Saved, "A file blocks the destination directory.");
        var overlay = new ScreenshotOverlay(fixture.Service);
        var window = new Window { Content = overlay, Width = 800, Height = 600 };
        window.Show();
        try
        {
            await overlay.OpenAsync(fixture.Freeze());
            window.UpdateLayout();
            Key(overlay, Avalonia.Input.Key.A, KeyModifiers.Control);
            await Capture(overlay);
            Assert.True(overlay.IsOpen);
            Assert.True(overlay.FindControl<Border>("SelectionBox")!.IsVisible);
            Assert.True(overlay.FindControl<Button>("CaptureButton")!.IsEnabled);
            File.Delete(fixture.Saved);
            Directory.CreateDirectory(fixture.Saved);
            await Capture(overlay);
            Assert.False(overlay.IsOpen);
            Assert.Single(Directory.GetFiles(fixture.Saved, "*.png"));
            Assert.False(File.Exists(fixture.Request));
        }
        finally { overlay.Close(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task EscapeDuringFrozenEncodingDoesNotDisposeWorkerPixelsOrLeavePartialFiles()
    {
        using var fixture = new CaptureFixture();
        var overlay = new ScreenshotOverlay(fixture.Service);
        var window = new Window { Content = overlay, Width = 800, Height = 600 };
        window.Show();
        try
        {
            await overlay.OpenAsync(fixture.Freeze(), ScreenshotCaptureMode.Screen);
            var capture = Capture(overlay);
            Key(overlay, Avalonia.Input.Key.Escape);
            await capture;
            Assert.False(overlay.IsOpen);
            Assert.Empty(Directory.GetFiles(fixture.Saved, "*.png"));
            Assert.False(File.Exists(fixture.Request));
        }
        finally { overlay.Close(); window.Close(); }
    }

    private static void Key(ScreenshotOverlay overlay, Key key, KeyModifiers modifiers = KeyModifiers.None)
        => overlay.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers });
    private static void Click(ScreenshotOverlay overlay, string name)
        => overlay.FindControl<Button>(name)!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
    private static Task Capture(ScreenshotOverlay overlay)
        => (Task)typeof(ScreenshotOverlay).GetMethod("ExecuteCaptureAsync", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!.Invoke(overlay, null)!;
    private static void Save(Window window, bool dark, int width, string state)
    {
        window.UpdateLayout();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Directory.CreateDirectory("/tmp/daisy-screenshot-previews");
        using var frame = window.CaptureRenderedFrame();
        frame!.Save($"/tmp/daisy-screenshot-previews/{(dark ? "Dark" : "Light")}-{width}-{state}.png");
    }

    private sealed class CaptureFixture : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"daisy-screenshot-ui-{Guid.NewGuid():N}");
        public string Saved => Path.Combine(_root, "saved");
        public string Request => Path.Combine(_root, "request");
        public string Fail => Path.Combine(_root, "fail");
        public DaisyScreenshotService Service { get; }
        public CaptureFixture()
        {
            Directory.CreateDirectory(Saved);
            using var image = new SKBitmap(1280, 720);
            using var canvas = new SKCanvas(image);
            canvas.Clear(SKColors.SteelBlue);
            using var stripe = new SKPaint { Color = SKColors.Coral };
            for (var i = 0; i < 40; i++) canvas.DrawRect(i * 40, 0, 10, 720, stripe);
            DaisyScreenshotService.SavePng(image, Path.Combine(_root, "source.png"));
            var script = Path.Combine(_root, "capture-backend");
            File.WriteAllText(script, "#!/bin/sh\nbase=$(dirname \"$0\")\nprintf '%s\\n' \"$@\" > \"$base/request\"\ntest ! -e \"$base/fail\" || exit 1\nwhile [ \"$#\" -gt 0 ]; do\n if [ \"$1\" = -o ]; then shift; output=$1; fi\n shift\ndone\ncp \"$base/source.png\" \"$output\"\n");
            File.SetUnixFileMode(script, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Service = new DaisyScreenshotService(script, Saved, TimeSpan.FromSeconds(2));
        }
        public string Freeze()
        {
            var path = Path.Combine(_root, $"freeze-{Guid.NewGuid():N}.png");
            File.Copy(Path.Combine(_root, "source.png"), path);
            return path;
        }
        public void Dispose() => Directory.Delete(_root, true);
    }
}
