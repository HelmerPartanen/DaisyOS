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
            if (activation.FirstOrDefault(arg => arg.StartsWith("--category=", StringComparison.Ordinal)) is { } category)
                ((SettingsWindow)window).SelectCategory(category["--category=".Length..]);
        });
}
