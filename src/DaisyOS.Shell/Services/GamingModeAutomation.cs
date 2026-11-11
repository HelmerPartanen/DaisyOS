using Avalonia.Threading;
using DaisyOS.System.Gaming;
using DaisyOS.Shell.Services.Diagnostics;

namespace DaisyOS.Shell.Services;

/// <summary>
/// Detects Steam/Lutris games and owns GameMode requests. Process discovery
/// and native GameMode calls stay off the input thread.
/// </summary>
internal sealed class GamingModeAutomation : IDisposable
{
    public const string AutomaticPreferenceKey = "gaming.auto-enable";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly ShellSessionState _state;
    private readonly Func<bool> _automaticEnabled;
    private readonly GameModeController _controller;
    private readonly GameProcessDetector _detector;
    private readonly GameFrameTelemetryController _frameTelemetry = new();
    private readonly GamingPowerProfileController _powerProfileController;
    private readonly Func<bool> _telemetryVisible;
    private readonly Func<int> _telemetryWindowPid;
    private readonly AutomaticGamingModePolicy _policy = new();
    private readonly CancellationTokenSource _lifetime = new();
    private bool _gamingModeRequested;
    private int _policyPending;
    private int _stopping;

    public GamingModeAutomation(ShellSessionState state, Func<bool> automaticEnabled,
        GameModeController? controller = null, GameProcessDetector? detector = null,
        Func<bool>? telemetryVisible = null, Func<int>? telemetryWindowPid = null,
        GamingPowerProfileController? powerProfileController = null)
    {
        _state = state;
        _automaticEnabled = automaticEnabled;
        _controller = controller ?? new();
        _detector = detector ?? new();
        _telemetryVisible = telemetryVisible ?? (() => false);
        _telemetryWindowPid = telemetryWindowPid ?? (() => 0);
        _powerProfileController = powerProfileController ?? new();
    }

    public void Start()
    {
        _state.Changed += OnStateChanged;
        Volatile.Write(ref _gamingModeRequested, _state.GamingMode);
        _ = Task.Run(() => PollAsync(_lifetime.Token));
    }

    private void OnStateChanged(object? sender, EventArgs e) =>
        Volatile.Write(ref _gamingModeRequested, _state.GamingMode);

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            do
            {
                try
                {
                    var processes = _detector.FindGameProcessIds();
                    var running = processes.Count > 0;
                    // A delayed UI update must not stop registration refreshes or
                    // accumulate policy callbacks while the desktop is busy.
                    if (Interlocked.Exchange(ref _policyPending, 1) == 0)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (Volatile.Read(ref _stopping) == 0 &&
                                    _policy.Evaluate(running, _automaticEnabled(), _state.GamingMode) is { } desired)
                                    _state.GamingMode = desired;
                            }
                            finally { Volatile.Write(ref _policyPending, 0); }
                        });
                    }
                    var allowBatteryBoost = _state.GetPreference(GamingPowerPolicy.BatteryBoostPreferenceKey, "false") == "true";
                    var shouldEnable = Volatile.Read(ref _gamingModeRequested) &&
                        GamingPowerPolicy.ShouldEnablePerformanceBoost(allowBatteryBoost);
                    var usePerformanceProfile = shouldEnable &&
                        _state.GetPreference(GamingPowerProfileController.EnabledPreferenceKey, "true") != "false";
                    await _powerProfileController.SetActiveAsync(usePerformanceProfile, cancellationToken).ConfigureAwait(false);
                    _controller.SetActive(shouldEnable);
                    _controller.UpdateGameProcesses(processes);
                    await _frameTelemetry.UpdateAsync(_telemetryVisible(), _telemetryWindowPid(), cancellationToken).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    Console.Error.WriteLine($"[gaming-mode] Could not refresh game requests: {ex.Message}");
                }
            // Cancellation may complete the timer inline on the UI thread.
            // Force the continuation onto the pool before native cleanup.
            } while (await timer.WaitForNextTickAsync(cancellationToken).AsTask()
                .ConfigureAwait(ConfigureAwaitOptions.ForceYielding));
        }
        catch (OperationCanceledException) { }
        finally
        {
            try { await _powerProfileController.SetActiveAsync(false, CancellationToken.None).ConfigureAwait(false); }
            catch (Exception ex) { Console.Error.WriteLine($"[gaming-mode] Could not restore the power profile: {ex.Message}"); }
            _powerProfileController.Dispose();
            await _frameTelemetry.UpdateAsync(false, 0, CancellationToken.None).ConfigureAwait(false);
            _controller.Dispose();
            _lifetime.Dispose();
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _stopping, 1) != 0) return;
        _state.Changed -= OnStateChanged;
        _lifetime.Cancel();
        // The polling worker releases requests in its finally block. Never
        // wait for a native GameMode call while shutting down the UI thread.
    }
}
