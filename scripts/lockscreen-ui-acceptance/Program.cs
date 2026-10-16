using System.Diagnostics;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.Wayland;
using DaisyOS.Core.Services;
using DaisyOS.Shell;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Services.Theming;
using DaisyOS.Shell.Views.Components.Wallpaper;
using DaisyOS.Shell.Views.Windows;

// A short, self-dismissing native preview. It never locks the session or uses PAM.
if (args.Length < 2) throw new ArgumentException("Usage: wallpaper.heic output-directory [--capture]");
PreviewApp.Wallpaper = Path.GetFullPath(args[0]);
PreviewApp.Output = Path.GetFullPath(args[1]);
PreviewApp.Capture = args.Contains("--capture");
LinuxTextLibraryIsolation.EnsureLoaded();
AppBuilder.Configure<PreviewApp>().UseSkia().UseHarfBuzz()
    .With(new WaylandPlatformOptions { UseDmabufSwapchain = false }).UseWayland()
    .StartWithClassicDesktopLifetime(args);

public sealed class PreviewApp : Application
{
    public static string Wallpaper = "", Output = "";
    public static bool Capture;
    public override void Initialize()
    {
        var shell = new App(); shell.Initialize();
        var resources = shell.Resources; shell.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
        RequestedThemeVariant = ThemeVariant.Light;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        var desktop = (IClassicDesktopStyleApplicationLifetime)ApplicationLifetime!;
        desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Dispatcher.UIThread.Post(async () =>
        {
            try { await RunAsync(); desktop.Shutdown(); }
            catch (Exception ex) { Console.Error.WriteLine(ex); desktop.Shutdown(1); }
        });
        base.OnFrameworkInitializationCompleted();
    }

    private async Task RunAsync()
    {
        Directory.CreateDirectory(Output);
        var backend = new AvaloniaLayerShellWindowBackend();
        var layer = new WallpaperLayer { WallpaperUri = Wallpaper };
        var background = backend.CreateWindow(LayerShellSurfaceOptions.Desktop() with
        {
            Role = LayerShellSurfaceRole.Overlay, Namespace = "daisyos.overlay.lock-preview-base", InputPassthrough = true
        }, layer);
        LockScreenWindow? preview = null;
        try
        {
            background.Show();
            await Task.Delay(300);
            if (!await layer.TryPresentWallpaperAsync(Wallpaper, TimeSpan.FromSeconds(20))) throw new Exception("Preview wallpaper did not load.");
            await Task.Delay(500);
            preview = LockScreenWindow.CreateNative(backend, LayerShellSurfaceOptions.LockScreen(null, false) with
            {
                KeyboardInteractivity = LayerShellKeyboardInteractivity.None, InputPassthrough = true
            }, Wallpaper, lockService: new PreviewAuthentication(), initialWallpaper: layer);
            var lockLayer = preview.FindControl<WallpaperLayer>("LockWallpaper")!;
            if (lockLayer.FindControl<Image>("WallpaperImage")!.Source is null) throw new Exception("Lock wallpaper was not seeded before mapping.");
            preview.FindControl<TextBlock>("UserNameText")!.Text = "DaisyOS Preview";
            preview.Show();
            var samples = new List<string> { "phase,elapsedMs,opacity,blurRects" };
            await SampleAsync(preview, samples, "enter", 500);
            await ScreenshotAsync("lock-light");
            RequestedThemeVariant = ThemeVariant.Dark;
            await SampleAsync(preview, samples, "theme", 900);
            await ScreenshotAsync("lock-dark");
            var closed = false;
            preview.Closed += (_, _) => closed = true;
            preview.FindControl<TextBox>("PasswordBox")!.Text = "preview";
            preview.FindControl<Button>("UnlockButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await SampleAsync(preview, samples, "unlock", 400);
            if (!closed) throw new Exception("Authenticated preview did not dismiss.");
            await File.WriteAllLinesAsync(Path.Combine(Output, "native-motion.csv"), samples);
            Console.WriteLine("PASS: seeded native entry, HEIC theme change, authenticated dismissal and published blur geometry.");
        }
        finally { preview?.Close(); background.Close(); }
    }

    private static async Task SampleAsync(LockScreenWindow preview, List<string> samples, string phase, int milliseconds)
    {
        var blur = (KWinBlur)typeof(LockScreenWindow).GetField("_nativeBlur", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(preview)!;
        var field = typeof(KWinBlur).GetField("_lastRegions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var watch = Stopwatch.StartNew();
        while (watch.ElapsedMilliseconds < milliseconds)
        {
            await Task.Delay(16);
            if (!preview.IsVisible) break;
            var published = (List<WaylandBlurRect>?)field.GetValue(blur) ?? [];
            if (!published.SequenceEqual(blur.BuildRegions())) throw new Exception("Native blur mask trails the current control geometry.");
            samples.Add($"{phase},{watch.ElapsedMilliseconds},{preview.FindControl<StackPanel>("CredentialsPanel")!.Opacity:F4},{published.Count}");
        }
    }

    private static async Task ScreenshotAsync(string name)
    {
        if (!Capture) return;
        var info = new ProcessStartInfo("spectacle") { UseShellExecute = false };
        foreach (var argument in new[] { "-b", "-n", "-f", "-o", Path.Combine(Output, name + ".png") }) info.ArgumentList.Add(argument);
        using var process = Process.Start(info)!;
        await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(15));
        if (process.ExitCode != 0) throw new Exception("Native screenshot failed.");
    }

    private sealed class PreviewAuthentication : ILockScreenService
    {
        public bool IsLocked => false;
        public event EventHandler<bool>? LockStateChanged { add { } remove { } }
        public void Lock() { }
        public void Unlock() { }
        public Task<bool> AuthenticateAsync(string password, CancellationToken cancellationToken = default) => Task.FromResult(password == "preview");
    }
}
