using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Shell.Services.Localization;

namespace DaisyOS.Shell.Apps.System.Settings;

internal sealed record DisplayPosition(string Name, int X, int Y);

/// <summary>A draft in compositor logical coordinates; changes are committed only by Apply.</summary>
internal sealed class DisplayArrangementEditor : UserControl
{
    private readonly IReadOnlyList<DetectedDisplay> _displays;
    private readonly Dictionary<string, Point> _positions;
    private readonly Canvas _canvas = new() { Height = 180, ClipToBounds = true, Name = "DisplayArrangementCanvas" };
    private readonly NumericUpDown _x = new() { Name = "DisplayPositionX", Width = 180, Increment = 1, Minimum = -32768, Maximum = 32768, FormatString = "0" };
    private readonly NumericUpDown _y = new() { Name = "DisplayPositionY", Width = 180, Increment = 1, Minimum = -32768, Maximum = 32768, FormatString = "0" };
    private readonly Button _apply;
    private readonly TextBlock _overlap;
    private string _selected;
    private bool _updating;
    private double _zoom;
    public event Func<IReadOnlyList<DisplayPosition>, Task>? ApplyRequested;

    public DisplayArrangementEditor(IReadOnlyList<DetectedDisplay> displays, string selected)
    {
        _displays = displays;
        _selected = selected;
        _positions = displays.ToDictionary(d => d.Name, d => new Point(d.PositionX, d.PositionY));
        _apply = new Button { Name = "ApplyDisplayArrangement", Content = Loc.Get("Apply"), Classes = { "Subtle" },
            HorizontalAlignment = HorizontalAlignment.Right };
        _overlap = new TextBlock { Text = Loc.Get("Displays must not overlap. Use Mirror displays to show the same content."),
            TextWrapping = TextWrapping.Wrap, IsVisible = false };
        var coordinates = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 12, RowSpacing = 8 };
        var horizontal = new TextBlock { Text = Loc.Get("Horizontal position"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(horizontal, 0); Grid.SetColumn(horizontal, 0); coordinates.Children.Add(horizontal);
        Grid.SetRow(_x, 0); Grid.SetColumn(_x, 1); _x.HorizontalAlignment = HorizontalAlignment.Stretch; coordinates.Children.Add(_x);
        var vertical = new TextBlock { Text = Loc.Get("Vertical position"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetRow(vertical, 1); Grid.SetColumn(vertical, 0); coordinates.Children.Add(vertical);
        Grid.SetRow(_y, 1); Grid.SetColumn(_y, 1); _y.HorizontalAlignment = HorizontalAlignment.Stretch; coordinates.Children.Add(_y);
        Content = new StackPanel { Spacing = 12, Children =
        {
            new TextBlock { Text = Loc.Get("Drag displays to match their physical arrangement."), TextWrapping = TextWrapping.Wrap },
            _canvas, coordinates, _overlap, _apply
        }};
        _canvas.SizeChanged += (_, _) => Draw();
        _x.ValueChanged += (_, _) => UpdateCoordinates();
        _y.ValueChanged += (_, _) => UpdateCoordinates();
        _apply.Click += async (_, _) =>
        {
            if (!HasOverlap() && ApplyRequested is { } handler) await handler(NormalizedPositions());
        };
        SyncCoordinates();
    }

    internal IReadOnlyList<DisplayPosition> NormalizedPositions()
    {
        var minimumX = _positions.Values.Min(p => p.X);
        var minimumY = _positions.Values.Min(p => p.Y);
        return _positions.Select(p => new DisplayPosition(p.Key,
            (int)Math.Round(p.Value.X - minimumX), (int)Math.Round(p.Value.Y - minimumY))).ToArray();
    }

    private void SyncCoordinates()
    {
        _updating = true;
        _x.Value = (decimal)_positions[_selected].X;
        _y.Value = (decimal)_positions[_selected].Y;
        _updating = false;
    }

    private void UpdateCoordinates()
    {
        if (_updating) return;
        _positions[_selected] = new Point((double)(_x.Value ?? 0), (double)(_y.Value ?? 0));
        Draw();
    }

    private IBrush Brush(string key) => this.TryFindResource(key, out var resource) && resource is IBrush brush ? brush : Brushes.Gray;

    private Rect Rectangle(DetectedDisplay display) => new(_positions[display.Name], new Size(display.LogicalWidth, display.LogicalHeight));

    private bool HasOverlap() => _displays.Any(a => _displays.Any(b => a.Name != b.Name && Rectangle(a).Intersects(Rectangle(b))));

    private void Snap(DetectedDisplay display)
    {
        foreach (var other in _displays.Where(d => d.Name != display.Name))
        {
            var moving = Rectangle(display);
            var neighbor = Rectangle(other);
            if (!moving.Intersects(neighbor)) continue;
            var candidates = new[]
            {
                new Point(neighbor.Left - moving.Width, moving.Top), new Point(neighbor.Right, moving.Top),
                new Point(moving.Left, neighbor.Top - moving.Height), new Point(moving.Left, neighbor.Bottom)
            };
            _positions[display.Name] = candidates.MinBy(point => Math.Abs(point.X - moving.X) + Math.Abs(point.Y - moving.Y));
        }
        SyncCoordinates();
    }

    private void Draw()
    {
        _canvas.Children.Clear();
        _overlap.IsVisible = HasOverlap();
        _apply.IsEnabled = !_overlap.IsVisible;
        var minimumX = _positions.Values.Min(p => p.X);
        var minimumY = _positions.Values.Min(p => p.Y);
        var width = _displays.Max(d => _positions[d.Name].X + d.LogicalWidth) - minimumX;
        var height = _displays.Max(d => _positions[d.Name].Y + d.LogicalHeight) - minimumY;
        _zoom = Math.Min(Math.Max(1, _canvas.Bounds.Width - 32) / Math.Max(1, width), 148 / Math.Max(1, height));
        foreach (var display in _displays)
        {
            var position = _positions[display.Name];
            var box = new Border { Width = Math.Max(24, display.LogicalWidth * _zoom), Height = Math.Max(24, display.LogicalHeight * _zoom),
                Background = Brush("SurfaceInputBrush"), BorderBrush = Brush(display.Name == _selected ? "ActionPrimaryBrush" : "ContentSecondaryBrush"),
                BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(6), Cursor = new Cursor(StandardCursorType.SizeAll),
                Child = new TextBlock { Text = display.Name, HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis,
                    Foreground = Brush("ContentPrimaryBrush"), Margin = new Thickness(4) } };
            Canvas.SetLeft(box, 16 + (position.X - minimumX) * _zoom);
            Canvas.SetTop(box, 16 + (position.Y - minimumY) * _zoom);
            Point? start = null;
            var startPosition = position;
            box.PointerPressed += (_, e) =>
            {
                if (!e.GetCurrentPoint(box).Properties.IsLeftButtonPressed) return;
                _selected = display.Name;
                start = e.GetPosition(_canvas);
                startPosition = _positions[display.Name];
                SyncCoordinates();
                e.Pointer.Capture(box);
                e.Handled = true;
            };
            box.PointerMoved += (_, e) =>
            {
                if (start is not { } origin) return;
                var delta = (e.GetPosition(_canvas) - origin) / _zoom;
                var next = startPosition + delta;
                _positions[display.Name] = new Point(Math.Round(next.X), Math.Round(next.Y));
                Canvas.SetLeft(box, 16 + (next.X - minimumX) * _zoom);
                Canvas.SetTop(box, 16 + (next.Y - minimumY) * _zoom);
                SyncCoordinates();
            };
            box.PointerReleased += (_, e) =>
            {
                if (start is null) return;
                start = null;
                e.Pointer.Capture(null);
                Snap(display);
                Draw();
            };
            box.PointerCaptureLost += (_, _) => { start = null; };
            _canvas.Children.Add(box);
        }
    }
}
