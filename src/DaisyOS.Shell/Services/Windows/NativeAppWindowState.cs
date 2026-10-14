namespace DaisyOS.Shell.Services.Windows;

/// <summary>Presentation state for a DaisyOS-owned application window.</summary>
public enum NativeAppWindowState
{
    NotRunning,
    RunningInactive,
    Active,
    Minimized
}

public sealed class NativeAppWindowStateChangedEventArgs(string appId, NativeAppWindowState state) : EventArgs
{
    public string AppId { get; } = appId;
    public NativeAppWindowState State { get; } = state;
}

/// <summary>
/// Testable state store shared by the native-window tracker and taskbar tests.
/// It keeps exactly one owned app active at a time.
/// </summary>
public sealed class NativeAppWindowStateStore
{
    private readonly Dictionary<string, NativeAppWindowState> _states = new(StringComparer.Ordinal);

    public event EventHandler<NativeAppWindowStateChangedEventArgs>? StateChanged;

    public NativeAppWindowState GetState(string appId) =>
        _states.TryGetValue(appId, out var state) ? state : NativeAppWindowState.NotRunning;

    public void Register(string appId) => SetState(appId, NativeAppWindowState.RunningInactive);

    public void Activate(string appId)
    {
        foreach (var activeAppId in _states
                     .Where(pair => pair.Value == NativeAppWindowState.Active && !string.Equals(pair.Key, appId, StringComparison.Ordinal))
                     .Select(pair => pair.Key)
                     .ToArray())
        {
            SetState(activeAppId, NativeAppWindowState.RunningInactive);
        }

        SetState(appId, NativeAppWindowState.Active);
    }

    public void Deactivate(string appId)
    {
        if (GetState(appId) == NativeAppWindowState.Active)
        {
            SetState(appId, NativeAppWindowState.RunningInactive);
        }
    }

    public void Minimize(string appId) => SetState(appId, NativeAppWindowState.Minimized);

    public void Restore(string appId) => SetState(appId, NativeAppWindowState.RunningInactive);

    public void Unregister(string appId)
    {
        if (_states.Remove(appId))
        {
            StateChanged?.Invoke(this, new NativeAppWindowStateChangedEventArgs(appId, NativeAppWindowState.NotRunning));
        }
    }

    private void SetState(string appId, NativeAppWindowState state)
    {
        if (GetState(appId) == state)
        {
            return;
        }

        _states[appId] = state;
        StateChanged?.Invoke(this, new NativeAppWindowStateChangedEventArgs(appId, state));
    }
}
