using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Views.Components.Volume;

public partial class VolumeIndicator : UserControl
{
    private bool _reportedGeometry;
    private readonly DispatcherTimer _dismiss = new() { Interval = TimeSpan.FromMilliseconds(1500) };
    public Border MaterialSurface => this.FindControl<Border>("MaterialFrame")!;

    public VolumeIndicator()
    {
        AvaloniaXamlLoader.Load(this);
        _dismiss.Tick += (_, _) => { _dismiss.Stop(); _ = DismissAsync(); };
        DetachedFromVisualTree += (_, _) => _dismiss.Stop();
    }

    public void ShowLevel(double volume, bool muted, bool microphone = false)
    {
        var level = this.FindControl<QuickSettingsSlider>("Level")!;
        level.Value = muted ? 0 : Math.Clamp(volume, 0, 100);
        level.Icon = microphone ? (muted ? "microphone-slash" : "microphone")
            : muted || volume == 0 ? "speaker-slash" : volume < 50 ? "speaker-low" : "speaker-high";
        this.FindControl<TextBlock>("Percentage")!.Text = $"{Math.Round(volume):0}%";
        // Repeated keys refresh the timeout without restarting an active entrance.
        _dismiss.Stop();
        _ = GuiMotion.SetVisibleAsync(this, MaterialSurface, true, offset: 160, fadeOnEnter: false);
        IsHitTestVisible = false;
        _dismiss.Start();
        if (!_reportedGeometry)
        {
            _reportedGeometry = true;
            DispatcherTimer.RunOnce(() => global::System.Console.Error.WriteLine(
                $"[volume-indicator] visible={IsVisible} bounds={Bounds} material={MaterialSurface.Bounds} opacity={MaterialSurface.Opacity:F2}"),
                TimeSpan.FromMilliseconds(300));
        }
    }

    public Task DismissAsync() => GuiMotion.SetVisibleAsync(this, MaterialSurface, false, offset: 160, fadeOnExit: false);
}
