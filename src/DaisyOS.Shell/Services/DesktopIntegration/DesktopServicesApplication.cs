using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal static class DesktopServicesApplication
{
    public static void Run(string[] args)
    {
        Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", null);
        Program.BuildAvaloniaApp(() => new App((app, desktop) =>
        {
            app.UseDesktopSettings();
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var portal = new DesktopPortalService();
            var authentication = new AuthenticationAgent();
            authentication.Stopped += () => desktop.Shutdown(1);

            var appearance = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            appearance.Tick += (_, _) => { app.ReloadApplicationAppearance(); portal.UpdateAppearance(); };
            appearance.Start();
            desktop.Exit += (_, _) => { appearance.Stop(); authentication.Dispose(); portal.Dispose(); };
            _ = StartAsync(portal, authentication, args.Contains("--authentication-agent", StringComparer.Ordinal), desktop);
        })).StartWithClassicDesktopLifetime(args);
    }

    private static async Task StartAsync(DesktopPortalService portal, AuthenticationAgent authentication, bool authenticate, Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
    {
        try { if (authenticate) await authentication.StartAsync(); await portal.StartAsync(); }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[desktop-services] Startup failed: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(1));
        }
    }
}
