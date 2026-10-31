namespace DaisyOS.Shell.Services.Compositor;

/// <summary>Delivers the newest finger position without losing release boundaries.</summary>
internal sealed class LauncherPinchDispatcher(Action<Action> schedule, Action<LauncherPinchUpdate> deliver)
{
    private readonly object _gate = new();
    private readonly LinkedList<LauncherPinchUpdate> _pending = new();
    private bool _scheduled;

    public void Push(LauncherPinchUpdate update)
    {
        lock (_gate)
        {
            if (_pending.Last is { Value.Finished: false } tail && !update.Finished &&
                tail.Value.Opening == update.Opening)
                tail.Value = update;
            else
                _pending.AddLast(update);
            if (_scheduled) return;
            _scheduled = true;
        }
        schedule(Drain);
    }

    private void Drain()
    {
        LauncherPinchUpdate[] batch;
        lock (_gate)
        {
            batch = _pending.ToArray();
            _pending.Clear();
            _scheduled = false;
        }
        foreach (var update in batch) deliver(update);
    }
}
