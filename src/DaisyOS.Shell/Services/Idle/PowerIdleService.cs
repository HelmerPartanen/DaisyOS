using System;
using System.IO;
using System.Threading;

namespace DaisyOS.Shell.Services.Idle;

/// <summary>
/// Owns every inactivity action of the session: turning the display off,
/// locking, and suspending. Each action uses its own ext-idle-notify-v1
/// notification so the compositor (not a polling timer) tracks idle time and
/// applications holding an idle inhibitor (video players, games) are honored.
/// </summary>
public sealed class PowerIdleService : IDisposable
{
    private readonly object _gate = new();
    private readonly DaisyIdleMonitorNative _lockMonitor = new();
    private readonly DaisyIdleMonitorNative _screenMonitor = new();
    private readonly DaisyIdleMonitorNative _suspendMonitor = new();
    private readonly Action _lock;
    private readonly Action _screenOff;
    private readonly Action _screenOn;
    private readonly Action _suspend;
    private readonly Func<bool> _suppressDisruptiveActions;
    private readonly Func<bool> _isOnBattery;
    private readonly Timer _powerSourceTimer;

    private PowerIdleConfiguration _configuration = PowerIdleConfiguration.Default;
    private bool _lockMonitoringPaused;
    private bool _screenIsOff;
    private bool _lastOnBattery;
    private bool _disposed;

    public PowerIdleService(
        Action lockSession,
        Action screenOff,
        Action screenOn,
        Action suspend,
        Func<bool>? suppressDisruptiveActions = null,
        Func<bool>? isOnBattery = null)
    {
        _lock = lockSession ?? throw new ArgumentNullException(nameof(lockSession));
        _screenOff = screenOff ?? throw new ArgumentNullException(nameof(screenOff));
        _screenOn = screenOn ?? throw new ArgumentNullException(nameof(screenOn));
        _suspend = suspend ?? throw new ArgumentNullException(nameof(suspend));
        _suppressDisruptiveActions = suppressDisruptiveActions ?? (() => false);
        _isOnBattery = isOnBattery ?? PowerSource.IsOnBattery;
        _lastOnBattery = _isOnBattery();
        // Power-source changes only matter for the suspend timeout. A slow
        // check avoids a permanent udev/DBus subscription for one value.
        _powerSourceTimer = new Timer(_ => CheckPowerSource(), null, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
    }

    public bool IsNativeSupported => _lockMonitor.IsSupported;
    public PowerIdleConfiguration Configuration { get { lock (_gate) return _configuration; } }

    /// <summary>Raised on the monitor thread when the user becomes active after an idle notification.</summary>
    public event EventHandler? Resumed;

    public void UpdateConfiguration(PowerIdleConfiguration configuration)
    {
        lock (_gate)
        {
            if (_disposed) return;
            _configuration = configuration;
            RestartAllLocked();
        }
    }

    /// <summary>Stops the lock timer while the lock screen is already visible.</summary>
    public void PauseLockMonitoring()
    {
        lock (_gate)
        {
            _lockMonitoringPaused = true;
            _lockMonitor.Stop();
        }
    }

    /// <summary>Restarts the lock timer after unlocking.</summary>
    public void ResumeLockMonitoring()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lockMonitoringPaused = false;
            StartLockLocked();
        }
    }

    private void RestartAllLocked()
    {
        StartLockLocked();

        _screenMonitor.Stop();
        if (_configuration.ScreenOffSeconds > 0)
            _screenMonitor.Start(_configuration.ScreenOffSeconds * 1000, OnScreenIdle, OnScreenResumed);

        _suspendMonitor.Stop();
        var suspendSeconds = _lastOnBattery ? _configuration.SuspendOnBatterySeconds : _configuration.SuspendSeconds;
        if (suspendSeconds > 0)
            _suspendMonitor.Start(suspendSeconds * 1000, OnSuspendIdle, OnGenericResumed);
    }

    private void StartLockLocked()
    {
        _lockMonitor.Stop();
        if (_configuration.LockSeconds > 0 && !_lockMonitoringPaused)
            _lockMonitor.Start(_configuration.LockSeconds * 1000, OnLockIdle, OnGenericResumed);
    }

    // Native callbacks run on the monitor's Wayland thread. They must never
    // stop/start a monitor directly (that would join the calling thread).
    // ext-idle-notify re-arms itself after every resume, so no restart is needed.

    private void OnLockIdle()
    {
        if (Volatile.Read(ref _disposed)) return;
        _lock();
    }

    private void OnScreenIdle()
    {
        if (Volatile.Read(ref _disposed) || _suppressDisruptiveActions()) return;
        Volatile.Write(ref _screenIsOff, true);
        _screenOff();
    }

    private void OnScreenResumed()
    {
        if (Volatile.Read(ref _screenIsOff))
        {
            Volatile.Write(ref _screenIsOff, false);
            _screenOn();
        }
        OnGenericResumed();
    }

    private void OnSuspendIdle()
    {
        if (Volatile.Read(ref _disposed) || _suppressDisruptiveActions()) return;
        _suspend();
    }

    private void OnGenericResumed() => Resumed?.Invoke(this, EventArgs.Empty);

    private void CheckPowerSource()
    {
        var onBattery = _isOnBattery();
        lock (_gate)
        {
            if (_disposed || onBattery == _lastOnBattery) return;
            _lastOnBattery = onBattery;
            if (_configuration.SuspendSeconds == _configuration.SuspendOnBatterySeconds) return;
            _suspendMonitor.Stop();
            var suspendSeconds = onBattery ? _configuration.SuspendOnBatterySeconds : _configuration.SuspendSeconds;
            if (suspendSeconds > 0)
                _suspendMonitor.Start(suspendSeconds * 1000, OnSuspendIdle, OnGenericResumed);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _powerSourceTimer.Dispose();
        _lockMonitor.Dispose();
        _screenMonitor.Dispose();
        _suspendMonitor.Dispose();
    }
}

