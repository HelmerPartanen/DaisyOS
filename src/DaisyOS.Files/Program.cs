using System.IO.Pipes;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Wayland;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;

namespace DaisyOS.Files;

internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        DaisyOS.Shell.Services.Theming.LinuxTextLibraryIsolation.EnsureLoaded();
        // An app launched by the shell inherits its environment, but is an
        // ordinary toplevel client rather than another layer-shell desktop.
        Environment.SetEnvironmentVariable("DAISYOS_SHELL_SESSION", null);
        var request = ParseActivation(args);
        var name = DaisyOS.Shell.Services.ApplicationPipe.Name("files");
        using var ownership = new Mutex(false, "Global\\" + name);
        bool acquired;
        try { acquired = ownership.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired)
        {
            try { ForwardActivationAsync(name, request).GetAwaiter().GetResult(); return 0; }
            catch (Exception ex) when (ex is IOException or TimeoutException or OperationCanceledException)
            { Console.Error.WriteLine($"[files] Couldn’t activate Files: {ex.Message}"); return 1; }
        }
        try
        {
            DaisyOS.Shell.Program.BuildAvaloniaApp(() => new App((app, desktop) =>
            {
                var window = ClientSideDecorationsWindow.Create(() => new FilesWindow(request.Paths.FirstOrDefault(), request.SelectItems));
                DaisyOS.Shell.Services.NativeAppWindowSizing.Apply(window);
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
                    WaylandWindow.SetAppId(window, DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.AppId("files"));
                DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.Attach(window, "files");
                desktop.MainWindow = window;
                desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;
                var stop = new CancellationTokenSource();
                var fileManager = new FileManagerActivationService();
                void Activate(FileManagerActivationRequest activation)
                {
                    foreach (var path in activation.Paths) window.Reveal(path, activation.SelectItems);
                    if (window.WindowState == WindowState.Minimized) window.WindowState = WindowState.Normal;
                    window.Activate();
                    _ = ShellClient.ActivateAsync();
                }
                fileManager.ActivationRequested += (_, activation) => Dispatcher.UIThread.Post(() => Activate(activation));
                if (!DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.Enabled) _ = fileManager.StartAsync(stop.Token);
                _ = ReceiveActivationsAsync(name, activation => Dispatcher.UIThread.Post(() => Activate(activation)), stop.Token);
                app.ShareRequested = async paths =>
                {
                    if (!await ShellClient.ShareAsync(paths ?? []))
                        app.Feedback.Show(DaisyOS.Shell.Services.Localization.Loc.Get("LocalSend requires the DaisyOS desktop to be running."));
                };
                app.Feedback.MessageShown += async (_, message) =>
                {
                    if (!await ShellClient.ShowFeedbackAsync(message))
                        Dispatcher.UIThread.Post(() => window.ReportFeedback(message));
                };
                var appearance = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                appearance.Tick += (_, _) => app.ReloadApplicationAppearance();
                appearance.Start();
                var appearanceObserver = new DaisyOS.Shell.Services.Theming.AppearanceFileObserver(
                    app.AppearancePreferencesPath, () => app.ReloadApplicationAppearance(force: true));
                desktop.Exit += (_, _) =>
                {
                    appearanceObserver.Dispose();
                    appearance.Stop();
                    stop.Cancel();
                    _ = fileManager.DisposeAsync();
                };
            })).StartWithClassicDesktopLifetime(args);
            return 0;
        }
        finally { ownership.ReleaseMutex(); }
    }

    internal static FileManagerActivationRequest ParseActivation(IEnumerable<string> args) => new(
        args.Where(arg => arg != "--select")
            .Select(FileManagerPath.GetLocalPathOrNull).Where(path => path is not null).Cast<string>().ToArray(),
        args.Contains("--select"));

    private static async Task ForwardActivationAsync(string name, FileManagerActivationRequest request)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var pipe = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        await pipe.ConnectAsync(timeout.Token);
        using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipe, leaveOpen: true);
        await writer.WriteLineAsync(JsonSerializer.Serialize(request));
        if (await reader.ReadLineAsync(timeout.Token) != "ok") throw new IOException("Files did not accept the activation request.");
    }

    private static async Task ReceiveActivationsAsync(string name, Action<FileManagerActivationRequest> activate, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var pipe = new NamedPipeServerStream(name, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                using var reader = new StreamReader(pipe, leaveOpen: true);
                using var writer = new StreamWriter(pipe, leaveOpen: true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(token);
                if (line is not null && JsonSerializer.Deserialize<FileManagerActivationRequest>(line) is { } request)
                {
                    activate(request);
                    await writer.WriteLineAsync("ok");
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (Exception ex) when (ex is IOException or JsonException)
            { Console.Error.WriteLine($"[files] Activation failed: {ex.Message}"); }
        }
    }
}
