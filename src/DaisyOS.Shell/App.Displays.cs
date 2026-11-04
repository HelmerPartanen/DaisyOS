using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.Wayland;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Shell;
using DaisyOS.Shell.Views.Components.Wallpaper;

namespace DaisyOS.Shell;

public partial class App
{
    private string? _nativePrimaryOutputName;
    private readonly Dictionary<Window, string> _secondaryLockScreenOutputNames = new();
    private Window? _primaryWallpaperSurface;
    private readonly Dictionary<string, Window> _secondaryWallpaperSurfaces = new(StringComparer.Ordinal);
    private readonly DispatcherTimer _nativeDisplayTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private CancellationTokenSource? _nativeDisplayLifetime;
    private bool _nativeDisplayRefreshRunning;
    private string? _nativeDisplaysConfigurationKey;

    private void StartNativeDisplayTracking(Window wallpaper)
    {
        _primaryWallpaperSurface = wallpaper;
        _nativeDisplayLifetime = new CancellationTokenSource();
        _nativeDisplayTimer.Tick += OnNativeDisplayTick;
        _nativeDisplayTimer.Start();
        _ = RefreshNativeDisplaysAsync();
    }

    private async void OnNativeDisplayTick(object? sender, EventArgs e) => await RefreshNativeDisplaysAsync();

    internal bool IsOutputDesktopOccluded(string outputName) => _compositorWindows is KWinWindowBridge bridge
        ? bridge.IsCoveredOnOutput(outputName)
        : DesktopOcclusion.IsCovered(_compositorWindows?.Windows.Where(w => w.OutputName == outputName) ?? []);

    private async Task RefreshNativeDisplaysAsync()
    {
        if (_nativeDisplayRefreshRunning || _nativeDisplayLifetime is not { IsCancellationRequested: false } lifetime) return;
        _nativeDisplayRefreshRunning = true;
        try
        {
            var displays = await DisplaySettingsHelper.DetectAllDisplaysAsync(cancellationToken: lifetime.Token);
            if (lifetime.IsCancellationRequested || displays.Count == 0 || _layerShellBackend is null) return;
            var key = DisplaySettingsHelper.ConfigurationKey(displays);
            if (_nativeDisplaysConfigurationKey == key) return;
            var primary = displays.FirstOrDefault(d => d.IsPrimary) ?? displays[0];
            var desired = displays.Where(d => d.Name != primary.Name).Select(d => d.Name).ToHashSet(StringComparer.Ordinal);
            foreach (var output in _secondaryWallpaperSurfaces.Keys.Where(name => !desired.Contains(name)).ToArray())
            {
                var surface = _secondaryWallpaperSurfaces[output];
                _secondaryWallpaperSurfaces.Remove(output);
                _nativeShellSurfaces.Remove(surface);
                surface.Close();
            }
            if (_nativePrimaryOutputName != primary.Name)
            {
                _nativePrimaryOutputName = primary.Name;
                DismissAllTransientShellSurfaces();
                // One primary output owns all bottom chrome and its overlays.
                // Their logical windows survive native output migration.
                foreach (var surface in _nativeShellSurfaces.Where(w => !_secondaryWallpaperSurfaces.ContainsValue(w)).ToArray())
                    LayerShellWindow.SetOutputName(surface, primary.Name);
                if (_primaryWallpaperSurface?.Content is WallpaperLayer mainWallpaper) mainWallpaper.OutputName = primary.Name;
                if (_compositorWindows is KWinWindowBridge bridge && _shellView is { } shell)
                {
                    shell.SetExternalFullscreenActive(bridge.IsFullscreenOnOutput(primary.Name));
                    SetExternalDesktopOcclusion(bridge.IsCoveredOnOutput(primary.Name));
                    ApplyNativeBottomChromeState(shell);
                }
            }
            foreach (var output in desired.Where(name => !_secondaryWallpaperSurfaces.ContainsKey(name)))
            {
                var layer = new WallpaperLayer { OutputName = output };
                var surface = _layerShellBackend.CreateWindow(LayerShellSurfaceOptions.Desktop(output) with
                { Namespace = "daisyos.wallpaper", InputPassthrough = true }, layer);
                _secondaryWallpaperSurfaces.Add(output, surface);
                _nativeShellSurfaces.Add(surface);
                surface.Show();
            }
            ReconcileNativeLockScreens(primary.Name, desired);
            _nativeDisplaysConfigurationKey = key;
            DesktopOcclusionChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Console.Error.WriteLine($"[displays] Reconciliation failed: {ex.Message}"); }
        finally { _nativeDisplayRefreshRunning = false; }
    }

    private void ReconcileNativeLockScreens(string primary, HashSet<string> secondaryOutputs)
    {
        if (_lockScreenWindow is null || _layerShellBackend is null) return;
        LayerShellWindow.SetOutputName(_lockScreenWindow, primary);
        foreach (var pair in _secondaryLockScreenOutputNames.ToArray())
        {
            if (secondaryOutputs.Contains(pair.Value)) continue;
            _secondaryLockScreenOutputNames.Remove(pair.Key);
            _secondaryLockScreens.Remove(pair.Key);
            pair.Key.Close();
        }
        foreach (var output in secondaryOutputs.Where(name => !_secondaryLockScreenOutputNames.ContainsValue(name)))
        {
            var screen = Views.Windows.LockScreenWindow.CreateNative(_layerShellBackend,
                LayerShellSurfaceOptions.LockScreen(output, isSecondary: true), GetCurrentWallpaperSource(), isSecondary: true);
            _secondaryLockScreens.Add(screen);
            _secondaryLockScreenOutputNames.Add(screen, output);
            screen.Show();
        }
    }

    private void StopNativeDisplayTracking()
    {
        _nativeDisplayTimer.Stop();
        _nativeDisplayTimer.Tick -= OnNativeDisplayTick;
        _nativeDisplayLifetime?.Cancel();
        _nativeDisplayLifetime?.Dispose();
        _nativeDisplayLifetime = null;
        foreach (var surface in _secondaryWallpaperSurfaces.Values.ToArray()) surface.Close();
        _secondaryWallpaperSurfaces.Clear();
    }
}
