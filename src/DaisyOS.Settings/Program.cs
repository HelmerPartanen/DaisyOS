using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services;

namespace DaisyOS.Settings;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args) => StandaloneApplication.Run("settings", args,
        app => { app.UseDesktopSettings(); return new SettingsWindow(); },
        (window, activation) =>
        {
            if (activation.FirstOrDefault(arg => arg.StartsWith("--continuity-send=", StringComparison.Ordinal)) is { } send)
                ((SettingsWindow)window).PrepareContinuitySend(send["--continuity-send=".Length..]);
            if (SettingsDestination.FromArguments(activation) is { } destination)
                ((SettingsWindow)window).Navigate(destination);
        });
}
