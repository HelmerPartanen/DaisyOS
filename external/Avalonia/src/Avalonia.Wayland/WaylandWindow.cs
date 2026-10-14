using System;
using Avalonia.Controls;

namespace Avalonia.Wayland;

/// <summary>Wayland-specific window metadata for normal xdg_toplevel windows.</summary>
public static class WaylandWindow
{
    /// <summary>
    /// Sets the xdg_toplevel app_id used by the compositor for window grouping
    /// and desktop-file/taskbar association. Call this before <see cref="Window.Show()"/>.
    /// Non-Wayland windows ignore the request.
    /// </summary>
    public static void SetAppId(Window window, string appId)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(appId);

        if (window.PlatformImpl is WindowImpl waylandWindow)
            waylandWindow.SetAppId(appId);
    }
}
