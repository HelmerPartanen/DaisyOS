using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Audio;
using DaisyOS.System.Diagnostics;
using DaisyOS.System.Display;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public class ModernOsFeaturesTests
{
    private sealed class MockCommandRunner : ICommandRunner
    {
        public string LastCommand { get; private set; } = string.Empty;
        public IReadOnlyList<string> LastArguments { get; private set; } = Array.Empty<string>();
        public List<(string Command, IReadOnlyList<string> Arguments)> History { get; } = new();
        public string OutputToReturn { get; set; } = string.Empty;
        public bool SuccessToReturn { get; set; } = true;

        public Task<CommandResult> RunAsync(string command, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            LastArguments = arguments;
            History.Add((command, arguments));
            return Task.FromResult(new CommandResult(SuccessToReturn ? 0 : 1, OutputToReturn, string.Empty, TimedOut: !SuccessToReturn));
        }
    }

    [Fact]
    public async Task NightLightService_SetsExpectedBusctlArguments()
    {
        var mockRunner = new MockCommandRunner();
        var service = new LinuxNightLightService(mockRunner);

        mockRunner.OutputToReturn = "b true";
        Assert.True(await service.SetNightLightAsync(true, 3500));
        Assert.Contains(mockRunner.History, call => call.Arguments.Contains("3500"));
        Assert.DoesNotContain(mockRunner.History, call => call.Arguments.Contains("preview"));
        Assert.Contains(mockRunner.History, call => call.Arguments.Contains("Mode") && call.Arguments.Contains("0"));
        Assert.Contains(mockRunner.History, call => call.Arguments.Contains("reconfigure"));
        Assert.True(await service.IsNightLightEnabledAsync());
        mockRunner.OutputToReturn = "b false";
        Assert.True(await service.SetNightLightAsync(false));
        Assert.False(await service.IsNightLightEnabledAsync());
        Assert.Contains(mockRunner.History, call => call.Arguments.Contains("Active") && call.Arguments.Contains("false"));
    }

    [Fact]
    public async Task NightLightService_FailedWriteDoesNotReportApplied()
    {
        var runner = new MockCommandRunner { SuccessToReturn = false };
        Assert.False(await new LinuxNightLightService(runner).SetNightLightAsync(true));
        Assert.DoesNotContain(runner.History, call => call.Arguments.Contains("reconfigure"));
    }

    [Fact]
    public async Task ProcessMonitorService_EnumeratesRealProcesses()
    {
        if (!OperatingSystem.IsLinux()) return;

        var service = new LinuxProcessMonitorService();
        var processes = await service.GetProcessesAsync();

        Assert.NotNull(processes);
        Assert.NotEmpty(processes);

        var currentProcess = processes.FirstOrDefault(p => p.Pid == Environment.ProcessId);
        Assert.NotNull(currentProcess);
        Assert.True(currentProcess.MemoryBytes > 0);
    }

    [Fact]
    public async Task AudioService_ParsesInputDevicesCorrectly()
    {
        var mockOutput = @"
Audio
 ├─ Devices:
 │      45. Scarlett Solo (3rd Gen.)            [alsa]
 │  
 ├─ Sinks:
 │  *   57. Scarlett Solo (3rd Gen.) Direct Scarlett Solo USB [vol: 1.00]
 │  
 ├─ Sources:
 │      56. Built-in Audio Analog Stereo        [vol: 1.00]
 │  *   58. Scarlett Solo (3rd Gen.) Direct Scarlett Solo USB [vol: 1.00]
 │  
 ├─ Filters:
 │  
 └─ Streams:
        77. Spotify                                                     
             79. output_FL       > Scarlett Solo USB:playback_FL	[active]
             81. output_FR       > Scarlett Solo USB:playback_FR	[active]
";
        var mockRunner = new MockCommandRunner { OutputToReturn = mockOutput };
        var audioService = new LinuxAudioService(mockRunner);

        var inputs = await audioService.GetAudioInputDevicesAsync();
        Assert.Equal(2, inputs.Count);
        Assert.Equal("56", inputs[0].Id);
        Assert.False(inputs[0].IsDefault);
        Assert.Equal("58", inputs[1].Id);
        Assert.True(inputs[1].IsDefault);

        var streams = await audioService.GetAudioAppStreamsAsync();
        Assert.Single(streams);
        Assert.Equal("77", streams[0].Id);
        Assert.Equal("Spotify", streams[0].Name);
    }

    [Fact]
    public void AppLauncher_IncludesTaskManager()
    {
        var launcher = new LinuxAppLauncherService();
        var apps = launcher.GetAvailableApps();
        var taskManager = apps.FirstOrDefault(a => a.Name == "Task Manager");

        Assert.NotNull(taskManager);
        Assert.Equal("org.daisyos.taskmanager.desktop", taskManager.Id);
    }

    [Fact]
    public async Task InputHardwareService_SetsExpectedConfigValues()
    {
        var mockRunner = new MockCommandRunner();
        var tempFile = Path.Combine(Path.GetTempPath(), $"kcminputrc_test_{Guid.NewGuid():N}");
        try
        {
            var service = new DaisyOS.System.Input.LinuxInputHardwareService(mockRunner, tempFile);

            await service.SetNaturalScrollAsync(true);
            Assert.Contains(mockRunner.History, h => h.Arguments.Contains("NaturalScroll"));

            await service.SetPointerSpeedAsync("Fast");
            Assert.Contains(mockRunner.History, h => h.Arguments.Contains("0.4"));

            await service.SetTapToClickAsync(true);
            Assert.Contains(mockRunner.History, h => h.Arguments.Contains("TapToClick"));
            Assert.Contains(mockRunner.History, h => h.Command == "busctl" && h.Arguments.Contains("reconfigure"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void ClipboardHistoryService_ManagesClipsAndPinsCorrectly()
    {
        var service = new ClipboardHistoryService();
        service.AddClip("First Clip");
        service.AddClip("Second Clip");

        var items = service.GetItems();
        Assert.True(items.Count >= 2);
        Assert.Equal("Second Clip", items[0].Text);

        var firstClipId = items[1].Id;
        service.TogglePin(firstClipId);

        var itemsAfterPin = service.GetItems();
        Assert.True(itemsAfterPin[0].IsPinned);
        Assert.Equal("First Clip", itemsAfterPin[0].Text);

        service.ClearAll();
        var itemsAfterClear = service.GetItems();
        Assert.Single(itemsAfterClear);
        Assert.Equal("First Clip", itemsAfterClear[0].Text);
    }

    [Fact]
    public void MultiMonitor_ParsesAllDisplaysCorrectly()
    {
        var json = @"
{
    ""outputs"": [
        {
            ""name"": ""DP-1"",
            ""connected"": true,
            ""enabled"": true,
            ""priority"": 1,
            ""pos"": { ""x"": 0, ""y"": 0 },
            ""scale"": 1.0,
            ""size"": { ""width"": 2560, ""height"": 1440 },
            ""currentModeId"": ""1"",
            ""modes"": [
                { ""id"": ""1"", ""size"": { ""width"": 2560, ""height"": 1440 }, ""refreshRate"": 144.0 }
            ]
        },
        {
            ""name"": ""HDMI-1"",
            ""connected"": true,
            ""enabled"": true,
            ""priority"": 2,
            ""pos"": { ""x"": 2560, ""y"": 0 },
            ""scale"": 1.0,
            ""size"": { ""width"": 1920, ""height"": 1080 },
            ""currentModeId"": ""2"",
            ""modes"": [
                { ""id"": ""2"", ""size"": { ""width"": 1920, ""height"": 1080 }, ""refreshRate"": 60.0 }
            ]
        }
    ]
}";
        var displays = DaisyOS.Shell.Apps.System.Settings.DisplaySettingsHelper.ParseAllDisplaysKScreenDoctorJson(json);
        Assert.Equal(2, displays.Count);

        var primary = displays[0];
        Assert.Equal("DP-1", primary.Name);
        Assert.True(primary.IsPrimary);
        Assert.Equal(0, primary.PositionX);
        Assert.Equal(2560, primary.Width);
        Assert.Equal(1440, primary.Height);

        var secondary = displays[1];
        Assert.Equal("HDMI-1", secondary.Name);
        Assert.False(secondary.IsPrimary);
        Assert.Equal(2560, secondary.PositionX);
        Assert.Equal(1920, secondary.Width);
        Assert.Equal(1080, secondary.Height);
    }

    [Fact]
    public void LayerShellSurfaceOptions_LockScreen_HasOverlayRoleAndExclusiveKeyboard()
    {
        var primary = DaisyOS.Shell.Services.Shell.LayerShellSurfaceOptions.LockScreen("DP-1", isSecondary: false);
        Assert.Equal(DaisyOS.Shell.Services.Shell.LayerShellSurfaceRole.Overlay, primary.Role);
        Assert.Equal("daisyos.overlay.lockscreen", primary.Namespace);
        Assert.Equal("DP-1", primary.OutputName);
        Assert.Equal(DaisyOS.Shell.Services.Shell.LayerShellSurfaceOptions.IgnoreOtherExclusiveZones, primary.ExclusiveZone);
        Assert.Equal(DaisyOS.Shell.Services.Shell.LayerShellKeyboardInteractivity.Exclusive, primary.KeyboardInteractivity);
        Assert.True(primary.Anchor.HasFlag(DaisyOS.Shell.Services.Shell.LayerShellAnchor.Top));
        Assert.True(primary.Anchor.HasFlag(DaisyOS.Shell.Services.Shell.LayerShellAnchor.Bottom));
        Assert.True(primary.Anchor.HasFlag(DaisyOS.Shell.Services.Shell.LayerShellAnchor.Left));
        Assert.True(primary.Anchor.HasFlag(DaisyOS.Shell.Services.Shell.LayerShellAnchor.Right));

        var secondary = DaisyOS.Shell.Services.Shell.LayerShellSurfaceOptions.LockScreen("HDMI-1", isSecondary: true);
        Assert.Equal(DaisyOS.Shell.Services.Shell.LayerShellSurfaceRole.Overlay, secondary.Role);
        Assert.Equal("HDMI-1", secondary.OutputName);
        Assert.Equal(DaisyOS.Shell.Services.Shell.LayerShellKeyboardInteractivity.None, secondary.KeyboardInteractivity);
    }

    [Fact]
    public void UnavailableLayerShellBackend_CreateWindowGeneric_ThrowsInvalidOperation()
    {
        var backend = new DaisyOS.Shell.Services.Shell.UnavailableLayerShellWindowBackend();
        var options = DaisyOS.Shell.Services.Shell.LayerShellSurfaceOptions.LockScreen();

        Assert.Throws<InvalidOperationException>(() => backend.CreateWindow(options, () => new Avalonia.Controls.Window()));
    }
}
