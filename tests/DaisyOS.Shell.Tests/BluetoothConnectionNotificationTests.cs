using DaisyOS.Core.Models;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Notifications;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class BluetoothConnectionNotificationTests
{
    [Fact]
    public void AnnouncesOnlyNewConnectionTransitionsAndUsesTheDeviceIcon()
    {
        var notifications = new NotificationService();
        var tracker = new BluetoothConnectionNotificationTracker(notifications);
        var mouse = Device("AA:BB:CC:DD:EE:01", "MX Master 3S", false, "Mouse");

        tracker.Observe(Status(mouse));
        Assert.Empty(notifications.GetNotifications());

        tracker.Observe(Status(mouse with { IsConnected = true }));
        var notification = Assert.Single(notifications.GetNotifications());
        Assert.Equal("Connected to MX Master 3S", notification.Title);
        Assert.Equal("Bluetooth", notification.DisplaySource);
        Assert.Equal("mouse", notification.SystemIconGlyph);

        tracker.Observe(Status(mouse with { IsConnected = true }));
        Assert.Single(notifications.GetNotifications());
    }

    [Fact]
    public void ExistingStartupConnectionsDoNotProduceToasts()
    {
        var notifications = new NotificationService();
        var tracker = new BluetoothConnectionNotificationTracker(notifications);

        tracker.Observe(Status(Device("AA:BB:CC:DD:EE:02", "Keychron K3", true, "Keyboard")));

        Assert.Empty(notifications.GetNotifications());
    }

    [Theory]
    [InlineData("AirPods Pro", "Unknown", "AirPods")]
    [InlineData("Daisy Buds", "Audio", "headphones")]
    [InlineData("Keychron K3", "Keyboard", "keyboard")]
    [InlineData("Magic Trackpad", "Input", "mouse")]
    [InlineData("Pixel Phone", "Phone", "device-mobile")]
    [InlineData("DualSense Wireless Controller", "Controller", "game-controller")]
    [InlineData("Accessory", "Unknown", "bluetooth")]
    public void ResolvesSemanticIconForDevice(string name, string type, string expectedIcon)
    {
        Assert.Equal(expectedIcon, BluetoothDevicePresentation.IconFor(Device("AA:BB:CC:DD:EE:03", name, true, type)));
    }

    private static BluetoothDevice Device(string address, string name, bool connected, string type) =>
        new(address, name, connected, true, type, true);

    private static BluetoothStatus Status(params BluetoothDevice[] devices) =>
        new(true, true, false, devices, "Available");
}
