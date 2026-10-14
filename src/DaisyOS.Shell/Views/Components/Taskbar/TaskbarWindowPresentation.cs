using DaisyOS.Shell.Services.Windows;

namespace DaisyOS.Shell.Views.Components.Taskbar;
public readonly record struct TaskbarWindowPresentation(
    bool IsRunning,
    bool IsActive,
    bool IsMinimized,
    string? StateLabel)
{
    public static TaskbarWindowPresentation From(NativeAppWindowState state) => state switch
    {
        NativeAppWindowState.Active => new(true, true, false, "active"),
        NativeAppWindowState.Minimized => new(true, false, true, "minimized"),
        NativeAppWindowState.RunningInactive => new(true, false, false, "running"),
        // Covers NativeAppWindowState.NotRunning and, intentionally, any future
        // state not yet given explicit taskbar treatment: fail safe to "no window".
        _ => new(false, false, false, null)
    };
}