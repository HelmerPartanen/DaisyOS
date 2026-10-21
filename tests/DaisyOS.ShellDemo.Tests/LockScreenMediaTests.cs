using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Views.Components.Wallpaper;
using DaisyOS.Shell.Views.Windows;
using DaisyOS.System.Media;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LockScreenMediaTests
{
    [Fact]
    public void SpotifyCanvasResolverExtractsTrackIdAndCanvasMp4Urls()
    {
        Assert.True(SpotifyCanvasResolver.TryExtractSpotifyTrackId(
            "https://open.spotify.com/track/1KcOU0q9AMXV9xi4tSzzCc?si=abc", out var id1));
        Assert.Equal("1KcOU0q9AMXV9xi4tSzzCc", id1);

        Assert.True(SpotifyCanvasResolver.TryExtractSpotifyTrackId(
            "spotify:track:2X485T9Z5Ly0xyaghN73ed", out var id2));
        Assert.Equal("2X485T9Z5Ly0xyaghN73ed", id2);

        Assert.True(SpotifyCanvasResolver.TryExtractSpotifyTrackId(
            "/com/spotify/track/5hM5arv9KDbCHS0k9uqwjr", out var id3));
        Assert.Equal("5hM5arv9KDbCHS0k9uqwjr", id3);

        const string sampleHtml = """
            <div class="artist-page__canvas-container">
              <button data-umami-event-canvas="spotify:track:1KcOU0q9AMXV9xi4tSzzCc"
                      onclick='saveFile("https://canvaz.scdn.co/upload/licensor/7JGwF0zhX9oItt9901OvB5/video/441c47add2e44202bf8fa55bf4a1af7e.cnvs.mp4", "Tame Impala")'></button>
            </div>
            <div class="artist-page__canvas-container">
              <button data-umami-event-canvas="spotify:track:1FnHpM2zimTALdIvuxFoNM"
                      onclick='saveFile("https://canvaz.scdn.co/upload/licensor/7JGwF0zhX9oItt9901OvB5/video/7d21dd7a6fb94bacb91dc206d639379b.cnvs.mp4", "Tame Impala")'></button>
            </div>
            """;

        var matched = SpotifyCanvasResolver.ExtractCanvasUrlFromHtml(sampleHtml, preferredTrackId: "1FnHpM2zimTALdIvuxFoNM");
        Assert.Equal("https://canvaz.scdn.co/upload/licensor/7JGwF0zhX9oItt9901OvB5/video/7d21dd7a6fb94bacb91dc206d639379b.cnvs.mp4", matched);
    }

    [Fact]
    public void FormatMediaTimeMatchesExpectedDotSeparatedFormat()
    {
        Assert.Equal("1.38", LockScreenWindow.FormatMediaTime(98, negative: false));
        Assert.Equal("- 1.22", LockScreenWindow.FormatMediaTime(82, negative: true));
        Assert.Equal("0.05", LockScreenWindow.FormatMediaTime(5, negative: false));
    }

    [AvaloniaFact]
    public async Task LockScreenShowsLargeBottomLeftWidgetAndExpandsToFullscreenCanvasMode()
    {
        var artworkPath = CreateSyntheticCurrentsArtwork();
        var fakeMedia = new FakeMediaSessionService
        {
            CurrentSession = new MediaSession(
                Title: "Let it happen",
                Artist: "Tame Impala",
                AlbumArtPath: artworkPath,
                PositionSeconds: 98,
                DurationSeconds: 180,
                IsPlaying: true,
                SourceIdentity: "spotify",
                SourceUrl: "spotify:track:1KcOU0q9AMXV9xi4tSzzCc")
        };

        var window = new LockScreenWindow(
            wallpaperSource: "solid:#3D2B24",
            lockService: new PreviewLockService(),
            isSecondary: false,
            mediaService: fakeMedia)
        {
            WindowState = WindowState.Normal,
            Width = 1280,
            Height = 720
        };

        window.Show();
        try
        {
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            await window.RefreshMediaAsync();
            await Task.Delay(350);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var collapsedHost = window.FindControl<Grid>("CollapsedMediaHost")!;
            var collapsedBorder = window.FindControl<Border>("CollapsedMediaBorder")!;
            var collapsedArtwork = window.FindControl<Border>("CollapsedArtworkButton")!;
            var collapsedTitle = window.FindControl<TextBlock>("CollapsedTitleText")!;
            var collapsedArtist = window.FindControl<TextBlock>("CollapsedArtistText")!;
            var credentials = window.FindControl<StackPanel>("CredentialsPanel")!;
            var expandedHost = window.FindControl<Grid>("ExpandedMediaHost")!;

            Assert.True(window.HasActiveMediaSession);
            Assert.False(window.IsMediaExpanded);
            Assert.True(collapsedHost.IsVisible);
            Assert.False(expandedHost.IsVisible);
            Assert.Equal("Let it happen", collapsedTitle.Text);
            Assert.Equal("Tame Impala", collapsedArtist.Text);
            Assert.Equal(354, collapsedBorder.Bounds.Width);
            Assert.Equal(160, collapsedBorder.Bounds.Height);

            // Verify bottom-left placement
            var collapsedOrigin = collapsedHost.TranslatePoint(default, window)!.Value;
            Assert.Equal(24, collapsedOrigin.X, precision: 1);
            Assert.Equal(720 - 24 - 160, collapsedOrigin.Y, precision: 1);

            Directory.CreateDirectory("/tmp/daisy-lock-media");
            using (var collapsedFrame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(collapsedFrame);
                collapsedFrame!.Save("/tmp/daisy-lock-media/lock-media-collapsed.png", PngBitmapEncoderOptions.Default);
            }

            // Expand to fullscreen mode by clicking the artwork (test SetMediaExpandedAsync)
            var expandTask = window.SetMediaExpandedAsync(true);
            await Task.Delay(120);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            // Mid-transition: MorphingArtworkBorder bridges the two states smoothly
            var morphingArtwork = window.FindControl<Border>("MorphingArtworkBorder")!;
            if (window.MediaExpandProgress > 0.01 && window.MediaExpandProgress < 0.99)
            {
                Assert.True(morphingArtwork.IsVisible);
                Assert.InRange(morphingArtwork.Bounds.Width, 126, 312);
            }

            await expandTask;
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.True(window.IsMediaExpanded);
            Assert.Equal(1.0, window.MediaExpandProgress, precision: 2);
            Assert.True(expandedHost.IsVisible);
            Assert.False(collapsedHost.IsVisible);
            Assert.Equal(0, credentials.Opacity, precision: 2);
            Assert.False(credentials.IsHitTestVisible);

            var expandedTitle = window.FindControl<TextBlock>("ExpandedTitleText")!;
            var expandedArtist = window.FindControl<TextBlock>("ExpandedArtistText")!;
            var elapsedText = window.FindControl<TextBlock>("ExpandedElapsedText")!;
            var remainingText = window.FindControl<TextBlock>("ExpandedRemainingText")!;
            var progressFill = window.FindControl<Border>("ExpandedProgressFill")!;
            var canvasLayer = window.FindControl<Panel>("LockCanvasLayer")!;

            Assert.Equal("Let it happen", expandedTitle.Text);
            Assert.Equal("Tame Impala", expandedArtist.Text);
            Assert.StartsWith("1.", elapsedText.Text);
            Assert.StartsWith("- 1.", remainingText.Text);
            Assert.True(progressFill.Bounds.Width > 50);
            Assert.True(canvasLayer.IsVisible);
            Assert.Equal(1.0, canvasLayer.Opacity, precision: 2);

            using (var expandedFrame = window.CaptureRenderedFrame())
            {
                Assert.NotNull(expandedFrame);
                expandedFrame!.Save("/tmp/daisy-lock-media/lock-media-expanded.png", PngBitmapEncoderOptions.Default);
            }

            // Collapse back to normal lock screen mode
            await window.SetMediaExpandedAsync(false);
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(window.IsMediaExpanded);
            Assert.Equal(0.0, window.MediaExpandProgress, precision: 2);
            Assert.True(collapsedHost.IsVisible);
            Assert.False(expandedHost.IsVisible);
            Assert.Equal(1.0, credentials.Opacity, precision: 2);
            Assert.True(credentials.IsHitTestVisible);
            Assert.False(canvasLayer.IsVisible);

            // Stopping media playback smoothly hides the widget and resets state
            fakeMedia.CurrentSession = null;
            await window.RefreshMediaAsync();
            await Task.Delay(320);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            Assert.False(window.HasActiveMediaSession);
            Assert.False(collapsedHost.IsVisible);
            Assert.False(expandedHost.IsVisible);
        }
        finally
        {
            window.Close();
            try { if (File.Exists(artworkPath)) File.Delete(artworkPath); } catch { }
        }
    }

    [AvaloniaFact]
    public async Task SpotifyCanvasResolverResolvesLocalVideoFileForFullscreenCanvasWallpaper()
    {
        var tempVideo = Path.Combine(Path.GetTempPath(), $"daisy-test-canvas-{Guid.NewGuid():N}.mp4");
        await File.WriteAllBytesAsync(tempVideo, [0x00, 0x00, 0x00, 0x18, 0x66, 0x74, 0x79, 0x70]);
        try
        {
            var session = new MediaSession(
                Title: "Let it happen",
                Artist: "Tame Impala",
                AlbumArtPath: null,
                PositionSeconds: 45,
                DurationSeconds: 180,
                IsPlaying: true,
                SourceIdentity: "spotify",
                SourceUrl: $"file://{tempVideo}");

            var resolvedPath = await SpotifyCanvasResolver.ResolveAsync(session);
            Assert.Equal(tempVideo, resolvedPath);
        }
        finally
        {
            try { if (File.Exists(tempVideo)) File.Delete(tempVideo); } catch { }
        }
    }

    private static string CreateSyntheticCurrentsArtwork()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daisy-currents-art-{Guid.NewGuid():N}.png");
        using var bitmap = new RenderTargetBitmap(new PixelSize(320, 320), new Vector(96, 96));
        using (var ctx = bitmap.CreateDrawingContext())
        {
            var bg = new LinearGradientBrush
            {
                StartPoint = new RelativePoint(0, 0, RelativeUnit.Relative),
                EndPoint = new RelativePoint(1, 1, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FF2B1B42"), 0),
                    new GradientStop(Color.Parse("#FF4C2B62"), 0.55),
                    new GradientStop(Color.Parse("#FF1C1429"), 1)
                }
            };
            ctx.FillRectangle(bg, new Rect(0, 0, 320, 320));

            // Diagonal crimson ribbon & chrome sphere inspired by Currents cover
            var ribbonPen = new Pen(new SolidColorBrush(Color.Parse("#FFE12845")), 14);
            ctx.DrawLine(ribbonPen, new Point(16, 12), new Point(176, 196));

            var amberPen = new Pen(new SolidColorBrush(Color.Parse("#FFF5A623")), 8);
            ctx.DrawLine(amberPen, new Point(176, 196), new Point(220, 312));

            var sphereBrush = new RadialGradientBrush
            {
                Center = new RelativePoint(0.38, 0.35, RelativeUnit.Relative),
                GradientOrigin = new RelativePoint(0.38, 0.35, RelativeUnit.Relative),
                RadiusX = new RelativeScalar(0.5, RelativeUnit.Relative),
                RadiusY = new RelativeScalar(0.5, RelativeUnit.Relative),
                GradientStops =
                {
                    new GradientStop(Color.Parse("#FFF4F4F6"), 0),
                    new GradientStop(Color.Parse("#FF8A8E99"), 0.55),
                    new GradientStop(Color.Parse("#FF23252C"), 1)
                }
            };
            ctx.DrawEllipse(sphereBrush, null, new Point(164, 182), 40, 40);
        }

        bitmap.Save(path, PngBitmapEncoderOptions.Default);
        return path;
    }

    private sealed class FakeMediaSessionService : IMediaSessionService
    {
        public MediaSession? CurrentSession { get; set; }
        public event EventHandler? MediaChanged;

        public void RaiseChanged() => MediaChanged?.Invoke(this, EventArgs.Empty);

        public Task<MediaSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentSession);

        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class PreviewLockService : ILockScreenService
    {
        public bool IsLocked => false;
        public event EventHandler<bool>? LockStateChanged { add { } remove { } }
        public void Lock() { }
        public void Unlock() { }
        public Task<bool> AuthenticateAsync(string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(false);
    }
}
