using DaisyOS.System.Bluetooth;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxBluetoothServiceTests
{
    [Fact]
    public async Task GetStatusAsyncReportsShowFailureAsUnavailableWithoutFakeDevices()
    {
        var runner = new ScriptedCommandRunner(Failed("bluetoothctl not found"));
        var service = new LinuxBluetoothService(runner);

        var status = await service.GetStatusAsync();

        Assert.False(status.IsAvailable);
        Assert.False(status.IsEnabled);
        Assert.False(status.IsDiscovering);
        Assert.Empty(status.Devices);
        Assert.Contains("bluetoothctl not found", status.Detail);
    }

    [Fact]
    public async Task GetAdapterStatusAsyncNormalizesMissingControllerForHardwarePresentation()
    {
        var runner = new ScriptedCommandRunner(Failed("No default controller available"));
        var service = new LinuxBluetoothService(runner);

        var status = await service.GetAdapterStatusAsync();

        Assert.False(status.IsAvailable);
        Assert.Equal("Bluetooth isn’t available on this device.", status.Detail);
    }

    [Fact]
    public async Task GetStatusAsyncParsesAndDeduplicatesBluetoothDevices()
    {
        const string headphonesAddress = "AA:BB:CC:DD:EE:01";
        const string keyboardAddress = "AA:BB:CC:DD:EE:02";
        var runner = new ScriptedCommandRunner(
            Succeeded("Controller 00:11:22:33:44:55\nPowered: yes\nDiscovering: yes"),
            Succeeded($"Device {headphonesAddress} Headphones\nDevice {headphonesAddress} Duplicate\nDevice {keyboardAddress} Keyboard"),
            Succeeded($"Device {headphonesAddress} Headphones\nDevice {headphonesAddress} Duplicate\nDevice {keyboardAddress} Keyboard"),
            Succeeded("Connected: yes\nPaired: yes\nIcon: audio-headset"),
            Succeeded("Connected: no\nPaired: yes\nIcon: input-keyboard"));
        var service = new LinuxBluetoothService(runner);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.True(status.IsEnabled);
        Assert.True(status.IsDiscovering);
        Assert.Equal("2 Bluetooth devices found.", status.Detail);
        Assert.Collection(
            status.Devices,
            device =>
            {
                Assert.Equal(headphonesAddress, device.Address);
                Assert.Equal("Headphones", device.Name);
                Assert.True(device.IsConnected);
                Assert.True(device.IsPaired);
                Assert.Equal("Audio", device.Type);
            },
            device =>
            {
                Assert.Equal(keyboardAddress, device.Address);
                Assert.Equal("Keyboard", device.Name);
                Assert.False(device.IsConnected);
                Assert.True(device.IsPaired);
                Assert.Equal("Keyboard", device.Type);
            });
    }

    [Fact]
    public async Task GetStatusAsyncDoesNotClaimNoDevicesWhenDeviceQueryFails()
    {
        var runner = new ScriptedCommandRunner(
            Succeeded("Powered: yes\nDiscovering: no"),
            Failed("org.bluez unavailable"),
            Failed("org.bluez unavailable"));
        var service = new LinuxBluetoothService(runner);

        var status = await service.GetStatusAsync();

        Assert.True(status.IsAvailable);
        Assert.True(status.IsEnabled);
        Assert.Empty(status.Devices);
        Assert.Equal("Bluetooth is enabled, but devices could not be queried.", status.Detail);
    }

    [Fact]
    public async Task GetStatusAsyncDoesNotInventDeviceDetailsWhenInfoQueryFails()
    {
        const string address = "AA:BB:CC:DD:EE:03";
        var runner = new ScriptedCommandRunner(
            Succeeded("Powered: yes"),
            Succeeded(),
            Succeeded($"Device {address} Unknown device"),
            Failed("device disappeared"));
        var service = new LinuxBluetoothService(runner);

        var status = await service.GetStatusAsync();

        Assert.Empty(status.Devices);
    }

    [Theory]
    [InlineData(true, "on")]
    [InlineData(false, "off")]
    public async Task SetEnabledAsyncReturnsFalseWhenPowerCommandFails(bool enabled, string action)
    {
        var runner = new ScriptedCommandRunner(Failed("power command rejected"));
        var service = new LinuxBluetoothService(runner);

        var changed = await service.SetEnabledAsync(enabled);

        Assert.False(changed);
        var call = Assert.Single(runner.Calls);
        Assert.Equal("bluetoothctl", call.FileName);
        Assert.Equal(["power", action], call.Arguments);
    }

    [Fact]
    public async Task PairingReportsTrustAndConnectionFailures()
    {
        var trustFailure = new LinuxBluetoothService(new ScriptedCommandRunner(Succeeded(), Succeeded(), Failed("denied"), Succeeded()));
        Assert.False(await trustFailure.PairDeviceAsync("AA:BB:CC:DD:EE:11"));
        var connectionFailure = new LinuxBluetoothService(new ScriptedCommandRunner(
            Succeeded(), Succeeded(), Succeeded(), Succeeded("Connected: no"), Failed("unreachable"),
            Succeeded("Connected: no"), Succeeded()));
        Assert.False(await connectionFailure.PairDeviceAsync("AA:BB:CC:DD:EE:12"));
    }

    [Fact]
    public async Task MousePairingEnablesPairabilityAndAgentThenVerifiesConnection()
    {
        const string address = "AA:BB:CC:DD:EE:17";
        var runner = new ScriptedCommandRunner(
            Succeeded(), Succeeded("Pairing successful"), Succeeded(),
            Succeeded("Paired: yes\nConnected: yes\nIcon: input-mouse"),
            Succeeded("Paired: yes\nConnected: yes\nIcon: input-mouse"), Succeeded());

        Assert.True(await new LinuxBluetoothService(runner).PairDeviceAsync(address));

        Assert.Equal(["--timeout", "3", "pairable", "on"], runner.Calls[0].Arguments);
        Assert.Equal(["--agent", "NoInputNoOutput", "--timeout", "20", "pair", address], runner.Calls[1].Arguments);
        Assert.Equal(["--timeout", "3", "pairable", "off"], runner.Calls[^1].Arguments);
        Assert.DoesNotContain(runner.Calls, call => call.Arguments.Contains("connect"));
    }

    [Fact]
    public async Task ManualDisconnectSuppressesReconnectUntilExplicitConnect()
    {
        const string address = "AA:BB:CC:DD:EE:13";
        var runner = new ScriptedCommandRunner(
            Succeeded(), Succeeded("Connected: no"),
            Succeeded(), Succeeded(), Succeeded("Connected: yes"));
        var service = new LinuxBluetoothService(runner);
        Assert.True(await service.SetConnectedAsync(address, false));
        Assert.False(LinuxBluetoothService.CanAutoConnect(address));
        Assert.True(await service.SetConnectedAsync(address, true));
        Assert.True(LinuxBluetoothService.CanAutoConnect(address));
        Assert.True(runner.Calls[3].Timeout > TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task RecognizesRenamedAirPodsByServiceUuid()
    {
        var runner = new ScriptedCommandRunner(Succeeded("Powered: yes"),
            Succeeded("Device AA:BB:CC:DD:EE:14 My earbuds"),
            Succeeded("Device AA:BB:CC:DD:EE:14 My earbuds"),
            Succeeded("Paired: yes\nRSSI: -45\nUUID: 74ec2172-0bad-4d01-8f77-997b2be0722a"));
        var device = Assert.Single((await new LinuxBluetoothService(runner).GetStatusAsync()).Devices);
        Assert.Equal("AirPods", device.Type);
        Assert.True(device.IsNearby);
    }

    [Fact]
    public async Task ExplicitEnableCanRecoverASoftBlockedRadio()
    {
        var runner = new ScriptedCommandRunner(new CommandResult(1, "org.bluez.Error.Blocked", "", false), Succeeded(), Succeeded());
        Assert.True(await new LinuxBluetoothService(runner).SetEnabledAsync(true));
        Assert.Equal("rfkill", runner.Calls[1].FileName);
        Assert.Equal(["unblock", "bluetooth"], runner.Calls[1].Arguments);
        Assert.Equal(["power", "on"], runner.Calls[2].Arguments);
    }

    [Fact]
    public async Task PairedDevicesRemainVisibleWhenRadioIsOff()
    {
        const string address = "AA:BB:CC:DD:EE:15";
        var runner = new ScriptedCommandRunner(
            Succeeded("Powered: no\nDiscovering: no"),
            Succeeded($"Device {address} My Phone"),
            Succeeded("Paired: yes\nConnected: no\nIcon: phone"));

        var status = await new LinuxBluetoothService(runner).GetStatusAsync();

        Assert.False(status.IsEnabled);
        var device = Assert.Single(status.Devices);
        Assert.True(device.IsPaired);
        Assert.Equal("Phone", device.Type);
        Assert.Equal(["--timeout", "2", "devices", "Paired"], runner.Calls[1].Arguments);
    }

    [Fact]
    public async Task PersistentPairedListIsMergedWithNearbyDiscoveryWithoutDuplicates()
    {
        const string address = "AA:BB:CC:DD:EE:16";
        var runner = new ScriptedCommandRunner(
            Succeeded("Powered: yes"),
            Succeeded($"Device {address} Saved Headphones"),
            Succeeded($"Device {address} Saved Headphones"),
            Succeeded("Paired: yes\nConnected: no\nRSSI: -54\nIcon: audio-headset"));

        var device = Assert.Single((await new LinuxBluetoothService(runner).GetStatusAsync()).Devices);

        Assert.True(device.IsPaired);
        Assert.True(device.IsNearby);
        Assert.Equal("Audio", device.Type);
    }

    private static CommandResult Succeeded(string output = "") => new(0, output, string.Empty, TimedOut: false);

    private static CommandResult Failed(string error) => new(1, string.Empty, error, TimedOut: false);
}
