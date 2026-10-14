using System.Collections.Concurrent;
using DaisyOS.System.Gaming;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxGamingServiceTests
{
    [Fact]
    public async Task GetStatusAsync_ReportsReadyGamingStack()
    {
        var runner = new GamingCommandRunner((fileName, arguments) => (fileName, arguments.FirstOrDefault()) switch
        {
            ("lspci", _) => Success("03:00.0 VGA compatible controller: Advanced Micro Devices, Inc. Radeon RX 7800 XT"),
            ("vulkaninfo", _) => Success("Devices:\n    driverName: RADV"),
            ("which", "steam") => Success("/usr/bin/steam"),
            ("which", "gamemoderun") => Success("/usr/bin/gamemoderun"),
            ("which", "mangohud") => Success("/usr/bin/mangohud"),
            ("which", "gamescope") => Success("/usr/bin/gamescope"),
            ("which", "wine") => Success("/usr/bin/wine"),
            ("systemctl", "show") => Success("loaded"),
            ("systemctl", "is-active") => Success("active"),
            _ => Failure()
        });

        var status = await new LinuxGamingService(runner).GetStatusAsync();

        Assert.Equal("AMD", status.GpuVendor);
        Assert.Contains("Radeon RX 7800 XT", status.GpuModel, StringComparison.Ordinal);
        Assert.Equal("RADV", status.VulkanDriver);
        Assert.True(status.VulkanAvailable);
        Assert.True(status.SteamInstalled);
        Assert.True(status.GameModeInstalled);
        Assert.True(status.MangoHudInstalled);
        Assert.True(status.GamescopeInstalled);
        Assert.True(status.WineInstalled);
        Assert.True(status.BluetoothServiceAvailable);
        Assert.True(status.BluetoothServiceRunning);

        // Vulkan32Available and ControllerRulesInstalled are checked via File.Exists (not via
        // the command runner), so their values depend on whether the system libraries and udev
        // rules are actually installed on the test host — not asserted here.
    }

    [Fact]
    public async Task GetStatusAsync_UsesCalmMissingStatesWhenOptionalToolsAreUnavailable()
    {
        var runner = new GamingCommandRunner((fileName, _) => fileName == "lspci"
            ? Success("No graphics controller was reported")
            : Failure());

        var status = await new LinuxGamingService(runner).GetStatusAsync();

        Assert.Equal("Unknown", status.GpuVendor);
        Assert.False(status.VulkanAvailable);
        Assert.False(status.WineInstalled);
        Assert.False(status.BluetoothServiceAvailable);
        Assert.False(status.BluetoothServiceRunning);
        Assert.Contains("Vulkan: Not detected", status.Detail, StringComparison.Ordinal);
        Assert.Contains("Bluetooth: Not detected", status.Detail, StringComparison.Ordinal);
    }

    private static CommandResult Success(string output = "ok") => new(0, output, string.Empty, false);

    private static CommandResult Failure() => new(1, string.Empty, "unavailable", false);

    private sealed class GamingCommandRunner : ICommandRunner
    {
        private readonly Func<string, IReadOnlyList<string>, CommandResult> _handler;

        public GamingCommandRunner(Func<string, IReadOnlyList<string>, CommandResult> handler)
        {
            _handler = handler;
        }

        public ConcurrentQueue<(string FileName, IReadOnlyList<string> Arguments)> Calls { get; } = new();

        public Task<CommandResult> RunAsync(
            string fileName,
            IReadOnlyList<string> arguments,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls.Enqueue((fileName, arguments));
            return Task.FromResult(_handler(fileName, arguments));
        }
    }
}
