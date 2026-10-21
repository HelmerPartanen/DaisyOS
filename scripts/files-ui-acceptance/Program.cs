using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Apps.Files.Models;

if (args.Length != 2) throw new ArgumentException("Usage: FilesUiAcceptance <fixture-root> <output-directory>");
Directory.CreateDirectory(args[1]);
AppBuilder.Configure<FilesQaApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz()
    .With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/Inter#Inter" })
    .SetupWithoutStarting();
var results = new List<object>();
foreach (var dir in Directory.GetDirectories(args[0]).OrderBy(x => x))
{
    var allocations = GC.GetTotalAllocatedBytes(true);
    var timer = Stopwatch.StartNew();
    var window = (FilesWindow)Activator.CreateInstance(typeof(FilesWindow), BindingFlags.Instance | BindingFlags.NonPublic, null, [dir, false], null)!;
    window.Width = 1100;
    window.Height = 760;
    window.Show();
    var tab = ((FilesViewModel)window.DataContext!).ActiveTab!;
    // Exercise view preferences without persisting QA choices to the user configuration.
    typeof(FilesTabViewModel).GetField("PreferencesChanged", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(tab, null);
    Pump(tab.NavigationTask);
    Tick(4);
    var firstFrame = tab.CurrentSession!.Performance.Timings.GetValueOrDefault("First frame");
    var grid = window.FindControl<ListBox>("EntriesGrid")!;
    var list = window.FindControl<ListBox>("EntriesList")!;
    var counts = new List<object>();
    foreach (var gridMode in new[] { false, true })
    {
        tab.IsGridView = gridMode;
        Tick(5);
        var view = gridMode ? grid : list;
        CheckContainers(view, tab, "top", counts);
        var target = tab.FilteredEntries[tab.FilteredEntries.Count / 2];
        view.ScrollIntoView(target);
        Tick(6);
        CheckContainers(view, tab, "middle", counts);
        if (Path.GetFileName(dir) == "images")
        {
            var decodeWait = Stopwatch.StartNew();
            while (!view.GetRealizedContainers().Any(c => c.DataContext is FileEntryViewModel { HasThumbnail: true }) && decodeWait.Elapsed < TimeSpan.FromSeconds(5)) Tick(1);
            if (!view.GetRealizedContainers().Any(c => c.DataContext is FileEntryViewModel { HasThumbnail: true })) throw new Exception("Image viewport did not decode a thumbnail.");
        }
        if (view.ContainerFromItem(target) == null) throw new Exception("ScrollIntoView did not realize its target.");
        var scroll = view.GetVisualDescendants().OfType<ScrollViewer>().First();
        if (scroll.Offset.Y <= 0) throw new Exception("ScrollIntoView did not scroll.");
        tab.SelectedItems.Clear();
        tab.SelectedItems.Add(target);
        var before = view.ContainerFromItem(target)!.TranslatePoint(default, scroll)!.Value.Y;
        var mutation = new FileEntrySnapshot(Path.Combine(dir, "000-ui-mutation.txt"), "000-ui-mutation.txt", ".txt", false, FileAttributes.Normal, 1, 1);
        tab.ApplyDelta(new([mutation], [], [], []));
        Tick(5);
        if (!tab.SelectedItems.Contains(target)) throw new Exception("Insert lost selection.");
        var after = view.ContainerFromItem(target)?.TranslatePoint(default, scroll)?.Y;
        // Grid row boundaries may shift by one cell row after an insertion.
        if (after == null || Math.Abs(after.Value - before) > (gridMode ? 114 : 1)) throw new Exception($"Insert lost scroll anchor: {before} -> {after}.");
        tab.ApplyDelta(new([], [mutation.FullPath], [], []));
        Tick(5);
        if (!tab.SelectedItems.Contains(target)) throw new Exception("Delete lost selection.");
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No rendered frame.");
        bitmap.Save(Path.Combine(args[1], $"{Path.GetFileName(dir)}-{(gridMode ? "grid" : "list")}.png"));
    }
    // The same collection/templates serve search and trash directory views.
    tab.SearchQuery = "*123*";
    Pump(tab.ViewUpdateTask);
    Tick(5);
    CheckContainers(grid, tab, "search", counts);
    tab.SearchQuery = "";
    Pump(tab.ViewUpdateTask);
    Tick(5);
    var maxPending = 0;
    foreach (var fraction in new[] { .1, .9, .2, .8, .3, .7 })
    {
        grid.ScrollIntoView(tab.FilteredEntries[(int)((tab.FilteredEntries.Count - 1) * fraction)]);
        Tick(2);
        maxPending = Math.Max(maxPending, tab.CurrentSession.Visuals.PendingCount);
        if (tab.CurrentSession.Visuals.ActiveCount > tab.CurrentSession.Visuals.WorkerLimit) throw new Exception("Decoder worker limit exceeded.");
        if (tab.CurrentSession.Visuals.PendingCount > 150) throw new Exception("Unbounded visual backlog.");
    }
    if (Path.GetFileName(dir) == "1k")
    {
        var enumerations = tab.EnumerationCount;
        Pump(tab.CreateNewDocumentAsync());
        Tick(5);
        var created = tab.SelectedEntry ?? throw new Exception("Create did not select the new entry.");
        var editor = window.GetVisualDescendants().OfType<TextBox>().FirstOrDefault(c => ReferenceEquals(c.DataContext, created) && c.Classes.Contains("RenameBox") && c.IsVisible);
        if (editor == null || !editor.IsFocused) throw new Exception("New document did not focus the rename editor.");
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        Tick(2);
        if (created.IsRenaming) throw new Exception("Escape did not cancel rename.");
        File.Delete(created.Path);
        Pump(tab.ReconcilePathsAsync([created.Path]));
        if (tab.EnumerationCount != enumerations) throw new Exception("Create re-enumerated the folder.");
        window.KeyPressQwerty(PhysicalKey.F5, RawInputModifiers.None);
        window.KeyReleaseQwerty(PhysicalKey.F5, RawInputModifiers.None);
        var deadline = Stopwatch.StartNew();
        while (tab.EnumerationCount == enumerations && deadline.Elapsed < TimeSpan.FromSeconds(5)) Tick(1);
        if (tab.EnumerationCount <= enumerations) throw new Exception("F5 did not reconcile the folder.");
        Tick(5);
    }
    results.Add(new { path = dir, entries = tab.Entries.Count, firstRenderedFrameMs = firstFrame,
        acceptanceRunMs = timer.Elapsed.TotalMilliseconds, allocated = GC.GetTotalAllocatedBytes(true) - allocations,
        containers = counts, maxPending, workers = tab.CurrentSession.Visuals.WorkerLimit,
        thumbnailsScheduled = tab.CurrentSession.Performance.Counts.GetValueOrDefault("Thumbnails scheduled"),
        timings = tab.CurrentSession.Performance.Timings.ToDictionary(x => x.Key, x => x.Value) });
    window.Close();
    Tick(3);
    Console.WriteLine($"Verified {dir}");
}
File.WriteAllText(Path.Combine(args[1], "ui-results.json"), JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));

static void CheckContainers(ListBox view, FilesTabViewModel tab, string position, List<object> results)
{
    var count = view.GetRealizedContainers().Count();
    results.Add(new { mode = tab.IsGridView ? "grid" : "list", position, count });
    if (tab.FilteredEntries.Count > 0 && (count == 0 || count > 150)) throw new Exception($"Invalid container count: {count}");
}
static void Tick(int count)
{
    for (int i = 0; i < count; i++)
    {
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Thread.Sleep(10);
    }
}
static void Pump(Task task)
{
    var deadline = Stopwatch.StartNew();
    while (!task.IsCompleted)
    {
        Tick(1);
        if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("UI task did not finish.");
    }
    task.GetAwaiter().GetResult();
}
public sealed class FilesQaApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        var shell = new App();
        shell.Initialize();
        var resources = shell.Resources;
        shell.Resources = new ResourceDictionary();
        Resources = resources;
        foreach (var style in shell.Styles.ToArray())
        {
            shell.Styles.Remove(style);
            Styles.Add(style);
        }
    }
}
