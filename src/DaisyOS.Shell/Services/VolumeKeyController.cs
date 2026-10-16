using DaisyOS.Core.Services;

namespace DaisyOS.Shell.Services;

/// <summary>Serializes hardware keys so repeats never race a stale read or lose mute toggles.</summary>
internal sealed class VolumeKeyController(IAudioService audio)
{
    private readonly Queue<string> _pending = new();
    private bool _running;
    internal event Action<double, bool, bool>? Changed;

    internal async Task HandleAsync(string action)
    {
        if (action is not ("volume-up" or "volume-down" or "volume-up-small" or "volume-down-small" or "volume-mute" or "microphone-mute")) return;
        _pending.Enqueue(action);
        if (_running) return;
        _running = true;
        try
        {
            while (_pending.TryDequeue(out var next))
            {
                var input = next == "microphone-mute";
                var volume = input ? await audio.GetInputVolumeAsync() : await audio.GetVolumeAsync();
                if (volume is null) continue;
                var muted = input ? await audio.GetInputMutedAsync() : await audio.GetMutedAsync();
                if (next.EndsWith("mute", StringComparison.Ordinal))
                {
                    if (input) await audio.SetInputMutedAsync(!muted);
                    else await audio.SetMutedAsync(!muted);
                }
                else
                {
                    var delta = next.Contains("up", StringComparison.Ordinal) ? 1 : -1;
                    var step = next.EndsWith("small", StringComparison.Ordinal) ? 1 : 5;
                    var repetitions = 1;
                    while (_pending.TryPeek(out var repeated) && repeated == next)
                    {
                        _pending.Dequeue();
                        repetitions++;
                    }
                    await audio.SetVolumeAsync(Math.Clamp(volume.Value + delta * step * repetitions, 0, 100));
                    if (muted) await audio.SetMutedAsync(false);
                }
                // Read back the backend value, including a rejected/unavailable device.
                var actual = input ? await audio.GetInputVolumeAsync() : await audio.GetVolumeAsync();
                if (actual is not null)
                    Changed?.Invoke(actual.Value, input ? await audio.GetInputMutedAsync() : await audio.GetMutedAsync(), input);
            }
        }
        catch (Exception exception)
        {
            _pending.Clear();
            Console.Error.WriteLine($"[volume-keys] {exception.Message}");
        }
        finally { _running = false; }
    }
}
