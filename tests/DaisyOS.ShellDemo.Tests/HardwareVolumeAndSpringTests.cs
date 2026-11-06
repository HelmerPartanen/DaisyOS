using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Motion;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Views.Components.Volume;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class HardwareVolumeAndSpringTests
{
    [Fact]
    public async Task RapidKeysAccumulateAndMuteTogglesAreSerialized()
    {
        var audio = new Audio();
        var controller = new VolumeKeyController(audio);
        var samples = new List<(double Volume, bool Muted)>();
        controller.Changed += (volume, muted, _) => samples.Add((volume, muted));
        var first = controller.HandleAsync("volume-up");
        await controller.HandleAsync("volume-up");
        await controller.HandleAsync("volume-up");
        audio.Gate.SetResult();
        await first;
        Assert.Equal(65, audio.Volume);
        Assert.Single(samples);
        await controller.HandleAsync("volume-mute");
        Assert.True(audio.Muted);
        await controller.HandleAsync("volume-up-small");
        Assert.Equal(66, audio.Volume);
        Assert.False(audio.Muted);
        audio.Volume = 99;
        await controller.HandleAsync("volume-up");
        Assert.Equal(100, audio.Volume);
        await controller.HandleAsync("microphone-mute");
        Assert.True(audio.InputMuted);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VolumeIndicatorUsesSharedTrackAndReversesDismissal(bool dark)
    {
        var indicator = new VolumeIndicator();
        var window = new Window { Content = indicator, Width = 400, Height = 180,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        try
        {
            indicator.ShowLevel(62, false);
            await Task.Delay(500);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var slider = indicator.FindControl<QuickSettingsSlider>("Level")!;
            Assert.False(slider.ShowThumb);
            Assert.Equal(62, slider.Value);
            Assert.False(indicator.IsHitTestVisible);
            Assert.NotNull(indicator.MaterialSurface.Background);
            Assert.Equal(320, indicator.MaterialSurface.Bounds.Width);
            Assert.Equal(new CornerRadius(999), indicator.MaterialSurface.CornerRadius);
            Directory.CreateDirectory("/tmp/daisy-volume-preview");
            using (var frame = window.CaptureRenderedFrame())
                frame!.Save($"/tmp/daisy-volume-preview/{(dark ? "Dark" : "Light")}.png");
            var dismissal = indicator.DismissAsync();
            await Task.Delay(30);
            indicator.ShowLevel(67, false);
            await dismissal;
            await Task.Delay(500);
            Assert.True(indicator.IsVisible);
            Assert.False(indicator.IsHitTestVisible);
            Assert.Equal(67, slider.Value);
            await indicator.DismissAsync();
            Assert.False(indicator.IsVisible);
            var options = LayerShellSurfaceOptions.VolumeIndicator();
            Assert.True(options.InputPassthrough);
            Assert.Equal(LayerShellKeyboardInteractivity.None, options.KeyboardInteractivity);
            Assert.Equal(-1, options.ExclusiveZone);
        }
        finally { window.Close(); }
    }

    private sealed class Audio : IAudioService
    {
        public double Volume = 50;
        public bool Muted, InputMuted;
        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<double?> GetVolumeAsync(CancellationToken token = default) { await Gate.Task; return Volume; }
        public Task SetVolumeAsync(double value, CancellationToken token = default) { Volume = value; return Task.CompletedTask; }
        public Task<bool> GetMutedAsync(CancellationToken token = default) => Task.FromResult(Muted);
        public Task SetMutedAsync(bool value, CancellationToken token = default) { Muted = value; return Task.CompletedTask; }
        public Task<double?> GetInputVolumeAsync(CancellationToken token = default) => Task.FromResult<double?>(50);
        public Task SetInputVolumeAsync(double value, CancellationToken token = default) => Task.CompletedTask;
        public Task<bool> GetInputMutedAsync(CancellationToken token = default) => Task.FromResult(InputMuted);
        public Task SetInputMutedAsync(bool value, CancellationToken token = default) { InputMuted = value; return Task.CompletedTask; }
        public Task<string> GetDefaultDeviceNameAsync(CancellationToken token = default) => Task.FromResult("Test output");
        public Task<IReadOnlyList<AudioDeviceInfo>> GetAudioDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task<bool> SetDefaultAudioDeviceAsync(string id, CancellationToken token = default) => Task.FromResult(true);
        public Task<IReadOnlyList<AudioDeviceInfo>> GetAudioInputDevicesAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<AudioDeviceInfo>>([]);
        public Task<bool> SetDefaultAudioInputDeviceAsync(string id, CancellationToken token = default) => Task.FromResult(true);
        public Task<IReadOnlyList<AudioAppStreamInfo>> GetAudioAppStreamsAsync(CancellationToken token = default) => Task.FromResult<IReadOnlyList<AudioAppStreamInfo>>([]);
        public Task SetAppStreamVolumeAsync(string id, double value, CancellationToken token = default) => Task.CompletedTask;
    }
}
