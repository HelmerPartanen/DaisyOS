using Avalonia.Threading;
using DaisyOS.Core.Search;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.Processes;
using DaisyOS.System.Search;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.System.Audio;
using DaisyOS.System.Bluetooth;
using DaisyOS.System.Networking;

namespace DaisyOS.Shell.Services;

/// <summary>Shell process service; launcher views are consumers, never service owners.</summary>
public sealed class DaisySearchService : IAsyncDisposable
{
    private readonly LinuxAppLauncherService _launcher = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task? _hardwarePolling;
    private readonly SemaphoreSlim _catalogUpdates = new(1, 1);
    private AppEntry[] _catalog = [];
    public ApplicationSearchProvider Applications { get; }
    public IReadOnlyList<AppEntry> Catalog => Volatile.Read(ref _catalog);
    public Task ApplicationsReady { get; }
    public event EventHandler? CatalogChanged;
    public event EventHandler? ResultsChanged;
    private int _invalidationQueued;
    private int _disposed;
    public SearchSettingsRegistry Settings { get; } = new();
    public SearchCoordinator Coordinator { get; }
    public FileSearchService? Files { get; }

    internal DaisySearchService(ApplicationSearchProvider applications, SearchSettingsRegistry settings, IEnumerable<ISearchProvider>? additionalProviders = null)
    {
        Applications = applications;
        Settings = settings;
        Coordinator = new(new ISearchProvider[] { applications, new SettingsSearchProvider(settings) }.Concat(additionalProviders ?? []));
        ApplicationsReady = Task.CompletedTask;
    }

