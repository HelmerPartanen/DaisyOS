using System.Diagnostics;

namespace DaisyOS.System.Audio;

/// <summary>
/// Observes PulseAudio-compatible sink events (including PipeWire's pulse
/// server) so shell volume controls track media keys without polling wpctl.
/// </summary>
public sealed class LinuxAudioChangeMonitor : IDisposable
{
    private readonly CancellationTokenSource _stopping = new();
    private readonly Task _worker;
    private Process? _process;
    private int _disposed;

    public event EventHandler? Changed;

    public LinuxAudioChangeMonitor()
    {
        _worker = OperatingSystem.IsLinux() ? Task.Run(ObserveAsync) : Task.CompletedTask;
    }

    private async Task ObserveAsync()
    {
        while (!_stopping.IsCancellationRequested)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = "pactl",
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardOutput = true
                    }
                };
                process.StartInfo.ArgumentList.Add("subscribe");
                _process = process;
                if (!process.Start()) return;
                while (!_stopping.IsCancellationRequested
                       && await process.StandardOutput.ReadLineAsync(_stopping.Token) is { } line)
                {
                    if (line.Contains(" on sink ", StringComparison.OrdinalIgnoreCase)
                        || line.Contains(" on server ", StringComparison.OrdinalIgnoreCase))
                    {
                        Changed?.Invoke(this, EventArgs.Empty);
                    }
                }
            }
            catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
            {
                return;
            }
            catch (Exception) when (!_stopping.IsCancellationRequested)
            {
                // pactl is optional; retry in case the audio server is starting.
            }
            finally
            {
                _process = null;
            }

            try { await Task.Delay(TimeSpan.FromSeconds(2), _stopping.Token); }
            catch (OperationCanceledException) { return; }
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _stopping.Cancel();
        try
        {
            if (_process is { HasExited: false } process) process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException) { }
        _ = _worker.ContinueWith(_ => _stopping.Dispose(), TaskScheduler.Default);
    }
}
