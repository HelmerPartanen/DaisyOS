using DaisyOS.System.Gaming;
using DaisyOS.Shell.Services.Diagnostics;

namespace DaisyOS.Shell.Services;

/// <summary>
/// Mirrors DaisyOS session preferences and active state to the user gaming
/// daemon. Process detection and temporary system policy live in the daemon.
/// </summary>
internal sealed class GamingModeAutomation : IDisposable
{
    public const string AutomaticPreferenceKey = "gaming.auto-enable";
    private const string ManualPreferenceKey = "gaming.manual-enabled";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(2);

    private readonly ShellSessionState _state;
    private readonly Func<bool> _automaticEnabled;
    private readonly DaisyGamingClient _client = new();
    private readonly GameFrameTelemetryController _frameTelemetry = new();
    private readonly Func<bool> _telemetryVisible;
    private readonly Func<int> _telemetryWindowPid;
    private readonly CancellationTokenSource _lifetime = new();
    private int _lastObservedGamingMode;
    private int _pendingManualUpdate;
    private int _pendingManualValue;
    private int _stopping;
    private DateTimeOffset _lastErrorLog;

    public GamingModeAutomation(ShellSessionState state, Func<bool> automaticEnabled,
        Func<bool>? telemetryVisible = null, Func<int>? telemetryWindowPid = null)
    {
        _state = state;
        _automaticEnabled = automaticEnabled;
        _telemetryVisible = telemetryVisible ?? (() => false);
        _telemetryWindowPid = telemetryWindowPid ?? (() => 0);
        _lastObservedGamingMode = state.GamingMode ? 1 : 0;
    }

    public void Start()
    {
        if (string.IsNullOrEmpty(_state.GetPreference(ManualPreferenceKey)))
            _state.SetPreference(ManualPreferenceKey, _state.GamingMode ? "true" : "false");
        _state.Changed += OnStateChanged;
        _ = Task.Run(() => PollAsync(_lifetime.Token));
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        var current = _state.GamingMode ? 1 : 0;
        var previous = Interlocked.Exchange(ref _lastObservedGamingMode, current);
        if (previous == current || Volatile.Read(ref _stopping) != 0) return;
        Volatile.Write(ref _pendingManualValue, current);
        Volatile.Write(ref _pendingManualUpdate, 1);
        _state.SetPreference(ManualPreferenceKey, current == 1 ? "true" : "false");
    }

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        bool? lastManual = null;
        (bool Automatic, bool Battery, bool Performance)? lastConfig = null;
        var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    var configuration = (
                        Automatic: _automaticEnabled(),
                        Battery: _state.GetPreference(GamingPowerPolicy.BatteryBoostPreferenceKey, "false") == "true",
                        Performance: _state.GetPreference(GamingPowerPolicy.PerformanceProfilePreferenceKey, "true") != "false");
                    if (lastConfig != configuration)
                    {
                        await _client.ConfigureAsync(configuration.Automatic, configuration.Battery,
                            configuration.Performance, cancellationToken).ConfigureAwait(false);
                        lastConfig = configuration;
                    }

                    var manual = _state.GetPreference(ManualPreferenceKey,
                        _state.GamingMode ? "true" : "false") == "true";
                    var userInitiated = Volatile.Read(ref _pendingManualUpdate) != 0;
                    if (userInitiated)
                        manual = Volatile.Read(ref _pendingManualValue) != 0;
                    if (lastManual != manual || userInitiated)
                    {
                        await _client.SetManualEnabledAsync(manual, userInitiated, cancellationToken).ConfigureAwait(false);
                        lastManual = manual;
                        if (userInitiated && Volatile.Read(ref _pendingManualValue) == (manual ? 1 : 0))
                            Interlocked.Exchange(ref _pendingManualUpdate, 0);
                    }

                    var active = await _client.GetGamingActiveAsync(cancellationToken).ConfigureAwait(false);
                    var desired = active ? 1 : 0;
                    if (Interlocked.Exchange(ref _lastObservedGamingMode, desired) != desired)
                        _state.GamingMode = active;
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _client.ResetConnection();
                    lastConfig = null;
                    lastManual = null;
                    if (DateTimeOffset.UtcNow - _lastErrorLog >= TimeSpan.FromSeconds(30))
                    {
                        Console.Error.WriteLine($"[gaming-mode] Gaming daemon is unavailable: {ex.Message}");
                        _lastErrorLog = DateTimeOffset.UtcNow;
                    }
                }
                try { await _frameTelemetry.UpdateAsync(_telemetryVisible(), _telemetryWindowPid(), cancellationToken).ConfigureAwait(false); }
                catch (Exception ex) when (ex is not OperationCanceledException)
                { Console.Error.WriteLine($"[gaming-mode] Could not update frame telemetry: {ex.Message}"); }
            } while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) { }
        finally
        {
            timer.Dispose();
            await _frameTelemetry.UpdateAsync(false, 0, CancellationToken.None).ConfigureAwait(false);
            _client.Dispose();
            _lifetime.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _state.Changed -= OnStateChanged;
        _lifetime.Cancel();
    }
}
