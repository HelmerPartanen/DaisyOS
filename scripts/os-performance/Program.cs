using System.Diagnostics;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;

if (args.Length != 4) throw new ArgumentException("Usage: PerformanceBenchmark <fixture-root> <output.json> <samples> <warmups>");
var fixture = args[0]; var output = args[1]; var samples = int.Parse(args[2]); var warmups = int.Parse(args[3]);
DaisyOS.Shell.Services.Theming.LinuxTextLibraryIsolation.EnsureLoaded();
AppBuilder.Configure<BenchmarkApp>().UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz().With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/Inter#Inter" }).SetupWithoutStarting();
var metrics = new Dictionary<string, List<double>>();
const int catalogSize = 2000, fileCount = 1000;
const string launcherQuery = "Application 012", filesQuery = "*012*", filesSort = "Size";
var apps = Enumerable.Range(0, catalogSize).Select(i => new AppEntry($"bench-{i}", $"Application {i:D4}", "Benchmark fixture", "", false)).ToArray();
var launcher = new LauncherView(new LauncherViewModel(new ShellSessionState(Path.Combine(fixture, "state.json")), apps));
var window = new Window { Width = 720, Height = 600, Content = launcher, Background = Brushes.Gray };
window.Show(); Tick();
for (var run = -warmups; run < samples; run++)
{
    launcher.ViewModel.SearchText = "Application"; Tick();
    var watch = Stopwatch.StartNew();
    launcher.ViewModel.SearchText = launcherQuery; Tick();
    Record("launcher.search", watch.Elapsed.TotalMilliseconds, run);
    if (launcher.ViewModel.FilteredApps.Count != 10) throw new Exception($"Search result mismatch: {launcher.ViewModel.FilteredApps.Count} matches, {launcher.ViewModel.AllApps.Count} catalog entries.");
    launcher.ViewModel.SearchText = "Application"; Tick();
    watch.Restart(); launcher.IsVisible = false; Tick(); launcher.IsVisible = true; Tick();
    Record("launcher.show-hide", watch.Elapsed.TotalMilliseconds, run);
}
window.Close();
// The model-only workloads use their explicit dispatcher, not the stopped GUI loop.
SynchronizationContext.SetSynchronizationContext(null);
for (var run = -warmups; run < samples; run++)
{
    using var tab = new FilesTabViewModel(Path.Combine(fixture, "files"), navigateImmediately: false, uiDispatcher: new ImmediateDispatcher(), watchFiles: false);
    var watch = Stopwatch.StartNew();
    tab.NavigateToAsync(Path.Combine(fixture, "files")).GetAwaiter().GetResult();
    Record("files.enumerate", watch.Elapsed.TotalMilliseconds, run);
    if (tab.Entries.Count != fileCount || tab.CurrentSession?.IsReady != true) throw new Exception("Directory fixture mismatch.");
    var enumerations = tab.EnumerationCount;
    watch.Restart(); tab.SearchQuery = filesQuery; tab.ViewUpdateTask.GetAwaiter().GetResult();
    Record("files.filter", watch.Elapsed.TotalMilliseconds, run);
    if (tab.FilteredEntries.Count != 11) throw new Exception("Filter result mismatch.");
    tab.SearchQuery = ""; tab.ViewUpdateTask.GetAwaiter().GetResult();
    watch.Restart(); tab.SortColumn = filesSort; tab.ViewUpdateTask.GetAwaiter().GetResult();
    Record("files.sort", watch.Elapsed.TotalMilliseconds, run);
    if (enumerations != tab.EnumerationCount) throw new Exception("Sort/filter re-enumerated the directory.");
}
File.WriteAllText(output, JsonSerializer.Serialize(new { protocol = 1, workload = new { files = fileCount, launcherApps = catalogSize, launcherWidth = window.Width, launcherHeight = window.Height, launcherQuery, filesQuery, filesSort }, metrics }, new JsonSerializerOptions { WriteIndented = true }));
void Record(string name, double milliseconds, int run)
{
    if (run < 0) return;
    if (!metrics.TryGetValue(name, out var values)) metrics[name] = values = [];
    values.Add(milliseconds);
}
static void Tick() { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs(); }
sealed class ImmediateDispatcher : IFileUiDispatcher { public Task InvokeAsync(Action action) { action(); return Task.CompletedTask; } }
public sealed class BenchmarkApp : Application
{
    public override void Initialize()
    {
        var shell = new App(); shell.Initialize(); var resources = shell.Resources; shell.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
    }
}
