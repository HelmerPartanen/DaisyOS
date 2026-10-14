using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ImageLifetimeTests
{
    [AvaloniaFact]
    public void ReplacingAndDisposingArtworkDuringTransitionsKeepsRendering()
    {
        var image = new Image();
        ThemeTransitionPolicy.SetEnabled(image, true);
        var window = new Window { Width = 320, Height = 200, Content = image };
        window.Resources["MotionDurationBase"] = TimeSpan.FromSeconds(1);
        var current = CreateBitmap();
        image.Source = current;
        window.Show();
        try
        {
            for (var i = 0; i < 40; i++)
            {
                var previous = current;
                current = CreateBitmap();
                image.Source = current;
                previous.Dispose();
                Assert.IsNotType<WriteableBitmap>(image.Source);
                using var renderTarget = new RenderTargetBitmap(new PixelSize(128, 128));
                using (var context = renderTarget.CreateDrawingContext())
                    image.Source!.Draw(context, new Rect(0, 0, 64, 64), new Rect(0, 0, 128, 128));
                // Render immediately, before the transition timer can observe
                // the new base value. This reproduced the shell render failure.
                image.InvalidateMeasure();
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                Assert.True(image.Source!.Size.Width > 0);
            }
            image.Source = null;
            current.Dispose();
            image.InvalidateMeasure();
            window.UpdateLayout();
            using var lastFrame = window.CaptureRenderedFrame();
            Assert.NotNull(lastFrame);
        }
        finally { window.Close(); current.Dispose(); Dispatcher.UIThread.RunJobs(); }
    }

    [AvaloniaFact]
    public void AsyncImageDoesNotRetainOwnedBitmapsForThemeTransitions()
    {
        Assert.False(ThemeTransitionPolicy.GetEnabled(new AsyncImage()));
    }

    private static WriteableBitmap CreateBitmap() => new(new PixelSize(64, 64),
        new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
}