public sealed record PowerIdleConfiguration(
    uint LockSeconds,
    uint ScreenOffSeconds,
    uint SuspendSeconds,
    uint SuspendOnBatterySeconds,
    bool LockOnSuspend)
{
    public static PowerIdleConfiguration Default { get; } = new(600, 300, 0, 900, true);

    public static PowerIdleConfiguration FromPreferences(Func<string, string, string> get)
    {
        // Older builds stored the lock timeout as "power.screen-timeout"; honor it
        // until the user picks a value on the new Power page.
        var legacyLock = get(PowerPreferences.LegacyScreenTimeoutKey, PowerPreferences.DefaultLockTimeout);
        return new PowerIdleConfiguration(
            PowerPreferences.ParseSeconds(get(PowerPreferences.LockTimeoutKey, legacyLock), Default.LockSeconds),
            PowerPreferences.ParseSeconds(get(PowerPreferences.ScreenOffTimeoutKey, PowerPreferences.DefaultScreenOffTimeout), Default.ScreenOffSeconds),
            PowerPreferences.ParseSeconds(get(PowerPreferences.SuspendTimeoutKey, PowerPreferences.DefaultSuspendTimeout), Default.SuspendSeconds),
            PowerPreferences.ParseSeconds(get(PowerPreferences.SuspendOnBatteryTimeoutKey, PowerPreferences.DefaultSuspendOnBatteryTimeout), Default.SuspendOnBatterySeconds),
            PowerPreferences.ParseBool(get(PowerPreferences.LockOnSuspendKey, "true"), true));
    }
}

/// <summary>Reads the AC adapter state from sysfs.</summary>
public static class PowerSource
{
    private const string PowerSupplyRoot = "/sys/class/power_supply";

    /// <summary>True only when a battery exists and no mains/USB supply reports online.</summary>
    public static bool IsOnBattery()
    {
        try
        {
            if (!Directory.Exists(PowerSupplyRoot)) return false;
            var hasBattery = false;
            foreach (var supply in Directory.EnumerateDirectories(PowerSupplyRoot))
            {
                var type = ReadValue(Path.Combine(supply, "type"));
                if (string.Equals(type, "Battery", StringComparison.OrdinalIgnoreCase))
                {
                    // Peripheral batteries (mice, headsets) report scope=Device.
                    if (!string.Equals(ReadValue(Path.Combine(supply, "scope")), "Device", StringComparison.OrdinalIgnoreCase))
                        hasBattery = true;
                }
                else if (type is "Mains" or "USB" or "USB_C" or "USB_PD" &&
                         ReadValue(Path.Combine(supply, "online")) == "1")
                {
                    return false;
                }
            }
            return hasBattery;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ReadValue(string path)
    {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }
}
