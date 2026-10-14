using System;
using System.Threading;
using System.Threading.Tasks;

namespace DaisyOS.Shell.Apps.System.Settings;

internal enum DisplayChangeResult { Busy, Kept, Reverted, Failed, RestoreFailed }

// The rollback deadline runs independently of Avalonia's dispatcher.
internal sealed class DisplayChangeConfirmation : IDisposable
{
    private readonly TimeProvider _clock;
    private readonly long _started;
    private readonly TimeSpan _duration;
    private readonly TaskCompletionSource<bool> _decision = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ITimer _timer;
    private readonly CancellationTokenRegistration _closed;

    public DisplayChangeConfirmation(CancellationToken closed, TimeProvider? clock = null, TimeSpan? duration = null)
    {
        _clock = clock ?? TimeProvider.System;
        _duration = duration ?? TimeSpan.FromSeconds(15);
        _started = _clock.GetTimestamp();
        _timer = _clock.CreateTimer(_ => Revert(), null, _duration, Timeout.InfiniteTimeSpan);
        _closed = closed.Register(Revert);
    }

    public Task<bool> Decision => _decision.Task;
    public int SecondsRemaining => Math.Max(0, (int)Math.Ceiling((_duration - _clock.GetElapsedTime(_started)).TotalSeconds));
    public bool IsComplete => _decision.Task.IsCompleted;
    public void Keep() => _decision.TrySetResult(SecondsRemaining > 0);
    public void Revert() => _decision.TrySetResult(false);
    public void Dispose() { _timer.Dispose(); _closed.Dispose(); }
}

internal sealed class DisplayChangeGuard
{
    private int _busy;
    private readonly TimeProvider _clock;
    public DisplayChangeGuard(TimeProvider? clock = null) => _clock = clock ?? TimeProvider.System;
    public bool IsBusy => Volatile.Read(ref _busy) != 0;

    public async Task<DisplayChangeResult> RunAsync(Func<Task<bool>> apply, Func<Task<bool>> restore,
        Action<DisplayChangeConfirmation> show, CancellationToken closed)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return DisplayChangeResult.Busy;
        var applied = false;
        var kept = false;
        try
        {
            if (closed.IsCancellationRequested) return DisplayChangeResult.Failed;
            try
            {
                applied = await apply().ConfigureAwait(false);
                if (applied)
                {
                    using var confirmation = new DisplayChangeConfirmation(closed, _clock);
                    if (!confirmation.IsComplete) show(confirmation);
                    kept = await confirmation.Decision.ConfigureAwait(false);
                }
            }
            catch (Exception ex) { Console.Error.WriteLine($"[display-settings] Apply/confirmation failed: {ex.Message}"); }
            if (kept) return DisplayChangeResult.Kept;

            // A failed or timed-out command may already have changed the output.
            // Never use the cancelled window token for restoration.
            for (var attempt = 0; attempt < 2; attempt++)
            {
                try
                {
                    if (await restore().ConfigureAwait(false))
                        return applied ? DisplayChangeResult.Reverted : DisplayChangeResult.Failed;
                }
                catch (Exception ex) { Console.Error.WriteLine($"[display-settings] Restore failed: {ex.Message}"); }
            }
            return DisplayChangeResult.RestoreFailed;
        }
        finally { Volatile.Write(ref _busy, 0); }
    }
}
