using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Shell.Services.Shell;

namespace DaisyOS.Shell.Services.DesktopIntegration;

/// <summary>A visible stop control which never receives the shared keyboard focus.</summary>
internal static class PortalSharingIndicator
{
    private static readonly bool[] Corners = new bool[4];
    public static Window Create(string title, string detail, Action stop)
    {
        int corner;
        lock (Corners) { corner = Array.FindIndex(Corners, used => !used); if (corner < 0) corner = 0; Corners[corner] = true; }
        var anchor = corner switch
        {
            1 => LayerShellAnchor.Top | LayerShellAnchor.Left,
            2 => LayerShellAnchor.Bottom | LayerShellAnchor.Right,
            3 => LayerShellAnchor.Bottom | LayerShellAnchor.Left,
            _ => LayerShellAnchor.Top | LayerShellAnchor.Right
        };
        Window Factory() => new()
        {
            Title = title, Width = 340, Height = 64, CanResize = false, CanMinimize = false, CanMaximize = false,
            ShowActivated = false, ShowInTaskbar = false, Topmost = true, Background = Brushes.Transparent, WindowDecorations = WindowDecorations.None
        };
        var window = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            ? new AvaloniaLayerShellWindowBackend().CreateWindow(new LayerShellSurfaceOptions(
                Role: LayerShellSurfaceRole.Overlay, Namespace: "daisyos-sharing-indicator-" + corner,
                Anchor: anchor, ExclusiveZone: 0, KeyboardInteractivity: LayerShellKeyboardInteractivity.None,
                Width: 340, Height: 64), Factory)
            : Factory();
        window.Closed += (_, _) => { lock (Corners) Corners[corner] = false; };
        var button = new Button { Content = Loc.Get("Stop sharing"), Classes = { "Primary", "Small" }, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => stop();
        var label = new TextBlock { Text = Loc.Get(title), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        ToolTip.SetTip(label, detail);
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 12, Children = { label, button } };
        Grid.SetColumn(button, 1);
        var frame = new Border { Padding = new Thickness(12), CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), Child = content };
        frame.Bind(Border.BackgroundProperty, window.GetResourceObservable("SurfaceBaseSolidBrush"));
        frame.Bind(Border.BorderBrushProperty, window.GetResourceObservable("StrokeDefaultBrush"));
        window.Content = frame;
        return window;
    }
}
