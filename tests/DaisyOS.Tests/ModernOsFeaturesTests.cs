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
        public Func<string, IReadOnlyList<string>, CommandResult>? ResultFactory { get; set; }

        public Task<CommandResult> RunAsync(string command, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            LastCommand = command;
            LastArguments = arguments;
            History.Add((command, arguments));
            if (ResultFactory is not null)
                return Task.FromResult(ResultFactory(command, arguments));
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
    public void AppLauncher_IncludesSystemMonitor()
    {
        var launcher = new LinuxAppLauncherService();
        var apps = launcher.GetAvailableApps();
        var matchingApps = apps.Where(a => a.Name == "System Monitor").ToArray();
        var taskManager = Assert.Single(matchingApps);

        Assert.Equal("org.daisyos.taskmanager.desktop", taskManager.Id);
        Assert.Equal(AppLaunchKind.Internal, taskManager.LaunchKind);
        Assert.Contains("Task Manager", taskManager.SearchTerms!);
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

            await service.SetTrackpadScrollSpeedAsync(1);
            Assert.Contains(mockRunner.History, h => h.Arguments.Contains("ScrollFactor") && h.Arguments.Contains("0.2"));
            Assert.Contains(mockRunner.History, h => h.Arguments.Contains(tempFile));
            Assert.Contains(mockRunner.History, h => h.Command == "busctl" && h.Arguments.Contains("reconfigure"));
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InputHardwareService_ReadsAndWritesTrackpadScrollSpeed()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kcminputrc_scroll_{Guid.NewGuid():N}");
        try
        {
            var mockRunner = new MockCommandRunner();
            var service = new DaisyOS.System.Input.LinuxInputHardwareService(mockRunner, tempFile);

            // DaisyOS defaults to the middle tick when no preference exists.
            var status = await service.GetStatusAsync();
            Assert.Equal(2, status.TrackpadScrollSpeed);

            // An explicit 0.4 factor remains the faster tick (3).
            await File.WriteAllLinesAsync(tempFile, ["[Touchpad]", "ScrollFactor=0.4"]);
            status = await service.GetStatusAsync();
            Assert.Equal(3, status.TrackpadScrollSpeed);

            // Force the command fallback and verify the selected value survives
            // a fresh read from the same config file.
            mockRunner.SuccessToReturn = false;
            Assert.False(await service.SetTrackpadScrollSpeedAsync(4));
            Assert.Contains(mockRunner.History, h => h.Arguments.Contains("ScrollFactor") && h.Arguments.Contains("0.5"));
            status = await service.GetStatusAsync();
            Assert.Equal(4, status.TrackpadScrollSpeed);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InputHardwareService_AppliesBalancedDefaultWithoutOverwritingAChoice()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kcminputrc_default_scroll_{Guid.NewGuid():N}");
        try
        {
            var runner = new MockCommandRunner { SuccessToReturn = false };
            var service = new DaisyOS.System.Input.LinuxInputHardwareService(runner, tempFile);

            Assert.False(await service.EnsureDefaultTrackpadScrollSpeedAsync());
            Assert.Contains("ScrollFactor=0.3", await File.ReadAllLinesAsync(tempFile));

            await File.WriteAllLinesAsync(tempFile, ["[Touchpad]", "ScrollFactor=1.4"]);
            runner.History.Clear();
            Assert.False(await service.EnsureDefaultTrackpadScrollSpeedAsync()); // Compositor is unavailable.
            Assert.DoesNotContain(runner.History, call => call.Arguments.Contains("set-property"));
            var persisted = await File.ReadAllTextAsync(tempFile);
            Assert.Contains("[Touchpad]\nScrollFactor=1.4", persisted);
            Assert.Contains("[Libinput][Defaults][Touchpad]\nScrollFactor=1.4", persisted);
            runner.History.Clear();
            Assert.True(await service.EnsureDefaultTrackpadScrollSpeedAsync());
            Assert.Empty(runner.History);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task InputHardwareService_SeedsHotplugDefaultsWithoutReplacingDevicePreferences()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"kcminputrc_hotplug_{Guid.NewGuid():N}");
        try
        {
            const string device = "[Libinput][1234][5678][Custom Touchpad]\nScrollFactor=0.5\n";
            await File.WriteAllTextAsync(tempFile, device);
            var runner = new MockCommandRunner { SuccessToReturn = false };
            var service = new DaisyOS.System.Input.LinuxInputHardwareService(runner, tempFile);
            Assert.False(await service.EnsureDefaultTrackpadScrollSpeedAsync());
            var config = await File.ReadAllTextAsync(tempFile);
            Assert.StartsWith(device, config);
            Assert.Contains("[Libinput][Defaults][Touchpad]\nScrollFactor=0.3", config);
            Assert.DoesNotContain(runner.History, call => call.Arguments.Contains("set-property"));
            Assert.Contains(runner.History, call => call.Command == "kwriteconfig6" &&
                call.Arguments.Count(argument => argument == "--group") == 3 &&
                call.Arguments.Contains("Defaults"));
        }
        finally { if (File.Exists(tempFile)) File.Delete(tempFile); }
    }

    [Fact]
    public async Task InputHardwareService_AppliesAndReadsActiveKWinTouchpadScrollSpeed()
    {
        var mockRunner = new MockCommandRunner
        {
            ResultFactory = (_, arguments) =>
            {
                var output = arguments.Contains("tree")
                    ? "└─ /org/kde/KWin/InputDevice/event8\n"
                    : arguments.Contains("touchpad")
                        ? "b true\n"
                        : arguments.Contains("scrollFactor") && arguments.Contains("get-property")
                            ? "d 0.4\n"
                            : string.Empty;
                return new CommandResult(0, output, string.Empty, TimedOut: false);
            }
        };
        var tempFile = Path.Combine(Path.GetTempPath(), $"kcminputrc_live_{Guid.NewGuid():N}");
        try
        {
            var service = new DaisyOS.System.Input.LinuxInputHardwareService(mockRunner, tempFile);

            Assert.Equal(3, (await service.GetStatusAsync()).TrackpadScrollSpeed);
            Assert.True(await service.SetTrackpadScrollSpeedAsync(1));
            Assert.Contains(mockRunner.History, call =>
                call.Arguments.Contains("set-property") &&
                call.Arguments.Contains("scrollFactor") &&
                call.Arguments.Contains("0.2"));
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
