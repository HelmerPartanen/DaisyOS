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
            Succeeded($"Device {address} Unknown device"),
            Failed("device disappeared"));
        var service = new LinuxBluetoothService(runner);

        var status = await service.GetStatusAsync();

        var device = Assert.Single(status.Devices);
        Assert.False(device.IsConnected);
        Assert.False(device.IsPaired);
        Assert.Equal("Unknown", device.Type);
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

    private static CommandResult Succeeded(string output = "") => new(0, output, string.Empty, TimedOut: false);

    private static CommandResult Failed(string error) => new(1, string.Empty, error, TimedOut: false);
}
