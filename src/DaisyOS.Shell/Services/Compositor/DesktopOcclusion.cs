namespace DaisyOS.Shell.Services.Compositor;

internal static class DesktopOcclusion
{
    public static bool IsCovered(IEnumerable<CompositorWindowRecord> windows) =>
        windows.Any(window => window.IsOnCurrentWorkspace && !window.IsMinimized
            && (window.IsFullScreen || window.IsMaximized));

    public static bool HasFullscreen(IEnumerable<CompositorWindowRecord> windows) =>
        windows.Any(window => window.IsOnCurrentWorkspace && !window.IsMinimized && window.IsFullScreen);
}
