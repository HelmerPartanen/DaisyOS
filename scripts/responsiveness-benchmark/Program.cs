using System.Diagnostics;
using System.Reflection;
using Pointer = Avalonia.Input.Pointer;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;

if (args.Length is < 1 or > 2)
    throw new ArgumentException("Usage: responsiveness-benchmark <output.json> [samples]");
var output = Path.GetFullPath(args[0]);
var samples = args.Length == 2 ? int.Parse(args[1]) : 40;
if (samples < 5) throw new ArgumentException("At least five samples are required.");
LinuxTextIsolation();
AppBuilder.Configure<QaApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz().With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/Inter#Inter" }).SetupWithoutStarting();
var fixture = Directory.CreateTempSubdirectory("daisy-launcher-benchmark-");
try
{
    const int catalogSize = 2000;
    var apps = Enumerable.Range(0, catalogSize).Select(i => new AppEntry($"qa-{i}", $"Application {i:D4}", "Benchmark application", "", false)).ToArray();
    var cold = Stopwatch.StartNew();
    var model = new LauncherViewModel(new ShellSessionState(Path.Combine(fixture.FullName, "state.json")), apps);
    var view = new LauncherView(model) { IsVisible = false };
    // Measure preparation/render work, excluding intentional animation duration.
    // Keep generic overrides for comparison with older launcher assemblies.
    view.Resources["MotionDurationEnter"] = TimeSpan.Zero;
    view.Resources["MotionDurationExit"] = TimeSpan.Zero;
    view.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
    view.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
    var window = new Window { Width = 1280, Height = 900, Content = view, Background = Brushes.Gray };
    window.Show(); view.AnimateIn(); Tick();
    var coldMs = cold.Elapsed.TotalMilliseconds;
    var metrics = new Dictionary<string, List<(double Ms, long Bytes)>>();
    using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
    var viewport = view.FindControl<Grid>("LauncherPageViewport")!;
    ulong timestamp = 1000;
    for (var run = -5; run < samples; run++)
    {
        model.SearchText = "Application"; Tick();
        Measure("search-render", () => { model.SearchText = "Application 012"; Tick(); }, run);
        if (model.FilteredApps.Count != 12 || model.ResolveSearchResult()?.Name != "Application 0120")
            throw new Exception($"Search mismatch: count={model.FilteredApps.Count}, top={model.ResolveSearchResult()?.Name}, query={model.SearchText}.");
        model.SearchText = ""; model.GoToPage(0); Tick();
        Measure("page-render", () => { model.GoToPage(1); Tick(); }, run);
        Measure("swipe-render", () =>
        {
            Scroll(-40); Tick(); Scroll(-80); Tick(); Scroll(-100); Tick(); Scroll(0, true); Tick();
        }, run);
        view.CloseImmediate(); Tick();
        Measure("warm-open-render", () => { view.AnimateIn(); Tick(); }, run);
    }
    var tiles = view.GetVisualDescendants().OfType<Button>().Count(b => b.IsEffectivelyVisible && b.Classes.Contains("LauncherTile"));
    if (tiles != model.PageApps.Count) throw new Exception($"Visible tiles do not match page: {tiles}.");
    Directory.CreateDirectory(Path.GetDirectoryName(output)!);
    File.WriteAllText(output, JsonSerializer.Serialize(new
    {
        protocol = 2,
        shellBuildConfiguration = typeof(App).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
        shellAssemblySha256 = Convert.ToHexString(global::System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(typeof(App).Assembly.Location))),
        presentationMeasured = false,
        mode = "headless Skia CPU preparation and render tick; animations disabled; no KWin presentation",
        catalogSize, samples, coldConstructionOpenRenderMs = coldMs, visibleTiles = tiles,
        metrics = metrics.ToDictionary(x => x.Key, x => Summary(x.Value)),
        managedBytes = GC.GetTotalMemory(false)
    }, new JsonSerializerOptions { WriteIndented = true }));
    view.CloseImmediate(); window.Close();
    void Measure(string name, Action action, int run)
    {
        var bytes = GC.GetAllocatedBytesForCurrentThread();
        var watch = Stopwatch.StartNew(); action(); var ms = watch.Elapsed.TotalMilliseconds;
        if (run < 0) return;
        if (!metrics.TryGetValue(name, out var values)) metrics[name] = values = [];
        values.Add((ms, GC.GetAllocatedBytesForCurrentThread() - bytes));
    }
    void Scroll(double pixels, bool end = false)
    {
        timestamp += 16;
        viewport.RaiseEvent(new PointerWheelEventArgs(viewport, pointer, window, new Point(100, 100), timestamp,
            new PointerPointProperties(), KeyModifiers.None, new Vector(pixels / 4, 0)) { IsPrecise = true, IsScrollEnd = end });
    }
}
finally { fixture.Delete(true); }
static object Summary(List<(double Ms, long Bytes)> values)
{
    var times = values.Select(x => x.Ms).Order().ToArray();
    var allocations = values.Select(x => x.Bytes).Order().ToArray();
    return new { p50Ms = times[times.Length / 2], p95Ms = times[(int)Math.Ceiling(times.Length * 0.95) - 1], worstMs = times[^1], medianAllocatedBytes = allocations[allocations.Length / 2] };
}
static void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
static void LinuxTextIsolation() => DaisyOS.Shell.Services.Theming.LinuxTextLibraryIsolation.EnsureLoaded();
public sealed class QaApp : Application
{
    public override void Initialize()
    {
        var shell = new App(); shell.Initialize(); var resources = shell.Resources; shell.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }
}
