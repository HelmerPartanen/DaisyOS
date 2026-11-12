using Avalonia;
using Avalonia.Controls;
using Avalonia.Automation;
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
    private readonly Dictionary<string, Border> _tiles = [];
    private readonly Canvas _canvas = new() { Height = 256, ClipToBounds = true, Name = "DisplayArrangementCanvas" };
    private readonly Button _apply = new() { Name = "ApplyDisplayArrangement", Content = Loc.Get("Apply"), Classes = { "Primary", "Small" } };
    private readonly Button _reset = new() { Name = "ResetDisplayArrangement", Content = Loc.Get("Reset"), Classes = { "Secondary", "Small" } };
    private readonly TextBlock _status = new() { TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private string _selected;
    private double _zoom = 1;
    private Point _offset;
    private string? _dragging;
    private Point _dragOrigin;
    private Point _dragPosition;
    private bool _applying;
    private string? _styledSelection;
    public event Func<IReadOnlyList<DisplayPosition>, Task>? ApplyRequested;

    public DisplayArrangementEditor(IReadOnlyList<DetectedDisplay> displays, string selected)
    {
        _displays = displays;
        _selected = displays.Any(d => d.Name == selected) ? selected : displays[0].Name;
        _positions = displays.ToDictionary(d => d.Name, d => new Point(d.PositionX, d.PositionY));
        Margin = new Thickness(16, 4, 16, 16);
        _status.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        var instruction = new TextBlock
        {
            Text = Loc.Get("Drag screens to match their positions on your desk. Edges snap together."),
            TextWrapping = TextWrapping.Wrap
        };
        instruction.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        var preview = new Border { Child = _canvas, CornerRadius = new CornerRadius(12), BorderThickness = new Thickness(1), ClipToBounds = true };
        preview.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));
        preview.Bind(Border.BorderBrushProperty, this.GetResourceObservable("StrokeSubtleBrush"));
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _reset, _apply } };
        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 16 };
        footer.Children.Add(_status);
        Grid.SetColumn(actions, 1);
        footer.Children.Add(actions);
        Content = new StackPanel { Spacing = 12, Children = { instruction, preview, footer } };
        for (var i = 0; i < displays.Count; i++) CreateTile(displays[i], i + 1);
        _canvas.SizeChanged += (_, _) => { if (_dragging is null) FitPreview(); };
        _reset.Click += (_, _) =>
        {
            foreach (var display in _displays) _positions[display.Name] = new Point(display.PositionX, display.PositionY);
            FitPreview();
        };
        _apply.Click += async (_, _) =>
        {
            if (!_apply.IsEnabled || ApplyRequested is not { } handler) return;
            _applying = true;
            IsEnabled = false;
            try { await handler(NormalizedPositions()); }
            finally { _applying = false; IsEnabled = true; UpdateFeedback(); }
        };
        UpdateFeedback();
    }

    private void CreateTile(DetectedDisplay display, int number)
    {
        var label = new TextBlock { Text = number.ToString(), FontSize = 24, FontWeight = FontWeight.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center };
        var name = new TextBlock { Text = display.Name, FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center };
        var content = new StackPanel { Spacing = 2, Margin = new Thickness(6), VerticalAlignment = VerticalAlignment.Center,
            Children = { label, name } };
        if (display.IsPrimary)
            content.Children.Add(new TextBlock { Text = Loc.Get("Main display"), FontSize = 11,
                HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis });
        foreach (var text in content.Children.OfType<TextBlock>())
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        var tile = new Border { Name = $"DisplayTile{number}", Tag = display.Name, Child = content, Focusable = true,
            BorderThickness = new Thickness(2), CornerRadius = new CornerRadius(8), Cursor = new Cursor(StandardCursorType.Hand) };
        AutomationProperties.SetName(tile, $"{number} · {display.Name}");
        ToolTip.SetTip(tile, Loc.Get("Drag to move this display. Use arrow keys for precise adjustments."));
        tile.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(tile).Properties.IsLeftButtonPressed) return;
            _selected = display.Name;
            _dragging = display.Name;
            _dragOrigin = e.GetPosition(_canvas);
            _dragPosition = _positions[display.Name];
            tile.Focus();
            tile.ZIndex = 1;
            e.Pointer.Capture(tile);
            UpdateFeedback();
            e.Handled = true;
        };
        tile.PointerMoved += (_, e) =>
        {
            if (_dragging != display.Name) return;
            var delta = (e.GetPosition(_canvas) - _dragOrigin) / _zoom;
            _positions[display.Name] = Snap(display, _dragPosition + delta);
            PositionTiles();
            UpdateFeedback();
            e.Handled = true;
        };
        tile.PointerReleased += (_, e) =>
        {
            if (_dragging != display.Name) return;
            _dragging = null;
            e.Pointer.Capture(null);
            tile.ZIndex = 0;
            ResolveOverlap(display);
            FitPreview();
            e.Handled = true;
        };
        tile.PointerCaptureLost += (_, _) =>
        {
            if (_dragging != display.Name) return;
            _dragging = null;
            _positions[display.Name] = _dragPosition;
            tile.ZIndex = 0;
            FitPreview();
        };
        tile.GotFocus += (_, _) => { _selected = display.Name; UpdateFeedback(); };
        tile.KeyDown += (_, e) =>
        {
            var direction = e.Key switch { Key.Left => new Vector(-1, 0), Key.Right => new Vector(1, 0),
                Key.Up => new Vector(0, -1), Key.Down => new Vector(0, 1), _ => default };
            if (direction == default || _dragging is not null) return;
            var step = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 100 : 10;
            _positions[display.Name] += direction * step;
            FitPreview();
            e.Handled = true;
        };
        _tiles.Add(display.Name, tile);
        _canvas.Children.Add(tile);
    }

    internal IReadOnlyList<DisplayPosition> NormalizedPositions()
    {
        var minimumX = _positions.Values.Min(p => p.X);
        var minimumY = _positions.Values.Min(p => p.Y);
        return _positions.Select(p => new DisplayPosition(p.Key,
            (int)Math.Round(p.Value.X - minimumX), (int)Math.Round(p.Value.Y - minimumY))).ToArray();
    }

    private Rect Rectangle(DetectedDisplay display) => new(_positions[display.Name], new Size(display.LogicalWidth, display.LogicalHeight));

    private bool HasOverlap()
    {
        for (var i = 0; i < _displays.Count; i++)
            for (var j = i + 1; j < _displays.Count; j++)
                if (Rectangle(_displays[i]).Intersects(Rectangle(_displays[j]))) return true;
        return false;
    }

    private Point Snap(DetectedDisplay display, Point position)
    {
        var threshold = 12 / _zoom;
        var moving = new Rect(position, new Size(display.LogicalWidth, display.LogicalHeight));
        var dx = threshold + 1;
        var dy = threshold + 1;
        foreach (var other in _displays.Where(d => d.Name != display.Name))
        {
            var neighbor = Rectangle(other);
            if (moving.Top < neighbor.Bottom + threshold && moving.Bottom > neighbor.Top - threshold)
                foreach (var candidate in new[] { neighbor.Left - moving.Right, neighbor.Right - moving.Left,
                    neighbor.Left - moving.Left, neighbor.Right - moving.Right })
                    if (Math.Abs(candidate) < Math.Abs(dx)) dx = candidate;
            if (moving.Left < neighbor.Right + threshold && moving.Right > neighbor.Left - threshold)
                foreach (var candidate in new[] { neighbor.Top - moving.Bottom, neighbor.Bottom - moving.Top,
                    neighbor.Top - moving.Top, neighbor.Bottom - moving.Bottom,
                    neighbor.Center.Y - moving.Center.Y })
                    if (Math.Abs(candidate) < Math.Abs(dy)) dy = candidate;
        }
        return new Point(Math.Round(position.X + (Math.Abs(dx) <= threshold ? dx : 0)),
            Math.Round(position.Y + (Math.Abs(dy) <= threshold ? dy : 0)));
    }

    private void ResolveOverlap(DetectedDisplay display)
    {
        var moving = Rectangle(display);
        var neighbors = _displays.Where(d => d.Name != display.Name).Select(Rectangle).ToArray();
        if (!neighbors.Any(moving.Intersects)) return;
        var candidates = neighbors.SelectMany(neighbor => new[]
        {
            new Point(neighbor.Left - moving.Width, moving.Top), new Point(neighbor.Right, moving.Top),
            new Point(moving.Left, neighbor.Top - moving.Height), new Point(moving.Left, neighbor.Bottom),
            new Point(neighbor.Left - moving.Width, neighbor.Top), new Point(neighbor.Right, neighbor.Top),
            new Point(neighbor.Left, neighbor.Top - moving.Height), new Point(neighbor.Left, neighbor.Bottom)
        }).Where(point => neighbors.All(neighbor => !new Rect(point, moving.Size).Intersects(neighbor)))
          .OrderBy(point => Math.Pow(point.X - moving.X, 2) + Math.Pow(point.Y - moving.Y, 2)).ToArray();
        if (candidates.Length > 0) _positions[display.Name] = candidates[0];
    }

    private void FitPreview()
    {
        var minimumX = _positions.Values.Min(p => p.X);
        var minimumY = _positions.Values.Min(p => p.Y);
        var width = _displays.Max(d => _positions[d.Name].X + d.LogicalWidth) - minimumX;
        var height = _displays.Max(d => _positions[d.Name].Y + d.LogicalHeight) - minimumY;
        _zoom = Math.Min(Math.Max(1, _canvas.Bounds.Width - 64) / Math.Max(1, width), 192 / Math.Max(1, height));
        _offset = new Point((_canvas.Bounds.Width - width * _zoom) / 2 - minimumX * _zoom,
            (_canvas.Height - height * _zoom) / 2 - minimumY * _zoom);
        PositionTiles();
        UpdateFeedback();
    }

    private void PositionTiles()
    {
        foreach (var display in _displays)
        {
            var tile = _tiles[display.Name];
            tile.Width = Math.Max(32, display.LogicalWidth * _zoom);
            tile.Height = Math.Max(32, display.LogicalHeight * _zoom);
            Canvas.SetLeft(tile, _offset.X + _positions[display.Name].X * _zoom);
            Canvas.SetTop(tile, _offset.Y + _positions[display.Name].Y * _zoom);
        }
    }

    private void UpdateFeedback()
    {
        var dirty = _displays.Any(d => _positions[d.Name] != new Point(d.PositionX, d.PositionY));
        var overlap = HasOverlap();
        _apply.IsEnabled = dirty && !overlap && !_applying;
        _reset.IsEnabled = dirty && !_applying;
        _status.Text = overlap ? Loc.Get("Displays must not overlap. Drag a screen beside another.")
            : _selected + (dirty ? " · " + Loc.Get("Changes not applied") : "");
        if (_styledSelection == _selected) return;
        _styledSelection = _selected;
        foreach (var (name, tile) in _tiles)
        {
            tile.Bind(Border.BackgroundProperty, this.GetResourceObservable(name == _selected ? "ActionPrimarySubtleBrush" : "SurfaceRaisedBrush"));
            tile.Bind(Border.BorderBrushProperty, this.GetResourceObservable(name == _selected ? "ActionPrimaryBrush" : "StrokeDefaultBrush"));
        }
    }
}