    public DaisySearchService(App app)
    {
        Applications = new ApplicationSearchProvider(async (id, token) =>
        {
            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                token.ThrowIfCancellationRequested();
                var entry = Catalog.SingleOrDefault(a => a.Id == id)
                    ?? throw new InvalidOperationException("This application is unavailable.");
                if (app.TryLaunchSystemApp(id) || await app.TryActivateExternalApplicationAsync(id))
                { app.SessionState.RecordApplicationLaunch(id); return; }
                var result = await _launcher.LaunchAsync(entry, token);
                if (!result.Succeeded) throw new InvalidOperationException("The application could not be opened.");
                app.SessionState.RecordApplicationLaunch(id);
            });
        }, async (id, action, token) =>
        {
            var result = DaisyLaunchClient.Enabled
                ? await DaisyLaunchClient.Shared.LaunchAsync(id, action, token: token)
                : await DaisyLaunchClient.FallbackAsync(id, action, token: token);
            if (!result.Succeeded) throw new InvalidOperationException("The application action could not be completed.");
        });
        Applications.UsageBoost = id =>
        {
            var (count, last) = app.SessionState.GetApplicationUsage(id);
            if (count <= 0) return 0;
            var age = (DateTimeOffset.UtcNow - last).TotalHours;
            return Math.Min(45, count * 5d) + (age <= 24 ? 20 : age <= 168 ? 10 : 0);
        };
        Register("accessibility.high-contrast", "High contrast", "Accessibility", ["contrast", "readability", "appearance"],
            () => app.HighContrastEnabled, app.SetHighContrastAsync);
        Register("accessibility.reduced-motion", "Reduced motion", "Accessibility", ["animation", "animations", "motion"],
            () => app.ReduceMotionEnabled, app.SetReduceMotionAsync);
        Register("appearance.panel-borders", "Panel borders", "Appearance", ["panel", "borders", "outline"],
            () => !app.PanelBordersDisabled, enabled => app.SetDisablePanelBordersAsync(!enabled));
        Register("desktop.auto-hide", "Auto-hide taskbar", "Taskbar", ["dock", "panel", "hide", "taskbar"],
            () => app.TaskbarAutoHideEnabled, app.SetTaskbarAutoHideAsync);
        Register("notifications.do-not-disturb", "Do not disturb", "Notifications", ["notifications", "quiet", "focus"],
            () => app.SessionState.DoNotDisturb, enabled => { app.SetDoNotDisturb(enabled); return Task.CompletedTask; });
        Settings.RegisterChoice(new("appearance.theme", "Theme", "Appearance", ["theme", "light", "dark", "automatic", "dark mode", "night appearance"],
            Enum.GetNames<ThemeMode>(),
            async token => { token.ThrowIfCancellationRequested(); return await Dispatcher.UIThread.InvokeAsync(() => app.CurrentThemeMode.ToString()); },
            async (value, token) =>
            { token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => app.SetThemeModeAsync(Enum.Parse<ThemeMode>(value))); }));
        Settings.RegisterChoice(new("appearance.accent", "Accent color", "Appearance", ["accent", "color", "colour"],
            Enum.GetNames<AccentColor>(),
            async token => { token.ThrowIfCancellationRequested(); return await Dispatcher.UIThread.InvokeAsync(() => app.CurrentAccentColor.ToString()); },
            async (value, token) =>
            { token.ThrowIfCancellationRequested(); await Dispatcher.UIThread.InvokeAsync(() => app.SetAccentColorAsync(Enum.Parse<AccentColor>(value))); }));
        var runner = new SafeCommandRunner();
        Files = new FileSearchService(async (path, reveal, token) =>
        {
            var target = reveal ? Path.GetDirectoryName(path)! : path;
            var result = await runner.RunAsync("xdg-open", [target], TimeSpan.FromSeconds(10), token);
            if (!result.Succeeded) throw new IOException("This item could not be opened.");
        });
        Files.Changed += (_, _) => InvalidateResults();
        app.CompositorWindowsChanged += (_, _) => InvalidateResults();
        var actions = new RegisteredActionSearchProvider([
            new("settings", "Open Settings", "DaisyOS", ["preferences", "system settings"], _ => RunOnUiAsync(() => app.ShowSettings())),
            new("display", "Display settings", "DaisyOS", ["resolution", "scaling", "monitor"], _ => RunOnUiAsync(() => app.ShowSettings("Displays"))),
            new("lock", "Lock screen", "Session", ["lock", "privacy"], _ => RunOnUiAsync(app.LockSession)),
            new("screenshot", "Take screenshot", "DaisyOS", ["capture", "screenshot"], _ => Dispatcher.UIThread.InvokeAsync(() => app.ShowScreenshotToolAsync())),
            new("terminal", "Open terminal", "Applications", ["console", "command line"], token => Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                if (!TerminalLauncher.OpenTerminal()) throw new InvalidOperationException("No terminal is available.");
            }, token))]);
        var pageEntries = SettingsView.SearchCatalog.Select(entry =>
            new SearchableSettingsPage(entry.Category + ":" + entry.Section + ":" + entry.Title,
                entry.Title, entry.Category, entry.Section, entry.Description, entry.Keywords.Split(' '))
            {
                InlineSettingId = entry.Title switch
                {
                    "Theme" => "appearance.theme", "Accent color" => "appearance.accent",
                    "High contrast" => "accessibility.high-contrast", "Reduce motion" => "accessibility.reduced-motion",
                    "Do not disturb" => "notifications.do-not-disturb", "Panel borders" => "appearance.panel-borders",
                    "Wi-Fi" => "network.wifi", "Bluetooth" => "network.bluetooth", "Output volume" => "audio.mute",
                    "Auto-hide taskbar components" => "desktop.auto-hide", _ => null
                },
                HasAdditionalTasks = entry.Title is "Wi-Fi" or "Bluetooth" or "Output volume" or "Auto-hide taskbar components",
                SearchTitle = entry.Title switch { "Wi-Fi" => "Wi-Fi networks", "Bluetooth" => "Bluetooth devices", _ => null }
            }).ToArray();
        var pages = new SettingsPageSearchProvider(pageEntries,
            (page, _) => RunOnUiAsync(() => app.ShowSettings(page.Category, page.Section, page.Title)), Settings);
        Coordinator = new([Applications, new SettingsSearchProvider(Settings, pageEntries), pages, Files, new RecentSearchProvider(Files), new CalculatorSearchProvider(), actions,
            new SearchKWinProvider(() => app.SearchCompositor)]);
        DaisyLaunchClient.Shared.CatalogChanged += () => _ = RefreshCatalogAsync();
        ApplicationsReady = RefreshCatalogAsync();
        _hardwarePolling = Task.Run(() => MonitorHardwareAsync(_lifetime.Token));
        app.AppearanceChanged += (_, _) =>
        {
            foreach (var setting in Settings.Settings) Settings.NotifyExternalChange(setting.Id);
        };
    }

    private void InvalidateResults()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        if (Interlocked.Exchange(ref _invalidationQueued, 1) == 0) _ = PublishInvalidationAsync();
    }
    private async Task PublishInvalidationAsync()
    {
        try
        {
            await Task.Delay(1500, _lifetime.Token).ConfigureAwait(false);
            Dispatcher.UIThread.Post(() =>
            {
                Interlocked.Exchange(ref _invalidationQueued, 0);
                if (!_lifetime.IsCancellationRequested) ResultsChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (OperationCanceledException) { Interlocked.Exchange(ref _invalidationQueued, 0); }
    }

    private static async Task RunOnUiAsync(Action action) => await Dispatcher.UIThread.InvokeAsync(action);
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _lifetime.Cancel();
        if (_hardwarePolling is not null) { try { await _hardwarePolling; } catch (OperationCanceledException) { } }
        if (Files is not null) await Files.DisposeAsync();
        _lifetime.Dispose();
    }

    private async Task MonitorHardwareAsync(CancellationToken token)
    {
        var runner = new SafeCommandRunner();
        var bluetooth = new LinuxBluetoothService(runner);
        var wireless = new LinuxWirelessNetworkService(runner);
        var audio = new LinuxAudioService(runner);
        async Task ProbeAsync(string id, Func<CancellationToken, Task<SearchableBooleanSetting?>> probe)
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
            deadline.CancelAfter(TimeSpan.FromSeconds(3));
            SearchableBooleanSetting? definition = null;
            try { definition = await probe(deadline.Token).WaitAsync(deadline.Token); }
            catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
            catch (Exception) when (!token.IsCancellationRequested) { }
            token.ThrowIfCancellationRequested();
            var present = Settings.Settings.Any(s => s.Id == id);
            if (definition is not null && !present) Settings.Register(definition);
            else if (definition is null && present) Settings.Unregister(id);
            else if (present) Settings.NotifyExternalChange(id);
            if (present != (definition is not null)) Dispatcher.UIThread.Post(() => CatalogChanged?.Invoke(this, EventArgs.Empty));
        }
        while (!token.IsCancellationRequested)
        {
            await Task.WhenAll(
                ProbeAsync("network.bluetooth", async ct =>
                {
                    if (!(await bluetooth.GetAdapterStatusAsync(ct)).IsAvailable) return null;
                    return new("network.bluetooth", "Bluetooth", "Connections", ["bluetooth", "wireless", "devices"],
                        async cancellation =>
                        {
                            var state = await bluetooth.GetAdapterStatusAsync(cancellation);
                            if (!state.IsAvailable) throw new InvalidOperationException("Bluetooth is unavailable.");
                            return state.IsEnabled;
                        }, async (enabled, cancellation) =>
                        { if (!await bluetooth.SetEnabledAsync(enabled, cancellation)) throw new InvalidOperationException("Bluetooth rejected the update."); });
                }),
                ProbeAsync("network.wifi", async ct =>
                {
                    if (!(await wireless.GetRadioStatusAsync(ct)).IsAvailable) return null;
                    return new("network.wifi", "Wi-Fi", "Connections", ["wifi", "wireless", "network", "internet"],
                        async cancellation =>
                        {
                            var state = await wireless.GetRadioStatusAsync(cancellation);
                            if (!state.IsAvailable) throw new InvalidOperationException("Wi-Fi is unavailable.");
                            return state.IsEnabled;
                        }, async (enabled, cancellation) =>
                        { if (!await wireless.SetEnabledAsync(enabled, cancellation)) throw new InvalidOperationException("Wi-Fi rejected the update."); });
                }),
                ProbeAsync("audio.mute", async ct => await audio.GetVolumeAsync(ct) is null ? null :
                    new("audio.mute", "Mute audio", "Sound", ["mute", "audio", "sound", "speaker"], audio.GetMutedAsync, audio.SetMutedAsync)));
            await Task.Delay(TimeSpan.FromSeconds(15), token);
        }
    }

    public async Task RefreshCatalogAsync()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        await _catalogUpdates.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = await Task.Run(() =>
            {
                DesktopItemLoader.WarmUp();
                _launcher.Refresh();
                return _launcher.GetAvailableApps();
            }).ConfigureAwait(false);
            var catalog = await Dispatcher.UIThread.InvokeAsync(() =>
                entries.Select(a => a.LaunchKind == AppLaunchKind.Internal || a.Id.StartsWith("org.daisyos.", StringComparison.OrdinalIgnoreCase)
                    ? a with { Name = Loc.Get(a.Name), Description = Loc.Get(a.Description) } : a)
                    .OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToArray());
            await Task.Run(() => Applications.ReplaceCatalog(catalog)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Volatile.Write(ref _catalog, catalog);
                CatalogChanged?.Invoke(this, EventArgs.Empty);
            });
        }
        catch (Exception)
        {
            // Retain the last good snapshot and avoid logging catalog metadata.
            Console.Error.WriteLine("[daisy-search] Application catalog refresh failed.");
        }
        finally { _catalogUpdates.Release(); }
    }

    private void Register(string id, string name, string category, string[] keywords,
        Func<bool> read, Func<bool, Task> write)
    {
        Settings.Register(new(id, name, category, keywords,
            async token =>
            {
                token.ThrowIfCancellationRequested();
                return await Dispatcher.UIThread.InvokeAsync(read);
            },
            async (value, token) =>
            {
                token.ThrowIfCancellationRequested();
                await Dispatcher.UIThread.InvokeAsync(() => write(value));
            }));
    }
}
