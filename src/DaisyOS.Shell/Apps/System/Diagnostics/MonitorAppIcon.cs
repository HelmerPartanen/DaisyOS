using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Theming;

namespace DaisyOS.Shell.Apps.System.Diagnostics;

/// <summary>One stable icon cell for apps, processes, windows and resource contributors.</summary>
public sealed class MonitorAppIcon : Grid
{
    public static readonly StyledProperty<IImage?> SourceProperty = AvaloniaProperty.Register<MonitorAppIcon, IImage?>(nameof(Source));
    public static readonly StyledProperty<string> FallbackProperty = AvaloniaProperty.Register<MonitorAppIcon, string>(nameof(Fallback), "cpu");
    private readonly Image _image = new() { Stretch = Stretch.Uniform, Margin = new Thickness(3) };
    private readonly DaisyIcon _fallback = new() { Size = 20, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
    public IImage? Source { get => GetValue(SourceProperty); set => SetValue(SourceProperty, value); }
    public string Fallback { get => GetValue(FallbackProperty); set => SetValue(FallbackProperty, value); }
    public MonitorAppIcon()
    {
        Width = Height = 30;
        Children.Add(_image); Children.Add(_fallback);
        ThemeTransitionPolicy.SetEnabled(_image, false);
        _fallback.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        Update();
    }
    static MonitorAppIcon()
    {
        SourceProperty.Changed.AddClassHandler<MonitorAppIcon>((icon, _) => icon.Update());
        FallbackProperty.Changed.AddClassHandler<MonitorAppIcon>((icon, _) => icon.Update());
    }
    private void Update() { _image.Source = Source; _image.IsVisible = Source is not null; _fallback.IsVisible = Source is null; _fallback.Icon = Fallback; }
}
