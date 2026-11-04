using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using System.IO;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.DesktopWidgets;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopMediaWidgetTests
{
    [AvaloniaFact]
    public void MediumTransportRevealsForPointerAndRetainsKeyboardFocus()
    {
        var widget = new MediaWidget();
        widget.Resources["MotionDurationFast"] = TimeSpan.Zero;
        widget.Resources["MotionDurationBase"] = TimeSpan.Zero;
        widget.SetGridSize(3, 2);
        var outside = new Button { Content = "Outside", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        widget.Width = 224;
        widget.Height = 180;
        var host = new Grid();
        host.Children.Add(widget);
        host.Children.Add(outside);
        var window = new Window { Width = 400, Height = 300, Content = host };
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            var layout = widget.FindControl<Grid>("MediumLayout")!;
            var info = widget.FindControl<Grid>("MediumInfoHost")!;
            var controls = widget.FindControl<StackPanel>("MediumControlsStack")!;
            var button = widget.FindControl<Button>("MediumPlayPauseButton")!;
            window.MouseMove(new Point(390, 290));
            Assert.True(outside.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, info.Opacity);
            Assert.Equal(0, controls.Opacity);
            Assert.True(button.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused);
            Assert.Equal(1, controls.Opacity);
            Assert.True(outside.Focus());
            Dispatcher.UIThread.RunJobs();
            window.MouseMove(layout.TranslatePoint(new Point(50, 50), window)!.Value);
            Assert.Equal(0, info.Opacity);
            Assert.Equal(1, controls.Opacity);
            Assert.True(controls.IsHitTestVisible);
            Assert.True(button.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.True(button.IsFocused);
            window.MouseMove(new Point(390, 290));
            Assert.Equal(1, controls.Opacity);
            Assert.True(outside.Focus());
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, info.Opacity);
            Assert.Equal(0, controls.Opacity);
            Assert.False(controls.IsHitTestVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task LargeTimelineMovesWhilePlayingStopsWhenPausedAndSeeks()
    {
        var media = new TestMediaService();
        var widget = new MediaWidget(media) { Width = 362, Height = 168 };
        widget.SetGridSize(5, 2);
        var window = new Window { Width = 400, Height = 210, Content = widget };
        window.Show();
        try
        {
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var title = widget.FindControl<DaisyOS.Shell.Controls.MarqueeText>("LargeTitle")!;
            var titleText = title.GetVisualDescendants().OfType<TextBlock>().First();
            var artist = widget.FindControl<TextBlock>("LargeArtist")!;
            Assert.Equal(Avalonia.Media.TextAlignment.Left, titleText.TextAlignment);
            Assert.Equal(artist.TranslatePoint(default, widget)!.Value.X,
                titleText.TranslatePoint(default, widget)!.Value.X, 1);
            var track = widget.FindControl<Border>("LargeProgressTrack")!;
            var fill = widget.FindControl<Border>("LargeProgressFill")!;
            Assert.True(track.Bounds.Width > 0);
            Assert.StartsWith("0.", widget.FindControl<TextBlock>("LargeElapsedText")!.Text);
            var before = fill.Width;
            await Task.Delay(160);
            Dispatcher.UIThread.RunJobs();
            Assert.True(fill.Width > before);
            Assert.NotEqual(Math.Round(fill.Width), fill.Width);

            media.Session = media.Session with { IsPlaying = false, PositionSeconds = 10 };
            media.RaiseChanged();
            await Task.Delay(100);
            Dispatcher.UIThread.RunJobs();
            var paused = fill.Width;
            await Task.Delay(300);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(paused, fill.Width);
            Assert.Equal("- 0.50", widget.FindControl<TextBlock>("LargeRemainingText")!.Text);

            var middle = track.TranslatePoint(new Point(track.Bounds.Width / 2, 3), window)!.Value;
            window.MouseDown(middle, MouseButton.Left);
            window.MouseUp(middle, MouseButton.Left);
            await Task.Delay(200);
            Dispatcher.UIThread.RunJobs();
            Assert.InRange(media.LastSeek, 29.9, 30.1);
            Assert.Equal("0.30", widget.FindControl<TextBlock>("LargeElapsedText")!.Text);

            var button = widget.FindControl<Button>("LargePlayPauseButton")!;
            Assert.Equal(new Size(40, 40), button.Bounds.Size);
            var transport = (StackPanel)button.Parent!;
            var previous = (Button)transport.Children[0];
            var next = (Button)transport.Children[2];
            Assert.Equal(new Size(36, 36), previous.Bounds.Size);
            Assert.Equal(new Size(36, 36), next.Bounds.Size);
            Assert.Equal(16, button.Bounds.X - previous.Bounds.Right);
            Assert.Equal(16, next.Bounds.X - button.Bounds.Right);
            Assert.True(button.Bounds.Bottom <= widget.Bounds.Height);
            Directory.CreateDirectory("/tmp/daisy-desktop-media");
            using var frame = window.CaptureRenderedFrame();
            frame!.Save("/tmp/daisy-desktop-media/large.png");
            window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
            window.Background = Avalonia.Media.Brushes.Black;
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            using var dark = window.CaptureRenderedFrame();
            dark!.Save("/tmp/daisy-desktop-media/large-dark.png");
            widget.SetGridSize(3, 2);
            widget.Width = 214;
            window.UpdateLayout();
            widget.FindControl<Button>("MediumPlayPauseButton")!.Focus();
            await Task.Delay(200);
            using var medium = window.CaptureRenderedFrame();
            medium!.Save("/tmp/daisy-desktop-media/medium-controls.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ExtraLargeMediaSizePersistsAndUsesPortraitIcon()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daisy-media-layout-{Guid.NewGuid():N}.json");
        try
        {
            var store = new DaisyOS.Shell.Services.Desktop.DesktopLayoutStore(path);
            var desktop = new DaisyOS.Shell.ViewModels.DesktopViewModel(store);
            desktop.ClearItems();
            var item = desktop.AddNewWidget("media", 5, 2);
            Assert.True(item.SupportsExtraLargeSize);
            Assert.Equal("widget_extralarge2", item.ExtraLargeSizeIcon);
            Assert.True(Avalonia.Platform.AssetLoader.Exists(new Uri(
                "avares://DaisyOS.Shell/Assets/Icons/HugeIcons/widget_extralarge2.svg")));
            desktop.ResizeWidget(item, 5, item.ExtraLargeRowSpan);
            Assert.True(item.IsSizeExtraLarge);
            Assert.False(item.IsSizeLarge);
            var restored = new DaisyOS.Shell.ViewModels.DesktopViewModel(store);
            var media = Assert.Single(restored.Items.Where(i => i.IsMediaWidget));
            Assert.True(media.IsSizeExtraLarge);
            Assert.True(media.SupportsExtraLargeSize);
            Assert.Equal(6, media.RowSpan);
        }
        finally { File.Delete(path); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExtraLargePortraitKeepsSquareArtworkAboveControls(bool dark)
    {
        var art = LockScreenMediaTests.CreateSyntheticCurrentsArtwork();
        var service = new TestMediaService();
        service.Session = service.Session with { AlbumArtPath = art, IsPlaying = false };
        var widget = new MediaWidget(service) { Width = 362, Height = 520 };
        widget.SetGridSize(5, MediaWidget.ExtraLargeRows);
        var resources = new Avalonia.Markup.Xaml.Styling.ResourceInclude(new Uri("avares://DaisyOS.Shell/"))
        { Source = new Uri("avares://DaisyOS.Shell/Themes/WidgetContainerTheme.axaml") };
        widget.FindControl<DaisyOS.Shell.Controls.WidgetContainer>("Container")!.Theme =
            (Avalonia.Styling.ControlTheme)resources.Loaded[typeof(DaisyOS.Shell.Controls.WidgetContainer)]!;
        var window = new Window
        {
            Width = 410, Height = 568, Content = widget,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light,
            Background = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse(dark ? "#231C34" : "#D9D3E5"))
        };
        window.Show();
        try
        {
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var artwork = widget.FindControl<Border>("LargeArtworkFrame")!;
            var details = widget.FindControl<Grid>("LargeDetails")!;
            Assert.Equal(artwork.Bounds.Width, artwork.Bounds.Height, 2);
            Assert.True(artwork.Bounds.Width > 280);
            Assert.Equal(new CornerRadius(14), artwork.CornerRadius);
            var title = widget.FindControl<DaisyOS.Shell.Controls.MarqueeText>("LargeTitle")!;
            var artist = widget.FindControl<TextBlock>("LargeArtist")!;
            Assert.Equal(Avalonia.Media.TextAlignment.Left, artist.TextAlignment);
            Assert.Equal(artwork.TranslatePoint(default, widget)!.Value.X,
                title.TranslatePoint(default, widget)!.Value.X, 1);
            Assert.Equal(title.TranslatePoint(default, widget)!.Value.X,
                artist.TranslatePoint(default, widget)!.Value.X, 1);

            Assert.True(artwork.Bounds.Bottom < details.Bounds.Top);
            Assert.True(details.Bounds.Bottom <= widget.FindControl<Grid>("LargeLayout")!.Bounds.Height);
            Assert.Equal(40, widget.FindControl<Button>("LargePlayPauseButton")!.Bounds.Height);
            Assert.True(widget.FindControl<Border>("LargeProgressFill")!.Width > 0);
            Directory.CreateDirectory("/tmp/daisy-desktop-media");
            using (var frame = window.CaptureRenderedFrame())
                frame!.Save($"/tmp/daisy-desktop-media/extralarge-{(dark ? "dark" : "light")}.png");

            // Constrained desktops still keep the portrait layout and every control.
            widget.Width = 288;
            widget.Height = 432;
            widget.SetGridSize(4, 5);
            window.UpdateLayout();
            Assert.Equal(artwork.Bounds.Width, artwork.Bounds.Height, 2);
            Assert.True(artwork.Bounds.Bottom < details.Bounds.Top);
            Assert.True(details.Bounds.Bottom <= widget.FindControl<Grid>("LargeLayout")!.Bounds.Height);
            widget.SetGridSize(5, 2);
            widget.Width = 362;
            widget.Height = 168;
            window.UpdateLayout();
            Assert.True(artwork.Bounds.Right < details.Bounds.Left);
            Assert.Equal(artwork.Bounds.Width, artwork.Bounds.Height, 2);
        }
        finally { window.Close(); File.Delete(art); }
    }

    private sealed class TestMediaService : IMediaSessionService
    {
        public MediaSession Session = new("Let it happen", "Tame Impala", null, 10, 60, true);
        public double LastSeek { get; private set; }
        public event EventHandler? MediaChanged;
        public void RaiseChanged() => MediaChanged?.Invoke(this, EventArgs.Empty);
        public Task<MediaSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) => Task.FromResult<MediaSession?>(Session);
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task SeekAsync(double positionSeconds, CancellationToken cancellationToken = default)
        {
            LastSeek = positionSeconds;
            Session = Session with { PositionSeconds = positionSeconds };
            return Task.CompletedTask;
        }
    }

}
