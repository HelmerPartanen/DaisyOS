using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Views.Windows;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class LockScreenSettingsTests
{
    [AvaloniaTheory]
    [InlineData(760, false)]
    [InlineData(1280, true)]
    public async Task DedicatedCategoryShowsAllSettingsAndUpdatesPreview(int width, bool dark)
    {
        var view = new SettingsView();
        var window = new Window { Width = width, Height = 900, Content = view,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            view.SelectCategory("LockScreen");
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<ScrollViewer>("LockScreenView")!.IsVisible);
            Assert.True(view.FindControl<Button>("NavLockScreen")!.Classes.Contains("Active"));
            Assert.True(view.FindControl<StackPanel>("LockMoreOptions")!.IsEffectivelyVisible);
            Assert.Empty(view.FindControl<ScrollViewer>("LockScreenView")!.GetVisualDescendants().OfType<Expander>());
            var toggle = view.FindControl<ToggleSwitch>("DeptEffectToggle")!;
            toggle.IsChecked = false;
            Assert.False(view.FindControl<StackPanel>("DeptEffectOptions")!.IsEnabled);
            var options = view.FindControl<StackPanel>("LockClockOptions")!;
            Assert.Empty(view.FindControl<ScrollViewer>("LockScreenView")!.GetVisualDescendants().OfType<Slider>());
            var clockSize = Assert.Single(options.GetVisualDescendants().OfType<QuickSettingsSlider>());
            Assert.Equal(220, clockSize.Width);
            Assert.False(clockSize.ShowIcon);
            Assert.True(clockSize.ShowTicks);
            Assert.Null(view.FindControl<QuickSettingsSlider>("DeptStrengthSlider"));
            var weight = options.GetVisualDescendants().OfType<Border>().Single(b => Equals(b.Tag, "Font weight"))
                .GetVisualDescendants().OfType<ComboBox>().Single();
            weight.SelectedItem = weight.Items.OfType<ComboBoxItem>().Single(i => Equals(i.Tag, "Light"));
            await Task.Delay(250);
            window.UpdateLayout();
            var clock = view.FindControl<TextBlock>("DeptPreviewClock")!;
            Assert.Equal(FontWeight.Light, clock.FontWeight);
            Assert.False(view.FindControl<ScrollViewer>("AppearanceView")!.IsVisible);
            foreach (var row in options.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("SettingsGroupRow")))
            {
                foreach (var picker in row.GetVisualDescendants().OfType<ComboBox>())
                {
                    var position = picker.TranslatePoint(default, row)!.Value;
                    Assert.True(position.X >= 0 && position.X + picker.Bounds.Width <= row.Bounds.Width + 1);
                }
            }
            Directory.CreateDirectory("/tmp/daisy-lock-settings");
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-lock-settings/page-{width}.png", PngBitmapEncoderOptions.Default);
            window.UpdateLayout();
            var content = view.FindControl<StackPanel>("LockScreenContent")!;
            var sliders = content.GetVisualDescendants().OfType<QuickSettingsSlider>().ToArray();
            Assert.Equal(2, sliders.Length);
            foreach (var slider in sliders)
            {
                var origin = slider.TranslatePoint(default, content)!.Value;
                Assert.True(origin.X >= 0 && origin.X + slider.Bounds.Width <= content.Bounds.Width + 1);
            }
            var reset = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "Restore defaults"));
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(FontWeight.Bold, clock.FontWeight);
            Assert.True(toggle.IsChecked);
            view.SelectCategory("Appearance");
            Assert.False(view.FindControl<ScrollViewer>("LockScreenView")!.IsVisible);
            Assert.False(toggle.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NativeLockClockHonorsStyleAndFitsSmallScreens()
    {
        var window = new LockScreenWindow("solid:#2E3440", isSecondary: true) { Width = 640, Height = 480 };
        window.Show();
        try
        {
            await Task.Delay(300);
            window.ApplyLockScreenAppearance(new LockScreenAppearance { FontFamily = "Noto Serif", Weight = "Light",
                ClockSize = 140, Alignment = "Right", Position = "Lower", Color = "Mint", DateStyle = "Hidden",
                WallpaperDimming = 40, ShowMedia = false, ShowBattery = false });
            window.UpdateLayout();
            var clock = window.FindControl<TextBlock>("ClockText")!;
            Assert.Equal(FontWeight.Light, clock.FontWeight);
            Assert.Equal("Noto Serif", clock.FontFamily.Name);
            Assert.Equal(HorizontalAlignment.Right, window.FindControl<StackPanel>("ClockArea")!.HorizontalAlignment);
            Assert.False(window.FindControl<TextBlock>("DateText")!.IsVisible);
            Assert.Equal(0.4, window.FindControl<Border>("LockWallpaperTint")!.Opacity);
            Assert.True(clock.Bounds.Width <= window.ClientSize.Width - 48);
            window.ApplyLockScreenAppearance(new LockScreenAppearance { DateBelowClock = true });
            window.UpdateLayout();
            var area = window.FindControl<StackPanel>("ClockArea")!;
            Assert.Same(window.FindControl<TextBlock>("DateText"), area.Children.Last());
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task DisablingMediaRestoresSignInAndBlocksFullscreenExpansion()
    {
        var window = new LockScreenWindow("solid:#2E3440", new UnlockedPreviewService(), mediaService: new PreviewMediaService())
            { Width = 1280, Height = 720 };
        window.Show();
        try
        {
            await Task.Delay(400);
            await window.RefreshMediaAsync();
            await Task.Delay(350);
            await window.SetMediaExpandedAsync(true);
            window.ApplyLockScreenAppearance(new() { ShowMedia = false });
            await Task.Delay(450);
            window.UpdateLayout();
            Assert.False(window.FindControl<Grid>("CollapsedMediaHost")!.IsVisible);
            Assert.False(window.FindControl<Grid>("ExpandedMediaHost")!.IsVisible);
            var credentials = window.FindControl<StackPanel>("CredentialsPanel")!;
            Assert.True(credentials.IsVisible && credentials.IsHitTestVisible);
            Assert.Equal(1, credentials.Opacity);
            await window.SetMediaExpandedAsync(true);
            Assert.False(window.IsMediaExpanded);
            window.ApplyLockScreenAppearance(new());
            Assert.True(window.FindControl<Grid>("CollapsedMediaHost")!.IsVisible);
        }
        finally { window.Close(); }
    }

    private sealed class PreviewMediaService : IMediaSessionService
    {
        public event EventHandler? MediaChanged { add { } remove { } }
        public Task<MediaSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaSession?>(new MediaSession("Local music", "Artist", null, 10, 180, true, "local-player", null));
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class UnlockedPreviewService : ILockScreenService
    {
        public bool IsLocked => false;
        public event EventHandler<bool>? LockStateChanged { add { } remove { } }
        public void Lock() { }
        public void Unlock() { }
        public Task<bool> AuthenticateAsync(string password, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

}
