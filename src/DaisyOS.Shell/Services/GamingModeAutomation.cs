using Avalonia.Threading;
using DaisyOS.System.Gaming;

namespace DaisyOS.Shell.Services;

/// <summary>
/// Owns GameMode for the desktop shell process: Gaming Mode holds a GameMode
/// request, and a running Steam or Lutris game turns Gaming Mode on automatically.
/// </summary>
internal sealed class GamingModeAutomation : IDisposable
{
    public const string AutomaticPreferenceKey = "gaming.auto-enable";
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(3);

    private readonly ShellSessionState _state;
    private readonly Func<bool> _automaticEnabled;
    private readonly GameModeController _controller = new();
    private readonly GameProcessDetector _detector = new();
    private readonly AutomaticGamingModePolicy _policy = new();
    private readonly CancellationTokenSource _lifetime = new();

    public GamingModeAutomation(ShellSessionState state, Func<bool> automaticEnabled)
    {
        _state = state;
        _automaticEnabled = automaticEnabled;
    }

    public void Start()
    {
        _state.Changed += OnStateChanged;
        _controller.SetActive(_state.GamingMode);
        _ = Task.Run(() => PollAsync(_lifetime.Token));
    }

    private void OnStateChanged(object? sender, EventArgs e) => _controller.SetActive(_state.GamingMode);

    private async Task PollAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                var running = _detector.IsGameRunning();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (_policy.Evaluate(running, _automaticEnabled(), _state.GamingMode) is { } desired)
                        _state.GamingMode = desired;
                });
            }
        }
        catch (OperationCanceledException) { }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _state.Changed -= OnStateChanged;
        _controller.Dispose();
        _lifetime.Dispose();
    }
}
