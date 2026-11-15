using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.System.Diagnostics;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.System.Diagnostics;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SystemMonitorDataTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 15, 12, 0, 0, TimeSpan.Zero);
    private static ProcessItemInfo Process(int pid, int parent = 1, long start = 1, double cpu = 0, long memory = 1024) =>
        new(pid, "process-" + pid, "/bin/process-" + pid, cpu, memory, Environment.UserName)
        { ParentPid = parent, StartTimeTicks = start, ThreadCount = 2, UserId = "1000" };
    private static CompositorWindowRecord Window(string id, string app, int pid) =>
        new(id, app, app + " window", null, null, null, 1, false, false, false, false, true, ProcessId: pid);
    private static MonitorSample Sample(params ProcessItemInfo[] processes) => new(
        new(Now, new("Test processor", 25, [20, 30], "0.1 · 0.2 · 0.3"), new(8192, 4096, 1024, 2048, 0, .2), [], [], TimeSpan.FromHours(1), []), processes, null);

    [Fact]
    public void ApplicationUsageCountsOwnersOnceAndStopsAtAnotherAppsBoundary()
    {
        var groups = ApplicationAttribution.Group([Window("a1", "app-a", 10), Window("a2", "app-a", 10), Window("b1", "app-b", 20)],
            [Process(10, cpu: 10), Process(11, 10, 2, 5), Process(20, 10, 2, 8), Process(21, 20, 3, 3)]);
        var a = Assert.Single(groups, g => g.Key == "app-a");
        Assert.Equal(2, a.Windows.Count);
        Assert.Equal(new[] { 10, 11 }, a.Processes.Select(p => p.Pid));
        Assert.Equal(15, a.Cpu);
        Assert.Equal(2048, a.Memory);
        Assert.Equal(new[] { 20, 21 }, Assert.Single(groups, g => g.Key == "app-b").Processes.Select(p => p.Pid));
    }

    [Fact]
    public void SharedAndMissingHostsAreNotInventedAppUsage()
    {
        var groups = ApplicationAttribution.Group([Window("a", "app-a", 10), Window("b", "app-b", 10), Window("c", "app-c", 99)], [Process(10), Process(11, 10, 2)]);
        Assert.All(groups, group => Assert.False(group.HasUsage));
        Assert.True(groups[0].HasSharedProcesses);
        Assert.True(groups[2].HasMissingProcesses);
        Assert.Contains("unavailable", groups[0].Attribution);
    }

    [Fact]
    public void ApplicationIdentityNormalizesDesktopIdsAndUnknownWindowsStaySeparate()
    {
        Assert.Equal("notes", ApplicationAttribution.Identity(Window("1", "org.daisyos.notes.desktop", 10)));
        var groups = ApplicationAttribution.Group([Window("1", "", 10), Window("2", "", 20)], [Process(10), Process(20)]);
        Assert.Equal(2, groups.Count);
    }

    [Fact]
    public void RecycledParentsDoNotClaimOlderChildren()
    {
        var group = Assert.Single(ApplicationAttribution.Group([Window("a", "app-a", 10)], [Process(10, start: 50), Process(11, 10, 5)]));
        Assert.Single(group.Processes);
    }

    [Fact]
    public void TimestampHistoryIsBoundedAndPreservesMissingSamplesAndGaps()
    {
        HistoryPoint[] history = [];
        for (var i = 0; i < 300; i++) history = MonitorHistory.Append(history, Now.AddSeconds(i * 2), i == 299 ? null : 10);
        Assert.True(history.Length <= 60);
        Assert.Null(history[^1].Value);
        var next = Now.AddSeconds(700);
        history = MonitorHistory.Append(history, next, 20);
        Assert.Equal(next, history[^1].Time);
        Assert.True((history[^1].Time - history[^2].Time).TotalSeconds > 5);
        Assert.All(history, point => Assert.True(point.Time > next.AddMinutes(-2)));
    }

    [AvaloniaFact]
    public async Task FilteringSortingAndProcessTreeRetainIdentityAndAncestry()
    {
        var sampler = new FakeSampling { Next = Sample(Process(10, cpu: 1), Process(11, 10, 2, 50), Process(20, cpu: 5)) };
        using var model = new TaskManagerViewModel(start: false, sampling: sampler);
        await model.RefreshAsync(); model.Navigate(2);
        Assert.Equal(11, model.FilteredProcesses[0].Pid);
        var selected = model.SelectedProcess = model.FilteredProcesses[0];
        await model.RefreshAsync();
        Assert.Same(selected, model.SelectedProcess);
        model.TreeMode = true;
        Assert.Equal(new[] { 20, 10, 11 }, model.FilteredProcesses.Select(p => p.Pid));
        model.SearchFilter = "process-11";
        Assert.Equal(new[] { 10, 11 }, model.FilteredProcesses.Select(p => p.Pid));
        model.SearchFilter = "";
        model.ToggleBranch(Assert.Single(model.FilteredProcesses, p => p.Pid == 10));
        Assert.DoesNotContain(model.FilteredProcesses, p => p.Pid == 11);
        model.TreeMode = false;
        model.SelectedProcess = Assert.Single(model.FilteredProcesses, p => p.Pid == 11);
        sampler.Next = Sample(Process(10), Process(20), Process(11, start: 100));
        await model.RefreshAsync();
        Assert.Null(model.SelectedProcess);
    }

    [AvaloniaFact]
    public async Task PauseAndSuspensionRejectPendingSamplesAndManualRefreshWorks()
    {
        var pending = new TaskCompletionSource<MonitorSample>();
        var sampler = new FakeSampling { Pending = pending.Task };
        using var model = new TaskManagerViewModel(start: false, sampling: sampler);
        var refresh = model.RefreshAsync(); model.TogglePause(); pending.SetResult(Sample(Process(10)));
        await refresh;
        Assert.Equal(0, model.ProcessCount);
        sampler.Pending = null; sampler.Next = Sample(Process(10));
        await model.RefreshAsync();
        Assert.Equal(1, model.ProcessCount);
        Assert.Equal("Resume", model.PauseLabel);
        model.TogglePause();
        var suspended = new TaskCompletionSource<MonitorSample>();
        sampler.Pending = suspended.Task;
        model.SetActive(true);
        var suspendedRefresh = model.RefreshAsync();
        model.SetActive(false);
        suspended.SetResult(Sample(Process(20)));
        await suspendedRefresh; model.Navigate(2);
        Assert.Equal(10, Assert.Single(model.FilteredProcesses).Pid);
        sampler.Pending = null; sampler.Next = Sample(Process(20));
        model.SetActive(true);
        await model.RefreshAsync();
        Assert.Equal(20, Assert.Single(model.FilteredProcesses).Pid);
        Assert.Equal(1, sampler.MaximumConcurrent);
    }

    [AvaloniaFact]
    public async Task ResourceFailureDoesNotClearOtherSourcesOrPretendToBeZero()
    {
        var sampler = new FakeSampling { Next = Sample(Process(10)) };
        using var model = new TaskManagerViewModel(start: false, sampling: sampler);
        await model.RefreshAsync();
        sampler.Next = sampler.Next with { Resources = sampler.Next.Resources with { Cpu = null, Errors = ["CPU unavailable"], Timestamp = Now.AddSeconds(2) } };
        await model.RefreshAsync();
        Assert.Equal("—", model.CpuUsage);
        Assert.NotEqual("—", model.MemoryUsage);
        Assert.Null(model.CpuHistory[^1].Value);
        Assert.True(model.HasError);
    }

    [AvaloniaFact]
    public async Task DeviceSelectionSurvivesFailureAndFallsBackWhenDeviceDisappears()
    {
        var sampler = new FakeSampling { Next = Sample() };
        sampler.Next = sampler.Next with { Resources = sampler.Next.Resources with { Disks = [new("sda", 100, 200, 1), new("sdb", 300, 400, 2)] } };
        using var model = new TaskManagerViewModel(start: false, sampling: sampler);
        await model.RefreshAsync(); model.OpenResource(2); model.SelectedDevice = "sdb";
        Assert.Equal("300 B/s", model.ResourceValue);
        sampler.Next = sampler.Next with { Resources = sampler.Next.Resources with { Disks = null, Timestamp = Now.AddSeconds(2), Errors = ["Disk activity unavailable"] } };
        await model.RefreshAsync();
        Assert.Equal("sdb", model.SelectedDevice);
        Assert.Equal("—", model.ResourceValue);
        sampler.Next = sampler.Next with { Resources = sampler.Next.Resources with { Disks = [new("sda", 100, 200, 1)], Timestamp = Now.AddSeconds(4), Errors = [] } };
        await model.RefreshAsync();
        Assert.Equal("All physical disks", model.SelectedDevice);
    }

    [AvaloniaFact]
    public async Task ConcurrentRefreshesCoalesceAndInspectionRejectsRecycledIdentity()
    {
        var pending = new TaskCompletionSource<MonitorSample>();
        var sampler = new FakeSampling { Pending = pending.Task };
        using var model = new TaskManagerViewModel(start: false, sampling: sampler);
        var first = model.RefreshAsync();
        var second = model.RefreshAsync();
        Assert.Same(first, second);
        pending.SetResult(Sample(Process(10, start: 50)));
        await first;
        model.InspectProcess(10, 1);
        Assert.Null(model.SelectedProcess);
        model.InspectProcess(10, 50);
        Assert.NotNull(model.SelectedProcess);
        Assert.Equal(1, sampler.MaximumConcurrent);
    }

    [AvaloniaFact]
    public async Task RefreshKeepsDetailAndContributorRowsForKeyboardFocusAndTextSelection()
    {
        var sampler = new FakeSampling { Next = Sample(Process(10, cpu: 25)) };
        using var model = new TaskManagerViewModel(start: false, sampling: sampler);
        await model.RefreshAsync();
        var top = Assert.Single(model.TopUsers);
        model.InspectProcess(10, 1);
        var command = Assert.Single(model.ProcessDetails, d => d.Label == "Command line");
        model.OpenResource(0);
        var contributor = Assert.Single(model.Contributors);
        var processor = Assert.Single(model.ResourceDetails, d => d.Label == "Processor");
        sampler.Next = Sample(Process(10, cpu: 50));
        await model.RefreshAsync();
        Assert.Same(top, Assert.Single(model.TopUsers));
        Assert.Equal("50.0%", top.Value);
        Assert.Same(command, Assert.Single(model.ProcessDetails, d => d.Label == "Command line"));
        Assert.Same(contributor, Assert.Single(model.Contributors));
        Assert.Same(processor, Assert.Single(model.ResourceDetails, d => d.Label == "Processor"));
    }

    [AvaloniaFact]
    public async Task IconsAreCachedPerExecutableAndInheritedByAppWorkers()
    {
        var appIcon = new DrawingImage(); var toolIcon = new DrawingImage();
        var calls = 0;
        var resolver = new MonitorIconResolver(identities =>
        {
            calls++;
            return identities.Contains("notes") ? appIcon : toolIcon;
        });
        var processes = new[] { Process(10), Process(11, 10, 2), Process(20) with { Name = "tool", ExecutablePath = "/bin/tool" }, Process(21) with { Name = "tool", ExecutablePath = "/bin/tool" } };
        var windows = new[] { Window("notes", "notes", 10) };
        var icons = await resolver.ResolveAsync(processes, windows, CancellationToken.None);
        Assert.Same(appIcon, icons[10]); Assert.Same(appIcon, icons[11]);
        Assert.Same(toolIcon, icons[20]); Assert.Same(toolIcon, icons[21]);
        await resolver.ResolveAsync(processes, windows, CancellationToken.None);
        Assert.Equal(2, calls);
    }

    [AvaloniaFact]
    public async Task DifferentAppsInTheSameRuntimeDoNotReuseTheWrongIcon()
    {
        var editor = new DrawingImage(); var runtime = new DrawingImage();
        var resolver = new MonitorIconResolver(identities => identities.First() == "code-oss" ? editor : runtime);
        var icons = await resolver.ResolveAsync([Process(10) with { Name = "code-oss", ExecutablePath = "/bin/electron" },
            Process(20) with { Name = "electron", ExecutablePath = "/bin/electron" }], [], CancellationToken.None);
        Assert.Same(editor, icons[10]); Assert.Same(runtime, icons[20]);
    }

    private sealed class FakeSampling : IMonitorSamplingService
    {
        public MonitorSample Next { get; set; } = Sample();
        public Task<MonitorSample>? Pending { get; set; }
        public int MaximumConcurrent { get; private set; }
        private int _concurrent;
        public async Task<MonitorSample> SampleAsync(CancellationToken token)
        {
            _concurrent++; MaximumConcurrent = Math.Max(MaximumConcurrent, _concurrent);
            try { return Pending is { } task ? await task : Next; } finally { _concurrent--; }
        }
        public Task<ProcessDetails?> GetDetailsAsync(int pid, long start, CancellationToken token) => Task.FromResult<ProcessDetails?>(new(pid, start, "test command", Now));
        public Task<bool> EndInstanceAsync(int pid, long start, bool force, CancellationToken token) => Task.FromResult(false);
    }
}
