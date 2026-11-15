using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Media.Imaging;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell;
using DaisyOS.Shell.Apps.System.Diagnostics;
using DaisyOS.Shell.Views.Components.Taskbar;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Services.Windows;
using DaisyOS.System.Diagnostics;
using DaisyOS.System.Processes;

var output = args.FirstOrDefault() ?? "artifacts/system-monitor";
Directory.CreateDirectory(output);
AppBuilder.Configure<MonitorQaApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz()
    .With(new FontManagerOptions { DefaultFamilyName = "Noto Sans" })
    .SetupWithoutStarting();

foreach (var (id, label, expected) in new[] { ("files", "files", "Files"), ("taskmanager", "taskmanager", "System Monitor"), ("org.daisyos.appstore.desktop", "appstore", "App Store"), ("custom", "my LibreOffice app", "My LibreOffice App") })
    Check(TaskbarAppLabel.Format(id, label) == expected, "Pinned app label");
var runner = new RecordingRunner();
var service = new LinuxProcessMonitorService(runner);
Pump(CheckService());
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
foreach (var (width, height) in new[] { (920, 620), (1100, 760), (1360, 860) })
{
    var window = new TaskManagerWindow { Width = width, Height = height, RequestedThemeVariant = theme };
    var vm = (TaskManagerViewModel)window.DataContext!;
    window.Show();
    var startup = Stopwatch.StartNew();
    while (vm.ProcessCount == 0 && startup.Elapsed.TotalSeconds < 20) Tick(1);
    vm.SetActive(false);
    Pump(vm.RefreshAsync()); Tick(5);
    Check(window.WindowDecorations == WindowDecorations.None, "Native chrome");
    Check(vm.ProcessCount > 0, "Real process samples");
    Check(vm.TotalMemory > 0, "Physical memory sample");
    Save("overview");
    var cards = window.GetVisualDescendants().OfType<MonitorMetricCard>().ToArray();
    var probes = cards.Select(card => card.TranslatePoint(new Point(card.Bounds.Width - 14, card.Bounds.Height - 14), window)!.Value).ToArray();
    using (var baseline = window.CaptureRenderedFrame()!)
    {
        var colors = probes.Select(point => Pixel(baseline, point)).ToArray();
        Pump(vm.RefreshAsync());
        for (var frameIndex = 0; frameIndex < 4; frameIndex++)
        {
            Tick(1);
            using var frame = window.CaptureRenderedFrame()!;
            Check(probes.Select(point => Pixel(frame, point)).SequenceEqual(colors), "Live metric card surfaces do not flash during refresh");
        }
    }
    var tracker = new NativeAppWindowTracker();
    tracker.Register("taskmanager", window);
    foreach (var restoreState in new[] { WindowState.Normal, WindowState.Maximized })
    {
        window.WindowState = restoreState;
        window.WindowState = WindowState.Minimized;
        Check(tracker.ToggleFromTaskbar("taskmanager"), "Owned monitor taskbar restore");
        Check(window.WindowState == restoreState, "Preserve window state after minimize");
        Check(tracker.ToggleFromTaskbar("taskmanager"), "Owned monitor taskbar minimize");
        Check(window.WindowState == WindowState.Minimized, "Active taskbar click minimizes");
    }
    tracker.RestoreAndActivate("taskmanager");
    window.WindowState = WindowState.Normal;
    vm.SetActive(false);
    vm.Navigate(2); Tick(5);
    Check(vm.FilteredProcesses.Count > 0, "Process rows");
    vm.SortIndex = 2;
    Check(vm.FilteredProcesses.Zip(vm.FilteredProcesses.Skip(1)).All(p => p.First.MemoryBytes >= p.Second.MemoryBytes), "Memory ordering");
    var own = vm.FilteredProcesses.Single(p => p.Pid == Environment.ProcessId);
    vm.SelectedProcess = own;
    Check(!vm.CanEnd, "Protect shell process");
    Pump(vm.RefreshAsync());
    Check(ReferenceEquals(own, vm.SelectedProcess), "Preserve selected row during refresh");
    Save("processes");
    var realizedRows = window.GetVisualDescendants().OfType<ProcessTableGrid>().Where(row => row.Classes.Contains("ProcessDataRow")).ToArray();
    Check(realizedRows.Length > 0, "Virtualized process rows are realized");
    foreach (var row in realizedRows)
    {
        var icon = row.GetVisualDescendants().OfType<MonitorAppIcon>().Single();
        Check(icon.Source is not null || icon.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.DaisyIcon>().Single().IsVisible, "Every process has an app icon or a visible system fallback");
    }
    vm.CloseDetails(); vm.TreeMode = true; Tick(2); Save("process-tree");
    vm.TreeMode = false; vm.AdvancedColumns = true; Tick(2); Save("process-columns"); vm.AdvancedColumns = false;
    for (var resource = 0; resource < 4; resource++)
    {
        vm.OpenResource(resource); Tick(2);
        if (resource >= 2)
        {
            var selector = window.GetVisualDescendants().OfType<ComboBox>().Single(c => c.Name == "DeviceSelector");
            Check(selector.SelectedIndex == vm.DeviceIndex && selector.SelectedItem is string,  $"Device selector displays its selection: resource={resource}, index={selector.SelectedIndex}, model={vm.DeviceIndex}, item={selector.SelectedItem}, count={selector.ItemCount}, options={string.Join(",", vm.DeviceOptions)}, device={vm.SelectedDevice}");
            if (selector.ItemCount > 1)
            {
                selector.SelectedIndex = 1; Tick(2);
                var selectedDevice = vm.SelectedDevice;
                Pump(vm.RefreshAsync()); Tick(2);
                Check(vm.SelectedDevice == selectedDevice && selector.SelectedIndex == 1, "Device selection survives sampling");
                vm.OpenResource(resource == 2 ? 3 : 2); Tick(2); vm.OpenResource(resource); Tick(2);
                Check(vm.SelectedDevice == selectedDevice && selector.SelectedIndex == 1, "Device selection survives navigation");
                selector.SelectedIndex = 0; Tick(2);
            }
        }
        Save("resource-" + resource);
    }
    vm.Navigate(2);
    vm.SearchFilter = "no-such-process-123456789";
    Check(vm.IsEmpty, "Empty search state"); Save("empty");
    vm.SearchFilter = ""; vm.Navigate(1); Tick(4);
    Check(vm.IsEmpty && vm.EmptyMessage.Contains("native desktop"), "Unavailable compositor state");
    Save("applications-unavailable");
    vm.TogglePause(); Check(vm.PauseLabel == "Resume", "Pause state");
    window.Close();
    void Save(string page)
    {
        Tick(4);
        using var bitmap = window.CaptureRenderedFrame() ?? throw new Exception("No frame");
        bitmap.Save(Path.Combine(output, $"{page}-{theme.Key}-{width}.png"));
    }
}
using (var pausedModel = new TaskManagerViewModel(start: false))
{
    var pending = pausedModel.RefreshAsync();
    pausedModel.TogglePause();
    Pump(pending);
    Check(pausedModel.ProcessCount == 0 && pausedModel.MemoryHistory.Length == 0, "Pause freezes an in-flight update");
    Pump(pausedModel.RefreshAsync());
    Check(pausedModel.ProcessCount > 0 && pausedModel.PauseLabel == "Resume", "Manual refresh while paused");
}
var compositor = new FakeWindows();
using (var vm = new TaskManagerViewModel(compositor, start: false))
{
    Pump(vm.RefreshAsync()); vm.Navigate(1);
    Check(vm.Applications.Count == 1, "Grouped compositor application rows");
    vm.SelectedApplication = vm.Applications[0];
    Check(vm.CanCloseApp, "Window close availability");
    Pump(vm.CloseApplicationAsync());
    Check(compositor.Closed == "window-1", "Close uses compositor window identity");
    var grouped = new FakeWindows { Windows = [
        new("notes-1", "notes", "Shopping list", null, null, null, 1, true, false, false, false, true, ProcessId: Environment.ProcessId),
        new("notes-2", "notes", "Project notes", null, null, null, 2, false, true, false, false, true, ProcessId: Environment.ProcessId),
        new("browser-1", "browser", "Browser window", null, null, null, 1, false, false, false, false, true, ProcessId: 99999999)] };
    var appWindow = new TaskManagerWindow(grouped) { Width = 1360, Height = 860 };
    appWindow.Show();
    var apps = (TaskManagerViewModel)appWindow.DataContext!;
    Pump(apps.RefreshAsync()); apps.Navigate(1); apps.SelectedApplication = apps.Applications[0];
    Tick(3);
    foreach (var application in apps.Applications) application.IsExpanded = true;
    Tick(4);
    using var frame = appWindow.CaptureRenderedFrame() ?? throw new Exception("No app frame");
    frame.Save(Path.Combine(output, "applications-grouped.png"));
    appWindow.Close();
}
Console.WriteLine("PASS: stable card frames, process icons, aligned tables, live sampling, device selection, process protection and themed rendering.");
async Task CheckService()
{
    var expectedCpuCount = File.ReadLines("/proc/stat").Count(l => l.Length > 3 && l.StartsWith("cpu") && char.IsDigit(l[3]));
    Check(LinuxSystemMetricsService.LogicalProcessorCount == expectedCpuCount, "Hardware CPU count ignores validation quota");
    var processes = await service.GetProcessesAsync();
    var own = processes.Single(p => p.Pid == Environment.ProcessId);
    Check(own.StartTimeTicks > 0 && own.MemoryBytes > 0 && own.ExecutablePath.StartsWith('/') && own.ThreadCount > 0 && own.ParentPid > 0, "Process metadata");
    Check(!await service.KillProcessAsync(1), "Protect init");
    Check(!await service.KillProcessAsync(Environment.ProcessId), "Protect monitor");
    Check(!await service.EndInstanceAsync(Environment.ProcessId, own.StartTimeTicks + 1), "Reject wrong identity");
    await service.KillProcessAsync(1234567);
    Check(runner.Calls.Count == 1 && runner.Calls[0] == "-15", "No automatic force escalation");
    await service.KillProcessAsync(1234567, force: true);
    Check(runner.Calls.Count == 2 && runner.Calls[1] == "-9", "Explicit force signal");
    using var child = Process.Start(new ProcessStartInfo("sleep", "30") { UseShellExecute = false })!;
    try
    {
        var realService = new LinuxProcessMonitorService();
        var childInfo = (await realService.GetProcessesAsync()).Single(p => p.Pid == child.Id);
        Check(!await realService.EndInstanceAsync(child.Id, childInfo.StartTimeTicks + 1), "Reject recycled process identity");
        Check(!child.HasExited, "Wrong identity does not terminate child");
        Check(await realService.EndInstanceAsync(child.Id, childInfo.StartTimeTicks), "Terminate owned test process");
        await child.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
    }
    finally { if (!child.HasExited) child.Kill(); }
    var activity = new LinuxActivitySampler();
    Check(activity.Sample().ReceiveBytesPerSecond is null, "First sample has no rate");
    Check(activity.Sample().ReceiveBytesPerSecond >= 0, "Second rate sample");
}
static int Pixel(Bitmap bitmap, Point point)
{
    var memory = Marshal.AllocHGlobal(4);
    try { bitmap.CopyPixels(new PixelRect((int)point.X, (int)point.Y, 1, 1), memory, 4, 4); return Marshal.ReadInt32(memory); }
    finally { Marshal.FreeHGlobal(memory); }
}
static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
static void Tick(int count) { for (var i = 0; i < count; i++) { Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Thread.Sleep(10); } }
static void Pump(Task task) { var timer = Stopwatch.StartNew(); while (!task.IsCompleted) { Tick(1); if (timer.Elapsed.TotalSeconds > 60) throw new TimeoutException(); } task.GetAwaiter().GetResult(); }
public sealed class RecordingRunner : ICommandRunner
{
    public List<string> Calls { get; } = [];
    public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default) { Calls.Add(arguments[0]); return Task.FromResult(CommandResult.Failed("Permission denied")); }
}
public sealed class MonitorQaApp : Application
{
    public override void Initialize()
    {
        var shell = new App(); shell.Initialize();
        var resources = shell.Resources; shell.Resources = new ResourceDictionary(); Resources = resources;
        foreach (var style in shell.Styles.ToArray()) { shell.Styles.Remove(style); Styles.Add(style); }
    }
}

public sealed class FakeWindows : ICompositorWindowService
{
    public string? Closed { get; private set; }
    public IReadOnlyCollection<CompositorWindowRecord> Windows { get; init; } = [new("window-1", "notes", "Notes — Example", null, null, null, 1, false, false, false, false, true, ProcessId: Environment.ProcessId)];
    public event EventHandler? WindowsChanged { add { } remove { } }
    public bool IsAvailable => true;
    public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ActivateOrMinimizeAsync(string windowId, bool wasActive, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task CloseAsync(string windowId, CancellationToken cancellationToken = default) { Closed = windowId; return Task.CompletedTask; }
    public Task<bool> ForceQuitAsync(string windowId, CancellationToken cancellationToken = default) => Task.FromResult(false);
    public Task SetMaximizedAsync(string windowId, bool maximized, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task MoveToWorkspaceAsync(string windowId, int workspace, CancellationToken cancellationToken = default) => Task.CompletedTask;
    public Task ToggleOverviewEffectAsync(string effectName = "daisyos-task-overview-v16", CancellationToken cancellationToken = default) => Task.CompletedTask;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
