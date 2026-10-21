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
            var authenticationLifetime = new CancellationTokenSource();

            var appearance = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            appearance.Tick += (_, _) => { app.ReloadApplicationAppearance(); portal.UpdateAppearance(); };
            appearance.Start();
            desktop.Exit += (_, _) => { appearance.Stop(); authenticationLifetime.Cancel(); portal.Dispose(); };
            _ = StartAsync(portal, args.Contains("--authentication-agent", StringComparer.Ordinal), desktop, authenticationLifetime.Token);
        })).StartWithClassicDesktopLifetime(args);
    }

    private static async Task StartAsync(DesktopPortalService portal, bool authenticate, Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop, CancellationToken token)
    {
        try
        {
            // Authentication is independent of portal availability. Another valid
            // session agent must not cause file picking/capture to crash-loop.
            await portal.StartAsync();
            if (authenticate) _ = RunAuthenticationAsync(token);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[desktop-services] Startup failed: {ex.Message}");
            await Dispatcher.UIThread.InvokeAsync(() => desktop.Shutdown(1));
        }
    }

    private static async Task RunAuthenticationAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                using (var authentication = new AuthenticationAgent())
                {
                    var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                    authentication.Stopped += () => stopped.TrySetResult();
                    try
                    {
                        await authentication.StartAsync().WaitAsync(token).ConfigureAwait(false);
                        await stopped.Task.WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"[authentication] Agent unavailable; portals remain active. Retrying in 30 seconds: {ex.Message}");
                    }
                }
                // Retry only the small native helper, never the entire Avalonia
                // portal process. This also recovers if the other agent exits.
                await Task.Delay(TimeSpan.FromSeconds(30), token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }
}
