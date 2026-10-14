using DaisyOS.Shell.Apps.System.Settings;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class DisplayChangeGuardTests
{
    [Fact]
    public async Task CountdownExpiresAtFifteenSecondsAndRejectsLateKeep()
    {
        var clock = new ManualClock();
        using var confirmation = new DisplayChangeConfirmation(default, clock);
        Assert.Equal(15, confirmation.SecondsRemaining);
        clock.Advance(TimeSpan.FromSeconds(14.1));
        Assert.Equal(1, confirmation.SecondsRemaining);
        Assert.False(confirmation.IsComplete);
        clock.Advance(TimeSpan.FromSeconds(.9), fire: false);
        Assert.Equal(0, confirmation.SecondsRemaining);
        confirmation.Keep(); // Even a delayed timer callback cannot extend the deadline.
        Assert.False(await confirmation.Decision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task KeepOrRevertExecutesExactlyOneDecision(bool keep)
    {
        var restores = 0;
        var guard = new DisplayChangeGuard();
        var result = await guard.RunAsync(() => Task.FromResult(true), () => { restores++; return Task.FromResult(true); },
            c => { if (keep) c.Keep(); else c.Revert(); c.Revert(); }, default);
        Assert.Equal(keep ? DisplayChangeResult.Kept : DisplayChangeResult.Reverted, result);
        Assert.Equal(keep ? 0 : 1, restores);
        Assert.False(guard.IsBusy);
    }

    [Fact]
    public async Task TimerRestoresWithoutAnyUiCallback()
    {
        var clock = new ManualClock();
        var guard = new DisplayChangeGuard(clock);
        var restores = 0;
        var pending = guard.RunAsync(() => Task.FromResult(true), () => { restores++; return Task.FromResult(true); }, _ => { }, default);
        clock.Advance(TimeSpan.FromSeconds(15));
        Assert.Equal(DisplayChangeResult.Reverted, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, restores);
    }

    [Fact]
    public async Task ClosingDuringApplyRestoresAfterTheCommandCompletes()
    {
        using var closed = new CancellationTokenSource();
        var apply = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restores = 0;
        var shown = false;
        var guard = new DisplayChangeGuard();
        var pending = guard.RunAsync(() => apply.Task, () => { restores++; return Task.FromResult(true); }, _ => shown = true, closed.Token);
        closed.Cancel();
        apply.SetResult(true);
        Assert.Equal(DisplayChangeResult.Reverted, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, restores);
        Assert.False(shown);
    }

    [Fact]
    public async Task ClosingDuringConfirmationRevertsAndRejectsAnotherApply()
    {
        using var closed = new CancellationTokenSource();
        var guard = new DisplayChangeGuard();
        var restores = 0;
        var pending = guard.RunAsync(() => Task.FromResult(true), () => { restores++; return Task.FromResult(true); }, _ => { }, closed.Token);
        Assert.Equal(DisplayChangeResult.Busy, await guard.RunAsync(() => throw new Exception("Overlapping apply"), () => Task.FromResult(true), _ => { }, default));
        closed.Cancel();
        Assert.Equal(DisplayChangeResult.Reverted, await pending.WaitAsync(TimeSpan.FromSeconds(2)));
        Assert.Equal(1, restores);
        Assert.False(guard.IsBusy);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PartialFailureOrExceptionStillRestores(bool throws)
    {
        var restores = 0;
        var result = await new DisplayChangeGuard().RunAsync(
            () => throws ? throw new IOException("Disconnected") : Task.FromResult(false),
            () => { restores++; return Task.FromResult(true); }, _ => throw new Exception("Must not ask to keep a failed change"), default);
        Assert.Equal(DisplayChangeResult.Failed, result);
        Assert.Equal(1, restores);
    }

    [Fact]
    public async Task FailedRollbackRetriesAndReportsFailure()
    {
        var restores = 0;
        var guard = new DisplayChangeGuard();
        var result = await guard.RunAsync(() => Task.FromResult(true), () => { restores++; return Task.FromResult(false); }, c => c.Revert(), default);
        Assert.Equal(DisplayChangeResult.RestoreFailed, result);
        Assert.Equal(2, restores);
        Assert.False(guard.IsBusy);
    }

    private sealed class ManualClock : TimeProvider
    {
        private long _ticks;
        private readonly List<ManualTimer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state, dueTime);
            _timers.Add(timer);
            return timer;
        }
        public void Advance(TimeSpan duration, bool fire = true)
        {
            _ticks += duration.Ticks;
            if (fire) foreach (var timer in _timers.ToArray()) timer.Fire();
        }
        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state, TimeSpan dueTime) : ITimer
        {
            private long _deadline = clock._ticks + dueTime.Ticks;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period) { _deadline = clock._ticks + dueTime.Ticks; return !_disposed; }
            public void Fire() { if (!_disposed && clock._ticks >= _deadline) { _disposed = true; callback(state); } }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
