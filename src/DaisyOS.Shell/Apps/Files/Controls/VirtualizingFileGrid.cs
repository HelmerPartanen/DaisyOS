using System.Collections.Specialized;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Apps.Files;

/// <summary>A fixed-cell grid with a bounded realized viewport and container recycling.</summary>
public class VirtualizingFileGrid : VirtualizingPanel
{
    private readonly Dictionary<int, Control> _realized = [];
    private readonly Dictionary<object, Stack<Control>> _pool = [];
    private readonly Dictionary<Control, object?> _keys = [];
    private Rect _viewport;
    private IScrollAnchorProvider? _anchorProvider;
    private int _columns = 1;
    private int _scrollTo = -1;
    protected virtual bool RealizeEntirePage => false;
    protected virtual double CellWidth => 122;
    protected virtual double CellHeight => 114;
    public int RealizedCount => _realized.Count;

    public VirtualizingFileGrid()
    {
        EffectiveViewportChanged += (_, e) =>
        {
            if (_viewport == e.EffectiveViewport) return;
            _viewport = e.EffectiveViewport;
            _scrollTo = -1;
            InvalidateMeasure();
        };
    }
    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : Math.Max(CellWidth, Bounds.Width);
        _columns = Math.Max(1, (int)(width / CellWidth));
        var height = _viewport.Height > 0 && double.IsFinite(_viewport.Height) ? _viewport.Height : 600;
        var top = Math.Max(0, _viewport.Y);
        int first = RealizeEntirePage ? 0 : Math.Max(0, (int)(top / CellHeight) - 1) * _columns;
        int last = RealizeEntirePage ? Items.Count : Math.Min(Items.Count, ((int)Math.Ceiling((top + height) / CellHeight) + 1) * _columns);
        foreach (var pair in _realized.ToArray())
            if ((pair.Key < first || pair.Key >= last) && pair.Key != _scrollTo && !pair.Value.IsKeyboardFocusWithin)
                Recycle(pair.Key);
        for (int index = first; index < last; index++) Realize(index).Measure(new Size(CellWidth, CellHeight));
        if (_scrollTo >= 0 && _scrollTo < Items.Count) Realize(_scrollTo).Measure(new Size(CellWidth, CellHeight));
        return new Size(width, Math.Ceiling((double)Items.Count / _columns) * CellHeight);
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        foreach (var pair in _realized)
        {
            var cell = Cell(pair.Key);
            pair.Value.Arrange(cell);
            if (_viewport.Intersects(cell)) _anchorProvider?.RegisterAnchorCandidate(pair.Value);
        }
        return finalSize;
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _anchorProvider = this.FindAncestorOfType<IScrollAnchorProvider>();
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        foreach (var control in _realized.Values) _anchorProvider?.UnregisterAnchorCandidate(control);
        _anchorProvider = null;
        base.OnDetachedFromVisualTree(e);
    }
    private Rect Cell(int index) => new(index % _columns * CellWidth, index / _columns * CellHeight, CellWidth, CellHeight);
    private Control Realize(int index)
    {
        if (_realized.TryGetValue(index, out var existing)) return existing;
        var generator = ItemContainerGenerator!;
        var item = Items[index];
        var needs = generator.NeedsContainer(item, index, out var key);
        Control container;
        if (!needs) container = (Control)item!;
        else if (key != null && _pool.TryGetValue(key, out var pool) && pool.TryPop(out var recycled))
            container = recycled;
        else
        {
            container = generator.CreateContainer(item, index, key);
            _keys[container] = key;
        }
        generator.PrepareItemContainer(container, item, index);
        container.IsVisible = true;
        _realized.Add(index, container);
        if (!Children.Contains(container)) AddInternalChild(container);
        generator.ItemContainerPrepared(container, item, index);
        return container;
    }
    private void Recycle(int index)
    {
        if (!_realized.Remove(index, out var control)) return;
        _anchorProvider?.UnregisterAnchorCandidate(control);
        ItemContainerGenerator!.ClearItemContainer(control);
        if (_keys.TryGetValue(control, out var key) && key != null)
        {
            control.IsVisible = false;
            if (!_pool.TryGetValue(key, out var pool)) _pool[key] = pool = new();
            // Keep a bounded spare pool when a window shrinks.
            if (pool.Count < 128) pool.Push(control);
            else { RemoveInternalChild(control); _keys.Remove(control); }
        }
        else RemoveInternalChild(control);
    }
    protected override void OnItemsChanged(IReadOnlyList<object?> items, NotifyCollectionChangedEventArgs e)
    {
        var old = _realized.ToArray();
        foreach (var pair in old)
        {
            int index = pair.Key, next = index;
            bool remove = e.Action == NotifyCollectionChangedAction.Reset;
            switch (e.Action)
            {
                case NotifyCollectionChangedAction.Add:
                    if (index >= e.NewStartingIndex) next += e.NewItems!.Count;
                    break;
                case NotifyCollectionChangedAction.Remove:
                    remove = index >= e.OldStartingIndex && index < e.OldStartingIndex + e.OldItems!.Count;
                    if (index >= e.OldStartingIndex + e.OldItems!.Count) next -= e.OldItems.Count;
                    break;
                case NotifyCollectionChangedAction.Replace:
                    remove = index >= e.NewStartingIndex && index < e.NewStartingIndex + e.NewItems!.Count;
                    break;
                case NotifyCollectionChangedAction.Move:
                    if (index == e.OldStartingIndex) next = e.NewStartingIndex;
                    else if (e.OldStartingIndex < e.NewStartingIndex && index > e.OldStartingIndex && index <= e.NewStartingIndex) next--;
                    else if (e.OldStartingIndex > e.NewStartingIndex && index >= e.NewStartingIndex && index < e.OldStartingIndex) next++;
                    break;
            }
            if (remove) Recycle(index);
        }
        _realized.Clear();
        foreach (var pair in old)
        {
            if (!pair.Value.IsVisible || !Children.Contains(pair.Value)) continue;
            int next = pair.Key;
            if (e.Action == NotifyCollectionChangedAction.Reset) continue;
            if (e.Action == NotifyCollectionChangedAction.Add && next >= e.NewStartingIndex) next += e.NewItems!.Count;
            if (e.Action == NotifyCollectionChangedAction.Remove && next >= e.OldStartingIndex + e.OldItems!.Count) next -= e.OldItems.Count;
            if (e.Action == NotifyCollectionChangedAction.Move)
            {
                if (next == e.OldStartingIndex) next = e.NewStartingIndex;
                else if (e.OldStartingIndex < e.NewStartingIndex && next > e.OldStartingIndex && next <= e.NewStartingIndex) next--;
                else if (e.OldStartingIndex > e.NewStartingIndex && next >= e.NewStartingIndex && next < e.OldStartingIndex) next++;
            }
            _realized[next] = pair.Value;
            if (next != pair.Key) ItemContainerGenerator!.ItemContainerIndexChanged(pair.Value, pair.Key, next);
        }
        InvalidateMeasure();
    }
    protected override Control? ContainerFromIndex(int index) => _realized.GetValueOrDefault(index);
    protected override int IndexFromContainer(Control container) => _realized.FirstOrDefault(p => ReferenceEquals(p.Value, container), new KeyValuePair<int, Control>(-1, container)).Key;
    protected override IEnumerable<Control> GetRealizedContainers() => _realized.Values;
    protected override Control? ScrollIntoView(int index)
    {
        if (index < 0 || index >= Items.Count) return null;
        _scrollTo = index;
        var control = Realize(index);
        control.Measure(new Size(CellWidth, CellHeight));
        control.Arrange(Cell(index));
        control.BringIntoView();
        InvalidateMeasure();
        return control;
    }
    protected override IInputElement? GetControl(NavigationDirection direction, IInputElement? from, bool wrap)
    {
        int index = from is Control control ? IndexFromContainer(control) : -1;
        var next = direction switch
        {
            NavigationDirection.First => 0,
            NavigationDirection.Last => Items.Count - 1,
            NavigationDirection.Up => index - _columns,
            NavigationDirection.Down => index + _columns,
            NavigationDirection.Left or NavigationDirection.Previous => index - 1,
            _ => index + 1
        };
        if (wrap && Items.Count > 0) next = (next + Items.Count) % Items.Count;
        return ScrollIntoView(next);
    }
}
