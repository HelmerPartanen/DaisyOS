using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.System.Bluetooth;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Services;

/// <summary>Session-owned monitoring continues when quick settings is closed.</summary>
public sealed class AirPodsCoordinator : IDisposable
{
    private readonly App _app;
    private readonly LibrePodsService _pods = new();
    private readonly LinuxBluetoothService _bluetooth = new(new SafeCommandRunner());
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(3) };
    private readonly Dictionary<string, DateTimeOffset> _nextAttempt = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _prompted = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _connectionGate = new(1, 1);
    private DateTimeOffset _nextBluetoothQuery;
    private DateTimeOffset _nextPairedRestore;
    private Task? _pairedRestoreTask;
    private AirPodsStatus? _bluetoothFallback;
    private bool _refreshing;
    private bool _changingMode;
    private string? _announcedAddress;
    public AirPodsStatus Status { get; private set; } = AirPodsStatus.Unavailable;
    public event Action? Changed;

    public AirPodsCoordinator(App app) { _app = app; _timer.Tick += OnTick; }
    public void Start() { _timer.Start(); _ = StartCompanionAsync(); }
    private async Task StartCompanionAsync()
    {
        try
        {
            QueuePairedDeviceRestore();
            var userBinary = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "lib", "daisyos", "librepods");
            var hasCompanion = File.Exists("/usr/lib/daisyos/librepods") || File.Exists(userBinary);
            if (hasCompanion)
            {
                var runner = new SafeCommandRunner();
                var startResult = await runner.RunAsync("systemctl", ["--user", "start", "daisyos-librepods.service"],
                    TimeSpan.FromSeconds(5), _lifetime.Token);
                if (!startResult.Succeeded)
                {
                    var bin = File.Exists("/usr/lib/daisyos/librepods") ? "/usr/lib/daisyos/librepods" : userBinary;
                    _ = runner.RunAsync(bin, ["--hide"], TimeSpan.FromHours(24), _lifetime.Token);
                }
            }
            await RefreshAsync();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }
    private async void OnTick(object? sender, EventArgs e) => await RefreshAsync();

    public async Task RefreshAsync()
    {
        if (_refreshing || _lifetime.IsCancellationRequested) return;
        _refreshing = true;
        try
        {
            // BlueZ handles device-initiated reconnects for trusted accessories. This
            // periodic pass also covers phones and headsets that expect the host to
            // initiate after they are switched on later in the session.
            QueuePairedDeviceRestore();
            var status = await _pods.GetStatusAsync(_lifetime.Token);
            if (!status.IsConnected && (DateTimeOffset.UtcNow >= _nextBluetoothQuery || (Status.IsConnected && _bluetoothFallback is null)))
            {
                _nextBluetoothQuery = DateTimeOffset.UtcNow.AddSeconds(15);
                var bluetooth = await _bluetooth.GetStatusAsync(_lifetime.Token);
                var devices = bluetooth.Devices.Where(IsAirPods).Select(device => device with
                {
                    // BlueZ keeps RSSI on cached Device objects after advertisements
                    // disappear. Only the companion's current manufacturer-data sighting
                    // proves that the case is actually advertising right now.
                    IsNearby = HasCurrentAdvertisement(device, status)
                }).ToArray();
                var connected = devices.FirstOrDefault(d => d.IsConnected);
                _bluetoothFallback = null;
                if (connected is not null)
                {
                    status = status with { IsAvailable = false, IsConnected = true, Address = connected.Address, Name = connected.Name,
                        NoiseMode = null, SupportedModes = [], Battery = "" };
                    _bluetoothFallback = status;
                }
                else if (bluetooth.IsEnabled)
                {
                    foreach (var device in devices.Where(d => d.IsNearby && !d.IsConnected))
                    {
                        if (device.IsPaired && LinuxBluetoothService.CanAutoConnect(device.Address)
                            && (!_nextAttempt.TryGetValue(device.Address, out var next) || next <= DateTimeOffset.UtcNow))
                        {
                            _nextAttempt[device.Address] = DateTimeOffset.UtcNow.AddMinutes(2);
                            if (await ConnectAsync(device.Address, automatic: true)) break;
                        }
                        else if (!device.IsPaired) Prompt(device);
                    }
                }
            }
            else if (!status.IsConnected && _bluetoothFallback is { } fallback)
                status = fallback;
            else if (status.IsConnected) _bluetoothFallback = null;
            Status = status;
            if (Status.IsConnected && !LinuxBluetoothService.IsAnonymousName(Status.Name) && !string.IsNullOrWhiteSpace(Status.Name))
            {
                if (!string.Equals(_announcedAddress, Status.Address, StringComparison.OrdinalIgnoreCase))
                {
                    // LibrePods and BlueZ signals can arrive out of order. Never
                    // announce an AirPods connection merely because some Bluetooth
                    // device connected; confirm the exact address and AirPods UUID.
                    var bluetooth = await _bluetooth.GetStatusAsync(_lifetime.Token);
                    var confirmed = FindConfirmedConnectedAirPods(Status, bluetooth.Devices);
                    if (confirmed is not null)
                    {
                        Status = Status with { Name = confirmed.Name };
                        _announcedAddress = confirmed.Address;
                        await SelectAudioOutputAsync(confirmed.Name);
                        _app.SessionState.SetPreference("airpods.last-address", confirmed.Address);
                        // The session-wide Bluetooth monitor announces this connection.
                        // Keep AirPods-specific work here limited to audio routing and state.
                    }
                }
                _prompted.Remove(Status.Address);
            }
            else if (!Status.IsConnected) _announcedAddress = null;
            Changed?.Invoke();
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
        catch (Exception ex)
        {
            global::System.Diagnostics.Debug.WriteLine($"AirPods status: {ex.Message}");
        }
        finally { _refreshing = false; }
    }

    private async Task SelectAudioOutputAsync(string name)
    {
        var audio = new DaisyOS.System.Audio.LinuxAudioService(new SafeCommandRunner());
        // PipeWire may publish the sink shortly after BlueZ reports Connected.
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var devices = await audio.GetAudioDevicesAsync(_lifetime.Token);
            var device = devices.FirstOrDefault(d => d.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (device is not null)
            {
                if (!device.IsDefault) await audio.SetDefaultAudioDeviceAsync(device.Id, _lifetime.Token);
                return;
            }
            await Task.Delay(400, _lifetime.Token);
        }
    }

    private bool IsAirPods(BluetoothDevice device) =>
        !LinuxBluetoothService.IsAnonymousName(device.Name) &&
        (device.Type == "AirPods" || device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase));

    internal static BluetoothDevice? FindConfirmedConnectedAirPods(
        AirPodsStatus status,
        IEnumerable<BluetoothDevice> devices) =>
        devices.FirstOrDefault(device => device.IsConnected
            && device.Address.Equals(status.Address, StringComparison.OrdinalIgnoreCase)
            && (device.Type.Equals("AirPods", StringComparison.OrdinalIgnoreCase)
                || device.Name.Contains("AirPods", StringComparison.OrdinalIgnoreCase)));

    private void Prompt(BluetoothDevice device)
    {
        // Pairing prompts are discovery affordances, not reconnect reminders. Paired
        // AirPods reconnect silently through the trusted-device path above.
        if (!ShouldOfferPairingPrompt(device) || !_prompted.Add(device.Address)) return;
        _app.Notifications.Add(new NotificationItem($"Connect to \"{device.Name}\"?",
            "AirPods in pairing mode were detected nearby.", DateTimeOffset.Now,
            SystemIconGlyph: "AirPods", Category: NotificationCategory.Network,
            ActionLabel: "Connect", ActionId: "airpods-connect:" + device.Address, TimeoutSeconds: 12));
    }

    internal static bool ShouldOfferPairingPrompt(BluetoothDevice device) =>
        !device.IsPaired && device.IsNearby && !LinuxBluetoothService.IsAnonymousName(device.Name);

    internal static bool HasCurrentAdvertisement(BluetoothDevice device, AirPodsStatus proximity) =>
        !string.IsNullOrWhiteSpace(proximity.NearbyAddress)
        && device.Address.Equals(proximity.NearbyAddress, StringComparison.OrdinalIgnoreCase);

    private void QueuePairedDeviceRestore()
    {
        var now = DateTimeOffset.UtcNow;
        if (now < _nextPairedRestore || _pairedRestoreTask is { IsCompleted: false }) return;
        _nextPairedRestore = now.AddMinutes(1);
        _pairedRestoreTask = Task.Run(async () =>
        {
            try { await _bluetooth.RestorePairedDevicesAsync(_lifetime.Token); }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
            catch (Exception ex) { global::System.Diagnostics.Debug.WriteLine($"Bluetooth reconnect: {ex.Message}"); }
        });
    }

    public async Task<bool> ConnectAsync(string address, bool automatic = false)
    {
        if (!await _connectionGate.WaitAsync(0)) return false;
        try
        {
            var status = await _bluetooth.GetStatusAsync(_lifetime.Token);
            var device = status.Devices.FirstOrDefault(d => d.Address.Equals(address, StringComparison.OrdinalIgnoreCase));
            if (device is null || !status.IsEnabled) return false;
            if (automatic)
            {
                var proximity = await _pods.GetStatusAsync(_lifetime.Token);
                if (!device.IsPaired || (!device.IsNearby && !address.Equals(proximity.NearbyAddress, StringComparison.OrdinalIgnoreCase))
                    || !LinuxBluetoothService.CanAutoConnect(address)) return false;
            }
            var success = device.IsConnected || (device.IsPaired
                ? await _bluetooth.SetConnectedAsync(address, true, _lifetime.Token)
                : await _bluetooth.PairDeviceAsync(address, _lifetime.Token));
            if (!success && !automatic) _app.Feedback.Show("Couldn’t connect to your AirPods. Keep them nearby and try again.");
            _nextBluetoothQuery = DateTimeOffset.MinValue;
            return success;
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        finally { _connectionGate.Release(); }
    }

    public async Task<bool> SetModeAsync(AirPodsNoiseMode mode)
    {
        if (_changingMode || !Status.IsConnected || !Status.SupportedModes.Contains(mode)) return false;
        _changingMode = true;
        try { return await _pods.SetNoiseModeAsync(Status.Address, mode, _lifetime.Token); }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { return false; }
        finally { _changingMode = false; await RefreshAsync(); }
    }

    public void Dispose() { _timer.Stop(); _timer.Tick -= OnTick; _lifetime.Cancel(); }
}
