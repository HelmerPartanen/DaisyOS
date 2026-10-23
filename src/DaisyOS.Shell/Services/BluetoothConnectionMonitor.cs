using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.System.Bluetooth;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

/// <summary>Watches BlueZ for device connection transitions throughout the shell session.</summary>
public sealed class BluetoothConnectionMonitor : IDisposable
{
    private readonly IBluetoothService _bluetooth;
    private readonly BluetoothConnectionNotificationTracker _notifications;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private bool _refreshing;

    public BluetoothConnectionMonitor(INotificationService notifications)
        : this(new LinuxBluetoothService(new SafeCommandRunner()), notifications)
    {
    }

    internal BluetoothConnectionMonitor(IBluetoothService bluetooth, INotificationService notifications)
    {
        _bluetooth = bluetooth;
        _notifications = new BluetoothConnectionNotificationTracker(notifications);
        _timer.Tick += OnTick;
    }

    public void Start()
    {
        _timer.Start();
        _ = RefreshAsync();
    }

    private async void OnTick(object? sender, EventArgs e) => await RefreshAsync();

    internal async Task RefreshAsync()
    {
        if (_refreshing || _lifetime.IsCancellationRequested) return;
        _refreshing = true;
        try
        {
            _notifications.Observe(await _bluetooth.GetStatusAsync(_lifetime.Token));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"Bluetooth connection monitor: {ex.Message}");
        }
        finally
        {
            _refreshing = false;
        }
    }

    public void Dispose()
    {
        _timer.Stop();
        _timer.Tick -= OnTick;
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}

public sealed class BluetoothConnectionNotificationTracker
{
    private readonly INotificationService _notifications;
    private Dictionary<string, bool> _connectionStates = new(StringComparer.OrdinalIgnoreCase);
    private bool _hasBaseline;

    public BluetoothConnectionNotificationTracker(INotificationService notifications) =>
        _notifications = notifications;

    public void Observe(BluetoothStatus status)
    {
        // A failed BlueZ query must not erase the baseline and cause every already
        // connected accessory to be announced when the service recovers.
        if (!status.IsAvailable) return;

        var devices = status.Devices
            .Where(device => !LinuxBluetoothService.IsAnonymousName(device.Name))
            .GroupBy(device => device.Address, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var currentStates = devices.ToDictionary(
            device => device.Address,
            device => device.IsConnected,
            StringComparer.OrdinalIgnoreCase);

        if (_hasBaseline)
        {
            foreach (var device in devices.Where(device => device.IsConnected))
            {
                if (_connectionStates.TryGetValue(device.Address, out var wasConnected) && wasConnected) continue;

                _notifications.Add(new NotificationItem(
                    $"Connected to {device.Name}",
                    "",
                    DateTimeOffset.Now,
                    SystemIconGlyph: BluetoothDevicePresentation.IconFor(device),
                    Category: NotificationCategory.Network,
                    TimeoutSeconds: 5,
                    SourceAppName: "Bluetooth"));
            }
        }

        _connectionStates = currentStates;
        _hasBaseline = true;
    }
}

public static class BluetoothDevicePresentation
{
    public static string IconFor(BluetoothDevice device)
    {
        var type = device.Type;
        var name = device.Name;
        if (Contains(type, "AirPods") || Contains(name, "AirPods")) return "AirPods";
        if (Contains(type, "audio") || Contains(type, "headphone") || Contains(type, "headset")
            || Contains(name, "bud") || Contains(name, "headphone")) return "headphones";
        if (Contains(type, "keyboard") || Contains(name, "keyboard") || Contains(name, "keychron")) return "keyboard";
        if (Contains(type, "mouse") || Contains(name, "mouse") || Contains(name, "master") || Contains(name, "trackpad")) return "mouse";
        if (Contains(type, "phone") || Contains(name, "phone")) return "device-mobile";
        if (Contains(type, "controller") || Contains(type, "gamepad") || Contains(name, "controller") || Contains(name, "dualsense")) return "game-controller";
        return "bluetooth";
    }

    private static bool Contains(string value, string fragment) =>
        value.Contains(fragment, StringComparison.OrdinalIgnoreCase);
}
