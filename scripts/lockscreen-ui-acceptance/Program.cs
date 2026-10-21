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
using DaisyOS.Core.Models;
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
var canvasIndex = Array.IndexOf(args, "--canvas");
if (canvasIndex >= 0) PreviewApp.Canvas = Path.GetFullPath(args[canvasIndex + 1]);
LinuxTextLibraryIsolation.EnsureLoaded();
AppBuilder.Configure<PreviewApp>().UseSkia().UseHarfBuzz()
    .With(new WaylandPlatformOptions { UseDmabufSwapchain = false }).UseWayland()
    .StartWithClassicDesktopLifetime(args);

public sealed class PreviewApp : Application
{
    public static string Wallpaper = "", Output = "";
    public static bool Capture;
    public static string? Canvas;
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
            }, Wallpaper, lockService: new PreviewAuthentication(), initialWallpaper: layer,
                mediaService: Canvas is null ? null : new PreviewMedia());
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
            if (Canvas is not null) await CheckCanvasAsync(preview);
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

    private static async Task CheckCanvasAsync(LockScreenWindow preview)
    {
        await preview.RefreshMediaAsync();
        await preview.SetMediaExpandedAsync(true);
        await Task.Delay(350); // Deliberately reproduce a slow Canvas arriving after artwork.
        var fallback = preview.FindControl<Panel>("LockCanvasFallbackPanel")!;
        var wallpaper = preview.FindControl<WallpaperLayer>("LockWallpaper")!;
        var video = preview.FindControl<WallpaperLayer>("LockCanvasWallpaper")!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(LockScreenWindow).GetField("_resolvedCanvasVideoPath", flags)!.SetValue(preview, Canvas);
        typeof(LockScreenWindow).GetField("_canvasVideoAttached", flags)!.SetValue(preview, true);
        typeof(LockScreenWindow).GetMethod("ApplyVisualState", flags)!.Invoke(preview, null);
        video.SetMediaVideo(Canvas!);
        var samples = new List<string> { "elapsedMs,state,native,artworkOpacity,wallpaperOpacity" };
        var watch = Stopwatch.StartNew();
        var previous = 1d;
        var ready = false;
        while (watch.Elapsed < TimeSpan.FromSeconds(12))
        {
            await Task.Delay(16);
            if (video.PresentationState == WallpaperLayer.WallpaperPresentationState.Failed)
                throw new Exception("Native Canvas failed to load.");
            samples.Add($"{watch.ElapsedMilliseconds},{video.PresentationState},{video.UsesNativeSubsurface},{fallback.Opacity:F4},{wallpaper.Opacity:F4}");
            if (fallback.Opacity > previous + .001) throw new Exception("Artwork flashed back during Canvas reveal.");
            if (video.UsesNativeSubsurface && Math.Abs(fallback.Opacity - wallpaper.Opacity) > .001)
                throw new Exception("Canvas covers are not synchronized.");
            previous = fallback.Opacity;
            if (video.UsesNativeSubsurface && fallback.Opacity == 0) { ready = true; break; }
        }
        await File.WriteAllLinesAsync(Path.Combine(Output, "canvas-handoff.csv"), samples);
        if (!ready) throw new Exception("Native Canvas did not finish revealing.");
        await Task.Delay(500);
        await ScreenshotAsync("canvas-presented");
        await preview.SetMediaExpandedAsync(false);
        if (wallpaper.Opacity != 1) throw new Exception("Collapse did not restore the wallpaper.");
        Console.WriteLine("PASS: delayed native Canvas reveal is monotonic, covers stay synchronized, collapse restores wallpaper.");
    }

    private sealed class PreviewMedia : IMediaSessionService
    {
        public event EventHandler? MediaChanged { add { } remove { } }
        public Task<MediaSession?> GetCurrentSessionAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<MediaSession?>(new("Canvas transition preview", "DaisyOS", Wallpaper, 98, 180, true));
        public Task PreviousAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task PlayPauseAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NextAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
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
