using DaisyOS.System.Networking;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxWirelessNetworkServiceTests
{
    [Fact]
    public async Task GetNetworksAsyncParsesEscapedFieldsAndDeduplicatesSsids()
    {
        var output = string.Join(
            '\n',
            ":Cafe\\:Guest:WPA2:35",
            "*:Cafe\\:Guest:WPA2:80",
            ":Backslash\\\\WiFi::125",
            ":Weak:WEP:-10");
        var runner = new ScriptedCommandRunner(
            Succeeded("wlan0:wifi\neth0:ethernet"),
            Succeeded(output));
        var service = new LinuxWirelessNetworkService(runner);

        var status = await service.GetNetworksAsync();

        Assert.True(status.IsAvailable);
        Assert.Equal("3 Wi-Fi networks found.", status.Detail);
        Assert.Collection(
            status.Networks,
            network =>
            {
                Assert.Equal("Cafe:Guest", network.Ssid);
                Assert.Equal(80, network.SignalPercent);
                Assert.Equal("WPA2", network.Security);
                Assert.True(network.IsActive);
            },
            network =>
            {
                Assert.Equal("Backslash\\WiFi", network.Ssid);
                Assert.Equal(100, network.SignalPercent);
                Assert.Equal("Open", network.Security);
                Assert.False(network.IsActive);
            },
            network =>
            {
                Assert.Equal("Weak", network.Ssid);
                Assert.Equal(0, network.SignalPercent);
            });
        Assert.Equal(["-t", "-f", "DEVICE,TYPE", "device", "status"], runner.Calls[0].Arguments);
    }

    [Fact]
    public async Task GetNetworksAsyncReportsCommandFailureAsUnavailable()
    {
        var runner = new ScriptedCommandRunner(Failed("NetworkManager is not running"));
        var service = new LinuxWirelessNetworkService(runner);

        var status = await service.GetNetworksAsync();

        Assert.False(status.IsAvailable);
        Assert.Empty(status.Networks);
        Assert.Contains("NetworkManager is not running", status.Detail);
    }

    [Fact]
    public async Task GetNetworksAsyncHidesWiFiWhenNoReceiverExists()
    {
        var runner = new ScriptedCommandRunner(Succeeded("eth0:ethernet\nlo:loopback"));
        var service = new LinuxWirelessNetworkService(runner);

        var status = await service.GetNetworksAsync();

        Assert.False(status.IsAvailable);
        Assert.Empty(status.Networks);
        Assert.Equal("Wi-Fi isn’t available on this device.", status.Detail);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task ConnectToNetworkAsyncReturnsFalseWhenNmcliFails()
    {
        var runner = new ScriptedCommandRunner(Failed("invalid password"));
        var service = new LinuxWirelessNetworkService(runner);

        var connected = await service.ConnectToNetworkAsync("Office", "secret");

        Assert.False(connected);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("nmcli", call.FileName);
        Assert.Equal(["device", "wifi", "connect", "Office", "password", "secret"], call.Arguments);
    }

    [Fact]
    public async Task DisconnectFromNetworkAsyncReturnsFalseWhenDeviceQueryFails()
    {
        var runner = new ScriptedCommandRunner(Failed("NetworkManager unavailable"));
        var service = new LinuxWirelessNetworkService(runner);

        var disconnected = await service.DisconnectFromNetworkAsync();

        Assert.False(disconnected);
        Assert.Single(runner.Calls);
    }

    [Fact]
    public async Task DisconnectFromNetworkAsyncParsesEscapedDeviceAndReturnsFalseWhenDisconnectFails()
    {
        var runner = new ScriptedCommandRunner(
            Succeeded("wlxusb\\:0:wifi:connected\neth0:ethernet:connected"),
            Failed("disconnect rejected"));
        var service = new LinuxWirelessNetworkService(runner);

        var disconnected = await service.DisconnectFromNetworkAsync();

        Assert.False(disconnected);
        Assert.Equal(2, runner.Calls.Count);
        Assert.Equal(["device", "disconnect", "wlxusb:0"], runner.Calls[1].Arguments);
    }

    private static CommandResult Succeeded(string output = "") => new(0, output, string.Empty, TimedOut: false);

    private static CommandResult Failed(string error) => new(10, string.Empty, error, TimedOut: false);
}
