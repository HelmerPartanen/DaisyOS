using System.Windows.Input;

namespace DaisyOS.Core.Helpers;

public sealed class AsyncRelayCommand : ICommand, IDisposable
{
    private readonly Func<object?, CancellationToken, Task> _execute;
    private readonly Predicate<object?>? _canExecute;
    private CancellationTokenSource? _executionCts;
    private bool _isRunning;
    private bool _disposed;

    public AsyncRelayCommand(Func<Task> execute)
        : this((_, _) => execute())
    {
    }

    public AsyncRelayCommand(Func<CancellationToken, Task> execute)
        : this((_, cancellationToken) => execute(cancellationToken))
    {
    }

    public AsyncRelayCommand(
        Func<object?, CancellationToken, Task> execute,
        Predicate<object?>? canExecute = null)
    {
        ArgumentNullException.ThrowIfNull(execute);
        _execute = execute;
        _canExecute = canExecute;
    }

    public event EventHandler? CanExecuteChanged;

    public event EventHandler<Exception>? ExecutionFailed;

    public bool IsRunning
    {
        get => _isRunning;
        private set
        {
            if (_isRunning == value)
            {
                return;
            }

            _isRunning = value;
            CanExecuteChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool CanExecute(object? parameter) =>
        !_disposed && !IsRunning && (_canExecute?.Invoke(parameter) ?? true);

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter))
        {
            return;
        }

        var executionCts = new CancellationTokenSource();
        var oldCts = Interlocked.Exchange(ref _executionCts, executionCts);
        if (oldCts != null)
        {
            oldCts.Cancel();
            oldCts.Dispose();
        }
        IsRunning = true;
        try
        {
            await _execute(parameter, executionCts.Token);
        }
        catch (OperationCanceledException) when (executionCts.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            ExecutionFailed?.Invoke(this, ex);
        }
        finally
        {
            Interlocked.CompareExchange(ref _executionCts, null, executionCts);
            executionCts.Dispose();
            IsRunning = false;
        }
    }

    public void Cancel()
    {
        try
        {
            Volatile.Read(ref _executionCts)?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Completion won the race with cancellation.
        }
    }

    public void RaiseCanExecuteChanged() => CanExecuteChanged?.Invoke(this, EventArgs.Empty);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        var executionCts = Interlocked.Exchange(ref _executionCts, null);
        if (executionCts is not null)
        {
            try
            {
                executionCts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // Completion owns disposal of an in-flight token source.
            }
        }
        CanExecuteChanged?.Invoke(this, EventArgs.Empty);
    }
}
