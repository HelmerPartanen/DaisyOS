using System;

namespace DaisyOS.Shell.Services.Idle;

/// <summary>
/// Monitors desktop idle time and triggers session lock according to user preferences.
/// </summary>
public sealed class IdleLockService : IDisposable
{
    private readonly DaisyIdleMonitorNative _nativeMonitor;
    private readonly Action _lockSessionAction;
    private uint _currentTimeoutSeconds;
    private bool _lockOnSleep = true;
    private bool _isDisposed;

    public uint TimeoutSeconds => _currentTimeoutSeconds;
    public bool LockOnSleep => _lockOnSleep;
    public bool IsNativeSupported => _nativeMonitor.IsSupported;
    public bool IsRunning { get; private set; }
    public event EventHandler? Resumed;

    public IdleLockService(Action lockSessionAction)
    {
        _lockSessionAction = lockSessionAction ?? throw new ArgumentNullException(nameof(lockSessionAction));
        _nativeMonitor = new DaisyIdleMonitorNative();
    }

    public static uint ParseTimeoutSeconds(string? timeoutPreference) => timeoutPreference switch
    {
        "5 minutes" => 300,
        "10 minutes" => 600,
        "15 minutes" => 900,
        "Never" => 0,
        _ => 600 // Default 10 minutes
    };

    public void UpdateConfiguration(string? timeoutPreference, bool lockOnSleep)
    {
        var seconds = ParseTimeoutSeconds(timeoutPreference);
        UpdateConfiguration(seconds, lockOnSleep);
    }

    public void UpdateConfiguration(uint timeoutSeconds, bool lockOnSleep)
    {
        _currentTimeoutSeconds = timeoutSeconds;
        _lockOnSleep = lockOnSleep;

        if (_isDisposed) return;

        if (_currentTimeoutSeconds > 0 && _lockOnSleep)
        {
            StartMonitoring();
        }
        else
        {
            StopMonitoring();
        }
    }

    public void StartMonitoring()
    {
        if (_currentTimeoutSeconds == 0 || !_lockOnSleep || _isDisposed)
        {
            return;
        }

        if (_nativeMonitor.IsSupported)
        {
            uint timeoutMs = _currentTimeoutSeconds * 1000;
            IsRunning = _nativeMonitor.Start(timeoutMs, OnIdleDetected, OnResumed);
        }
    }

    public void StopMonitoring()
    {
        _nativeMonitor.Stop();
        IsRunning = false;
    }

    private void OnIdleDetected()
    {
        if (_lockOnSleep && _currentTimeoutSeconds > 0)
        {
            _lockSessionAction();
        }
    }

    private void OnResumed()
    {
        Resumed?.Invoke(this, EventArgs.Empty);
        if (_lockOnSleep && _currentTimeoutSeconds > 0)
        {
            StopMonitoring();
            StartMonitoring();
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        StopMonitoring();
        _nativeMonitor.Dispose();
    }
}
