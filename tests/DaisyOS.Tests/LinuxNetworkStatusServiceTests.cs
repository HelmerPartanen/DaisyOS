using DaisyOS.System.Networking;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxNetworkStatusServiceTests
{
    [Fact]
    public async Task FailureReturnsCalmUnavailableStateWithoutCommandOutput()
    {
        var runner = new ScriptedCommandRunner(new CommandResult(
            10,
            string.Empty,
            "nmcli: D-Bus org.freedesktop.NetworkManager stack detail",
            TimedOut: false));
        var service = new LinuxNetworkStatusService(runner);

        var status = await service.GetStatusAsync();

        Assert.False(status.IsAvailable);
        Assert.False(status.IsConnected);
        Assert.Equal("Network unavailable", status.DisplayName);
        Assert.Equal("Couldn't read the network status. Try again in a moment.", status.Detail);
        Assert.DoesNotContain("nmcli", status.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("D-Bus", status.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoActiveDeviceReturnsOfflineState()
    {
        var runner = new ScriptedCommandRunner(new CommandResult(
            0,
            "wlan0:wifi:disconnected:--\neth0:ethernet:unavailable:--",
            string.Empty,
            TimedOut: false),
            new CommandResult(0, "none", string.Empty, TimedOut: false));
        var service = new LinuxNetworkStatusService(runner);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.False(status.IsConnected);
        Assert.Equal(DaisyOS.Core.Models.NetworkConnectivity.None, status.Connectivity);
        Assert.Equal("Offline", status.DisplayName);
        Assert.Equal("No active network connection was found.", status.Detail);
    }

    [Fact]
    public async Task ConnectedEthernetUsesPlainStatusAndTakesPriority()
    {
        var runner = new ScriptedCommandRunner(new CommandResult(
            0,
            "wlan0:wifi:connected:Studio Wi-Fi\neth0:ethernet:connected:Wired connection",
            string.Empty,
            TimedOut: false),
            new CommandResult(0, "full", string.Empty, TimedOut: false));
        var service = new LinuxNetworkStatusService(runner);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.True(status.IsConnected);
        Assert.Equal("Wired connection", status.DisplayName);
        Assert.Equal("Ethernet is connected.", status.Detail);
        Assert.Equal(DaisyOS.Core.Models.NetworkConnectionKind.Ethernet, status.ConnectionKind);
        Assert.Equal(DaisyOS.Core.Models.NetworkConnectivity.Full, status.Connectivity);
    }

    [Fact]
    public async Task ConnectedWifiIncludesConnectivityAndActiveSignalWithoutRescan()
    {
        var runner = new ScriptedCommandRunner(
            new CommandResult(0, "wlan0:wifi:connected:Studio Wi-Fi", string.Empty, TimedOut: false),
            new CommandResult(0, "full", string.Empty, TimedOut: false),
            new CommandResult(0, "*:73\n:32", string.Empty, TimedOut: false));
        var service = new LinuxNetworkStatusService(runner);

        var status = await service.GetStatusAsync();

        Assert.Equal(DaisyOS.Core.Models.NetworkConnectionKind.WiFi, status.ConnectionKind);
        Assert.Equal(DaisyOS.Core.Models.NetworkConnectivity.Full, status.Connectivity);
        Assert.Equal(73, status.SignalPercent);
        Assert.Equal(["-t", "-f", "IN-USE,SIGNAL", "device", "wifi", "list", "--rescan", "no"], runner.Calls[2].Arguments);
    }

    [Fact]
    public async Task LimitedWifiConnectionPreservesItsConnectivityState()
    {
        var runner = new ScriptedCommandRunner(
            new CommandResult(0, "wlan0:wifi:connected:Studio Wi-Fi", string.Empty, TimedOut: false),
            new CommandResult(0, "limited", string.Empty, TimedOut: false),
            new CommandResult(0, "*:51", string.Empty, TimedOut: false));
        var service = new LinuxNetworkStatusService(runner);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsConnected);
        Assert.Equal(DaisyOS.Core.Models.NetworkConnectivity.Limited, status.Connectivity);
        Assert.Equal(51, status.SignalPercent);
    }
}
