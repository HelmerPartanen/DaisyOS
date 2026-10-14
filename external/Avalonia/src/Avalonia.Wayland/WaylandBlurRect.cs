namespace Avalonia.Wayland;

/// <summary>A surface-local compositor blur rectangle in logical pixels.</summary>
public readonly record struct WaylandBlurRect(int X, int Y, int Width, int Height);
