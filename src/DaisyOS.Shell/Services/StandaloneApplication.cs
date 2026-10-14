using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Wayland;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services;

/// <summary>Runs a normal application with its own UI thread and single-instance activation.</summary>
public static class StandaloneApplication
{
    public static int Run(string id, string[] args, Func<App, Window> create, Action<Window, string[]> activate)
    {
        Theming.LinuxTextLibraryIsolation.EnsureLoaded();
        Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", null);
        var name = ApplicationPipe.Name(id);
        using var ownership = new Mutex(false, "Global\\" + name);
        bool acquired;
        try { acquired = ownership.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try
            {
                var response = ApplicationPipe.RequestAsync(name, args).GetAwaiter().GetResult();
                if (response == "ok") return 0;
                Console.Error.WriteLine(response);
            }
            catch (Exception ex) { Console.Error.WriteLine($"[{id}] Couldn’t activate application: {ex.Message}"); }
            return 1;
        }
        try
        {
            Program.BuildAvaloniaApp(() => new App((app, desktop) =>
            {
                var window = ClientSideDecorationsWindow.Create(() => create(app));
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                    WaylandWindow.SetAppId(window, Diagnostics.ApplicationPerformanceProbe.AppId(id));
                Diagnostics.ApplicationPerformanceProbe.Attach(window, id);
                desktop.MainWindow = window;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                window.Opened += (_, _) => activate(window, args);
                var stop = new CancellationTokenSource();
                _ = ApplicationPipe.ServeAsync(name, async request =>
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        activate(window, request);
                        if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                        window.Activate();
                    });
                    await RequestShellAsync("activate-app:" + id);
                    return "ok";
                }, stop.Token);
                app.Feedback.MessageShown += async (_, message) =>
                {
                    if (!await RequestShellAsync("app-feedback:" + message))
                    {
                        Console.Error.WriteLine($"[{id}] {message}");
                        if (window.IsVisible)
                            await new Views.Dialogs.ConfirmationDialog(window.Title ?? id,
                                Loc.Get("Desktop settings"), message, Loc.Get("OK"), Loc.Get("Close")).ShowDialog<bool>(window);
                    }
                };
                var appearance = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                appearance.Tick += (_, _) => { app.ReloadApplicationAppearance(); app.SessionState.Reload(); };
                appearance.Start();
                var appearanceObserver = new Theming.AppearanceFileObserver(app.AppearancePreferencesPath,
                    () => app.ReloadApplicationAppearance(force: true));
                desktop.Exit += (_, _) => { appearanceObserver.Dispose(); appearance.Stop(); stop.Cancel(); };
            })).StartWithClassicDesktopLifetime(args);
            return 0;
        }
        finally { ownership.ReleaseMutex(); }
    }

    private static async Task<bool> RequestShellAsync(string action)
    {
        try
        {
            using var connection = new DBusConnection(DBusAddress.Session ?? throw new IOException("No session bus."));
            await connection.ConnectAsync();
            MessageBuffer message;
            using (var writer = connection.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: "org.daisyos.Shell.WindowBridge",
                    path: "/org/daisyos/Shell/WindowBridge", @interface: "org.daisyos.Shell.WindowBridge",
                    member: "RequestShellAction", signature: "s");
                writer.WriteString(action);
                message = writer.CreateMessage();
            }
            await connection.CallMethodAsync(message).WaitAsync(TimeSpan.FromSeconds(3));
            return true;
        }
        catch (Exception ex) { Console.Error.WriteLine($"[application] Desktop request failed: {ex.Message}"); return false; }
    }
}
