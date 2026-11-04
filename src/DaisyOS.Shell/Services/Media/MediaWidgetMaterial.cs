using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DaisyOS.Shell.Services.Media;

/// <summary>Keeps artwork-tinted media surfaces opaque while retaining the theme's tint strength.</summary>
internal static class MediaWidgetMaterial
{
    public static IBrush CreateTint(Control target, Color? accent, float strength = ArtworkColorExtractor.DefaultTintOpacity)
    {
        if (accent is not { } color) return Brushes.Transparent;
        var baseColor = target.TryFindResource("SurfaceBaseSolidBrush", out var resource) && resource is ISolidColorBrush surface
            ? surface.Color
            : target.ActualThemeVariant == ThemeVariant.Light ? Color.FromRgb(242, 242, 242) : Color.FromRgb(23, 23, 23);
        return new SolidColorBrush(ArtworkColorExtractor.ApplyArtworkTint(baseColor, color, strength));
    }
}
