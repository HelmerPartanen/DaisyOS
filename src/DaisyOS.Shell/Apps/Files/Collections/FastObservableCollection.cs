using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DaisyOS.Shell.Apps.Files.Collections;

public sealed class FastObservableCollection<T> : ObservableCollection<T>
{
    private int _batchDepth;
    private bool _changed;
    public long NotificationCount { get; private set; }

    public void ReplaceAll(IEnumerable<T> items)
    {
        var replacement = items.ToArray();
        ApplyBatch(() =>
        {
            Items.Clear();
            foreach (var item in replacement) Items.Add(item);
            _changed = true;
        });
    }

    public void AddRange(IEnumerable<T> items) => ApplyBatch(() =>
    {
        foreach (var item in items.ToArray()) Add(item);
    });

    public void RemoveRange(IEnumerable<T> items) => ApplyBatch(() =>
    {
        foreach (var item in items.ToArray()) Remove(item);
    });

    public void ApplyBatch(Action action)
    {
        CheckReentrancy();
        _batchDepth++;
        try { action(); }
        finally
        {
            if (--_batchDepth == 0 && _changed)
            {
                _changed = false;
                base.OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
                base.OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
                OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
            }
        }
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        if (_batchDepth == 0) base.OnPropertyChanged(e);
    }

    protected override void OnCollectionChanged(NotifyCollectionChangedEventArgs e)
    {
        if (_batchDepth > 0) { _changed = true; return; }
        NotificationCount++;
        base.OnCollectionChanged(e);
    }
}
