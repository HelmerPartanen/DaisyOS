using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class TaskbarSizeTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ResizingGrowsUpwardAndKeepsBottomInset(bool dark)
    {
        var view = new TaskbarView();
        var window = new Window { Width = 1000, Height = 180, Content = view,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        try
        {
            Directory.CreateDirectory("/tmp/daisy-taskbar-size");
            foreach (var height in new[] { 44d, 52d, 55d, 44d })
            {
                Application.Current!.Resources["TaskbarScale"] = height / 52;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                using var frame = window.CaptureRenderedFrame();
                var body = view.FindControl<Border>("TaskbarBody")!;
                var top = body.TranslatePoint(default, window)!.Value;
                var bottom = body.TranslatePoint(new Point(0, body.Bounds.Height), window)!.Value;
                Assert.Equal(height, bottom.Y - top.Y, 3);
                Assert.Equal(window.ClientSize.Height - 3, bottom.Y, 3);
                Assert.True(top.X >= 0);
                frame!.Save($"/tmp/daisy-taskbar-size/{dark}-{height}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); Application.Current!.Resources["TaskbarScale"] = 1d; }
    }

    [AvaloniaFact]
    public void AppearanceShowsThreeSnappingSizeStops()
    {
        var window = new DaisyOS.Shell.Apps.System.Settings.SettingsWindow { Width = 1040, Height = 850 };
        window.Show();
        try
        {
            window.SelectCategory("Appearance");
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var view = window.FindControl<DaisyOS.Shell.Apps.System.Settings.SettingsView>("SettingsContent")!;
            var slider = view.FindControl<DaisyOS.Shell.Controls.QuickSettingsSlider>("TaskbarSizeSlider")!;
            Assert.Equal(0, slider.Minimum);
            Assert.Equal(2, slider.Maximum);
            Assert.Equal(1, slider.Value);
            Assert.Equal(1, slider.StepFrequency);
            Assert.True(slider.ShowTicks);
            var volume = new DaisyOS.Shell.Controls.QuickSettingsSlider { Value = 42.5 };
            Assert.Equal(42.5, volume.Value);
            Assert.False(volume.ShowTicks);
            slider.Value = 1.4;
            Assert.Equal(1, slider.Value);
            slider.Value = 1.8;
            Assert.Equal(2, slider.Value);
            slider.Value = 1;
            slider.BringIntoView();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Directory.CreateDirectory("/tmp/daisy-taskbar-size");
            using var frame = window.CaptureRenderedFrame();
            Assert.Equal(3, slider.FindControl<Canvas>("TickMarks")!.Children.Count);
            frame!.Save("/tmp/daisy-taskbar-size/settings.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [Fact]
    public async Task SizePersistsAndOlderPreferencesKeepDefault()
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-taskbar-size-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "appearance.json");
        try
        {
            var store = new AppearanceSettingsStore(path);
            Assert.Equal(1, store.Load().TaskbarSize);
            foreach (var size in new[] { 0, 1, 2 })
            {
                await store.SaveAsync(new AppearanceSettings { TaskbarSize = size });
                Assert.Equal(size, store.Load().TaskbarSize);
            }
            await File.WriteAllTextAsync(path, "{}");
            Assert.Equal(1, store.Load().TaskbarSize);
        }
        finally { Directory.Delete(directory, true); }
    }
}
