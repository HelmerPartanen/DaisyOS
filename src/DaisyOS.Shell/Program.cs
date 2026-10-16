using System;
using System.Linq;
using Avalonia;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Wayland;
using Avalonia.X11;

namespace DaisyOS.Shell;

internal static class Program
{
    private static readonly bool IsShellSession =
        string.Equals(Environment.GetEnvironmentVariable("DAISYOS_SHELL_SESSION"), "1", StringComparison.Ordinal)
        || Environment.GetCommandLineArgs().Contains("--shell-session", StringComparer.Ordinal);

    [STAThread]
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            var ex = e.ExceptionObject as Exception;
            Console.Error.WriteLine($"[daisy-fatal] Unhandled shell exception: {ex?.Message}\n{ex?.StackTrace}");
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Console.Error.WriteLine($"[daisy-warning] Unobserved background task exception: {e.Exception.Message}");
            e.SetObserved();
        };

        if (args.Contains("--desktop-services", StringComparer.Ordinal))
        {
            Services.DesktopIntegration.DesktopServicesApplication.Run(args);
            return;
        }

        if (args.Contains("--disk-manager", StringComparer.Ordinal))
        {
            Services.StandaloneApplication.Run("diskmanager", args,
                _ => new Apps.System.Disks.DiskManagerWindow(), (_, _) => { });
            return;
        }

        if (args.Contains("--verify-app-resources", StringComparer.Ordinal))
        {
            VerifyApplicationResources();
            return;
        }

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args.Where(arg => arg != "--shell-session").ToArray());
    }

    /// <summary>
    /// Validates compiled Avalonia XAML before the display-manager session
    /// launches KWin. A cancelled or competing build can otherwise leave a
    /// runnable apphost beside an incomplete UI-resource assembly.
    /// </summary>
    private static void VerifyApplicationResources()
    {
        AvaloniaLocator.CurrentMutable.Bind<ICursorFactory>().ToConstant(new DummyCursorFactory());
        var application = new App();
        application.Initialize();
    }

    private sealed class DummyCursorFactory : ICursorFactory
    {
        private sealed class DummyCursorImpl : ICursorImpl
        {
            public void Dispose() { }
        }

        public ICursorImpl GetCursor(StandardCursorType cursorType) => new DummyCursorImpl();
        public ICursorImpl CreateCursor(Bitmap cursor, PixelPoint hotSpot) => new DummyCursorImpl();
    }

    public static AppBuilder BuildAvaloniaApp(Func<App>? applicationFactory = null)
    {
        Services.Theming.LinuxTextLibraryIsolation.EnsureLoaded();
        var builder = AppBuilder.Configure(applicationFactory ?? (() => new App()))
            .LogToTrace()
            .With(new FontManagerOptions
            {
                DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts#Inter",
                FontFamilyMappings = new Dictionary<string, FontFamily>
                {
                    ["Inter"] = new FontFamily("avares://DaisyOS.Shell/Assets/fonts#Inter")
                }
            })
            // Configure Skia GPU resource cache dynamically based on installed RAM
            // On <= 4GB RAM systems, 96MB avoids competing with integrated VRAM
            // On >= 16GB systems, 256MB allows caching multiple 4K surfaces
            .With(new SkiaOptions
            {
                MaxGpuResourceSizeBytes = GetAdaptiveSkiaGpuResourceSizeBytes()
            });

        if (IsShellSession)
        {
            // The display-manager session must be a native Wayland client. Do
            // not let platform auto-detection silently put the shell back on
            // XWayland when a Wayland-session prerequisite is missing.
            // KWin's dmabuf import path is not yet stable enough for the shell
            // on every GPU. The WSI/EGL path is slightly less aggressive but
            // keeps the session alive instead of aborting during first render.
            return builder
                .UseSkia()
                .UseHarfBuzz()
                .With(new WaylandPlatformOptions { UseDmabufSwapchain = false })
                .UseWayland();
        }

        // On Linux under a Wayland compositor (GNOME, KDE Plasma, Sway, etc.), prefer native Wayland
        // directly over XWayland to eliminate 1-2 frames of display latency and compositor round-trips.
        if (OperatingSystem.IsLinux()
            && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"))
            && !string.Equals(Environment.GetEnvironmentVariable("DAISYOS_FORCE_X11"), "1", StringComparison.Ordinal))
        {
            return builder
                .UseSkia()
                .UseHarfBuzz()
                .With(new WaylandPlatformOptions { UseDmabufSwapchain = false })
                .UseWayland();
        }

        builder.UsePlatformDetect();
        var x11Options = new X11PlatformOptions
        {
            OverlayPopups = true
        };

        if (string.Equals(Environment.GetEnvironmentVariable("DAISYOS_SOFTWARE_RENDERING"), "1",
                StringComparison.Ordinal))
        {
            x11Options.RenderingMode = [X11RenderingMode.Software];
        }

        return builder.With(x11Options);
    }

    private static long GetAdaptiveSkiaGpuResourceSizeBytes()
    {
        if (long.TryParse(Environment.GetEnvironmentVariable("DAISYOS_SKIA_GPU_CACHE_MB"), out var overrideMb) && overrideMb > 0)
        {
            return overrideMb * 1024 * 1024;
        }

        try
        {
            var memoryInfo = GC.GetGCMemoryInfo();
            long totalPhysicalBytes = memoryInfo.TotalAvailableMemoryBytes;
            if (totalPhysicalBytes > 0)
            {
                // <= 4GB RAM -> 96MB
                if (totalPhysicalBytes <= 4L * 1024 * 1024 * 1024)
                {
                    return 96L * 1024 * 1024;
                }

                // <= 8GB RAM -> 160MB
                if (totalPhysicalBytes <= 8L * 1024 * 1024 * 1024)
                {
                    return 160L * 1024 * 1024;
                }
            }
        }
        catch
        {
            // Default fallback
        }

        // Default 256MB for 16GB+ systems
        return 256L * 1024 * 1024;
    }
}
