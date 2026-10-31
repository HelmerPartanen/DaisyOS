using System.Diagnostics;
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
using DaisyOS.System.Diagnostics;
using DaisyOS.System.Processes;

var output = args.FirstOrDefault() ?? "artifacts/system-monitor";
Directory.CreateDirectory(output);
AppBuilder.Configure<MonitorQaApp>()
    .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
    .UseSkia().UseHarfBuzz()
    .With(new FontManagerOptions { DefaultFamilyName = "avares://DaisyOS.Shell/Assets/fonts/Inter#Inter" })
    .SetupWithoutStarting();

foreach (var (id, label, expected) in new[] { ("files", "files", "Files"), ("taskmanager", "taskmanager", "System Monitor"), ("org.daisyos.appstore.desktop", "appstore", "App Store"), ("custom", "my LibreOffice app", "My LibreOffice App") })
    Check(TaskbarAppLabel.Format(id, label) == expected, "Pinned app label");
var runner = new RecordingRunner();
var service = new LinuxProcessMonitorService(runner);
Pump(CheckService());
foreach (var theme in new[] { ThemeVariant.Dark, ThemeVariant.Light })
foreach (var width in new[] { 920, 1100 })
{
    var window = new TaskManagerWindow { Width = width, Height = 760, RequestedThemeVariant = theme };
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
var compositor = new FakeWindows();
using (var vm = new TaskManagerViewModel(compositor, start: false))
{
    Pump(vm.RefreshAsync()); vm.Navigate(1);
    Check(vm.FilteredProcesses.Count == 1, "Compositor application rows");
    vm.SelectedProcess = vm.FilteredProcesses[0];
    Check(vm.CanEnd && !vm.CanForce, "Window close availability");
    Pump(vm.EndProcessAsync(vm.SelectedProcess));
    Check(compositor.Closed == "window-1", "Close uses compositor window identity");
}
Console.WriteLine("PASS: live sampling, sorting, filtering, selection, process protection, signals, tooltip labels and themed rendering.");
async Task CheckService()
{
    var expectedCpuCount = File.ReadLines("/proc/stat").Count(l => l.Length > 3 && l.StartsWith("cpu") && char.IsDigit(l[3]));
    Check(LinuxSystemMetricsService.LogicalProcessorCount == expectedCpuCount, "Hardware CPU count ignores validation quota");
    var processes = await service.GetProcessesAsync();
    var own = processes.Single(p => p.Pid == Environment.ProcessId);
    Check(own.StartTimeTicks > 0 && own.MemoryBytes > 0 && own.ExecutablePath.StartsWith('/'), "Process metadata");
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
    public IReadOnlyCollection<CompositorWindowRecord> Windows { get; } = [new("window-1", "notes", "Notes — Example", null, null, null, 1, false, false, false, false, true, ProcessId: Environment.ProcessId)];
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
