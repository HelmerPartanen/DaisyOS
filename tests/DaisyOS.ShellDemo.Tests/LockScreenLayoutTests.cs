using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.Views.Windows;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LockScreenLayoutTests
{
    [AvaloniaTheory]
    [InlineData(1280, 720, 100, "Atkinson Hyperlegible", "en-US")]
    [InlineData(1920, 1080, 100, "Atkinson Hyperlegible", "en-US")]
    [InlineData(640, 480, 150, "Noto Sans", "fi-FI")]
    [InlineData(360, 640, 125, "DejaVu Serif", "fi-FI")]
    public async Task ClockCredentialsAndFeedbackFitOrScrollWithoutOverlap(int width, int height, int scale, string font, string language)
    {
        var resources = Application.Current!.Resources;
        Loc.Apply(resources, language);
        TypographyPolicy.Apply(resources, font, scale);
        var window = new LockScreenWindow("solid:#2E3440", new PreviewLockService())
        { WindowState = WindowState.Normal, Width = width, Height = height };
        window.Show();
        try
        {
            await Task.Delay(400);
            Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory("/tmp/daisy-lock-polish");
            using (var initialFrame = window.CaptureRenderedFrame())
                initialFrame!.Save($"/tmp/daisy-lock-polish/balanced-{width}-{height}-{scale}.png", PngBitmapEncoderOptions.Default);
            var name = window.FindControl<TextBlock>("UserNameText")!;
            name.Text = "Alexandra Åström — a long display name";
            var error = window.FindControl<TextBlock>("ErrorMessageText")!;
            error.Text = Loc.Get("Incorrect password. Please try again.");
            error.IsVisible = true;
            window.FindControl<TextBlock>("CapsLockWarningText")!.IsVisible = true;
            window.UpdateLayout();
            var clock = window.FindControl<TextBlock>("ClockText")!;
            var clockArea = window.FindControl<StackPanel>("ClockArea")!;
            var credentials = window.FindControl<StackPanel>("CredentialsPanel")!;
            var scroller = window.FindControl<ScrollViewer>("LockContentScroll")!;
            var clockOrigin = clockArea.TranslatePoint(default, window)!.Value;
            var credentialsOrigin = credentials.TranslatePoint(default, window)!.Value;
            Assert.True(clockOrigin.Y + clockArea.Bounds.Height <= credentialsOrigin.Y);
            Assert.True(clock.Bounds.Width <= window.ClientSize.Width - 48);
            Assert.Equal(128, ((SolidColorBrush)clock.Foreground!).Color.A);
            var password = window.FindControl<TextBox>("PasswordBox")!;
            Assert.Equal((double)resources["ControlHeightStandard"]!, password.Bounds.Height);
            Assert.True(credentials.Bounds.Width <= window.ClientSize.Width - 48);
            credentials.BringIntoView();
            window.UpdateLayout();
            credentialsOrigin = credentials.TranslatePoint(default, window)!.Value;
            Assert.True(credentialsOrigin.Y + credentials.Bounds.Height <= scroller.Bounds.Bottom + 1);
            Assert.True(scroller.Extent.Height >= scroller.Viewport.Height);
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Directory.CreateDirectory("/tmp/daisy-lock-polish");
            frame!.Save($"/tmp/daisy-lock-polish/lock-{width}-{height}-{scale}.png", PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); Loc.Apply(resources, "en-US"); TypographyPolicy.Apply(resources, "Atkinson Hyperlegible", 100); }
    }

    private sealed class PreviewLockService : ILockScreenService
    {
        public bool IsLocked => false;
        public event EventHandler<bool>? LockStateChanged { add { } remove { } }
        public void Lock() { }
        public void Unlock() { }
        public Task<bool> AuthenticateAsync(string password, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }
}
