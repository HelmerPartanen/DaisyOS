using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;

namespace DaisyOS.Shell.Services;

/// <summary>Keeps fixed design-time app sizes useful on fractionally scaled laptop displays.</summary>
public static class NativeAppWindowSizing
{
    private const double WorkAreaUsage = 0.92;

    public static void Apply(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);
        var screen = window.Screens.ScreenFromWindow(window) ?? window.Screens.Primary ?? window.Screens.All.FirstOrDefault();
        if (screen is null) return;

        var scaling = Math.Max(1, screen.Scaling);
        var workArea = new Size(screen.WorkingArea.Width / scaling, screen.WorkingArea.Height / scaling);
        var size = Calculate(window.Width, window.Height, window.MinWidth, window.MinHeight, workArea, scaling);
        window.MinWidth = size.MinWidth;
        window.MinHeight = size.MinHeight;
        window.Width = size.Width;
        window.Height = size.Height;
    }

    internal static NativeAppWindowGeometry Calculate(
        double width, double height, double minWidth, double minHeight, Size logicalWorkArea, double scaling)
    {
        scaling = Math.Max(1, scaling);
        var maximumWidth = Math.Max(1, logicalWorkArea.Width * WorkAreaUsage);
        var maximumHeight = Math.Max(1, logicalWorkArea.Height * WorkAreaUsage);
        var effectiveMinWidth = Math.Min(minWidth, maximumWidth);
        var effectiveMinHeight = Math.Min(minHeight, maximumHeight);
        return new NativeAppWindowGeometry(
            Math.Clamp(width / scaling, effectiveMinWidth, maximumWidth),
            Math.Clamp(height / scaling, effectiveMinHeight, maximumHeight),
            effectiveMinWidth,
            effectiveMinHeight);
    }
}

internal readonly record struct NativeAppWindowGeometry(
    double Width, double Height, double MinWidth, double MinHeight);
