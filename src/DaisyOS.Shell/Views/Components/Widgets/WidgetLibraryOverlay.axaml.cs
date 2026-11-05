using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.DesktopWidgets;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Views.Components.Widgets;

/// <summary>
/// Widget gallery that presents one live widget size or style at a time.
/// Neighbouring variants peek in from the sides; trackpad swipes, wheel ticks,
/// arrow keys, the caret buttons and the page dots move between them.
/// </summary>
public partial class WidgetLibraryOverlay : UserControl
{
    public event Action<string, int, int, Point, Point, Size>? WidgetDragRequested;
    /// <summary>Raised by "Add Widget" with the currently displayed variant.</summary>
    public event Action<string, int, int>? WidgetAddRequested;
    public event Action? Dismissed;

    internal sealed record WidgetVariant(string Type, int Columns, int Rows, string Title, string Description, Func<Control> Create);

    private sealed class Slide
    {
        public required WidgetVariant Variant { get; init; }
        public required Border Card { get; init; }
        public required Grid Content { get; init; }
        public ScaleTransform Scale { get; } = new();
        public Rotate3DTransform Tilt { get; } = new() { Depth = 900 };
        public TranslateTransform Translate { get; } = new();
    }

    private const double MaxSideAngle = 24;
    private const double SideScale = 0.84;

    private readonly Dictionary<string, List<Slide>> _slideCache = new();
    private List<Slide> _slides = new();
    private double[] _anchors = Array.Empty<double>();
    private string _category = "Clock";
    private int _index;
    private double _position;
    private bool _dismissing;

    // Settlement / swipe state.
    private int _animationGeneration;
    private readonly SwipeVelocityTracker _velocity = new();
    private readonly DispatcherTimer _swipeIdle = new() { Interval = TimeSpan.FromMilliseconds(180) };
    private double _dragPx;
    private bool _dragging;
    private Vector _axisTravel;
    private bool? _horizontal;
    private long _lastWheelStep;

    // Ambient 3D tilt (time-driven, never pointer-driven).
    private double _ambientX, _ambientY;
    private bool _ambientRunning;

    private readonly Grid _viewport;
    private readonly Grid _track;

    internal Desktop.DesktopView? DesktopHost { get; set; }
    private Desktop.DesktopView? OwnerDesktop => DesktopHost ?? this.FindAncestorOfType<Desktop.DesktopView>();

    public bool IsOpen => GuiMotion.IsOpen(this);
    internal int SelectedIndex => _index;
    internal string SelectedCategory => _category;
    internal WidgetVariant SelectedVariant => _slides[_index].Variant;

    public WidgetLibraryOverlay()
    {
        InitializeComponent();
        _viewport = this.FindControl<Grid>("CarouselViewport")!;
        _track = this.FindControl<Grid>("CarouselTrack")!;
        SizeChanged += (_, _) => FitLibrary();
        _viewport.SizeChanged += (_, _) => LayoutSlides();
        AddHandler(PointerWheelChangedEvent, OnWheel, RoutingStrategies.Tunnel);
        AddHandler(KeyDownEvent, OnLibraryKeyDown, RoutingStrategies.Tunnel);
        _swipeIdle.Tick += (_, _) => SettleSwipe(null);
        ShowCategory("Clock", animate: false);
    }

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

    // ------------------------------------------------------------------
    // Catalogue
    // ------------------------------------------------------------------

    internal static IReadOnlyList<WidgetVariant> VariantsFor(string category) => category switch
    {
        "Calendar" =>
        [
            new("calendar", 3, 2, Loc.Get("Medium"), Loc.Get("Today’s date at a glance."), () => Sized(new CalendarWidget(), w => w.SetGridSize(3, 2))),
            new("calendar", 4, 2, Loc.Get("Large"), Loc.Get("Today beside the whole month."), () => Sized(new CalendarWidget(), w => w.SetGridSize(4, 2))),
        ],
        "Media" =>
        [
            new("media", 3, 2, Loc.Get("Medium"), Loc.Get("Artwork with controls on hover."), () => Sized(new DesktopWidgets.MediaWidget(), w => w.SetGridSize(3, 2))),
            new("media", 5, 2, Loc.Get("Large"), Loc.Get("Artwork, track details and playback controls."), () => Sized(new DesktopWidgets.MediaWidget(), w => w.SetGridSize(5, 2))),
            new("media", 5, DesktopWidgets.MediaWidget.ExtraLargeRows, Loc.Get("Extra large"), Loc.Get("Full artwork with progress and controls."), () => Sized(new DesktopWidgets.MediaWidget(), w => w.SetGridSize(5, DesktopWidgets.MediaWidget.ExtraLargeRows))),
        ],
        "Photos" =>
        [
            new("image", 3, 2, Loc.Get("Medium"), Loc.Get("A rotating selection of your photos."), () => Sized(new ImageWidget(), w => w.SetGridSize(3, 2))),
            new("image", 4, 2, Loc.Get("Large"), Loc.Get("A wider frame for landscapes."), () => Sized(new ImageWidget(), w => w.SetGridSize(4, 2))),
            new("image", 5, 4, Loc.Get("Extra large"), Loc.Get("A large frame for your favourite moments."), () => Sized(new ImageWidget(), w => w.SetGridSize(5, 4))),
        ],
        _ =>
        [
            new("clock", 3, 2, Loc.Get("Sweep Clock"), Loc.Get("A smooth, continuously sweeping hand."), () => Sized(new ClockWidget(), w => w.SetGridSize(3, 2))),
            new("clock-digital", 3, 2, Loc.Get("Digital Clock"), Loc.Get("Bold segment digits that read from across the room."), () => Sized(new DigitalClockWidget(), w => w.SetGridSize(3, 2))),
            new("clock-analog", 3, 2, Loc.Get("Analog Clock"), Loc.Get("A classic dial with ticking hands."), () => Sized(new AnalogClockWidget(), w => w.SetGridSize(3, 2))),
            new("clock-analog", 4, 2, Loc.Get("Analog Clock") + " · " + Loc.Get("Large"), Loc.Get("A classic dial with ticking hands."), () => Sized(new AnalogClockWidget(), w => w.SetGridSize(4, 2))),
        ],
    };

    private static Control Sized<T>(T widget, Action<T> size) where T : Control
    {
        size(widget);
        // Previews are pictures of live widgets: the gallery owns every pointer
        // gesture so photo pickers and transport buttons never fire here.
        widget.IsHitTestVisible = false;
        return widget;
    }

    private List<Slide> BuildSlides(string category)
    {
        var slides = new List<Slide>();
        foreach (var variant in VariantsFor(category))
        {
            var content = new Grid { Children = { variant.Create() } };
            var card = new Border
            {
                Classes = { "PreviewCard" },
                Tag = $"{variant.Columns},{variant.Rows}",
                Background = Brushes.Transparent,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Child = new Viewbox { Stretch = Stretch.Uniform, Child = content },
            };
            var slide = new Slide { Variant = variant, Card = card, Content = content };
            card.RenderTransform = new TransformGroup { Children = { slide.Scale, slide.Tilt, slide.Translate } };
            card.PointerPressed += (_, e) => OnCardPressed(slide, e);
            slides.Add(slide);
        }
        return slides;
    }

    // ------------------------------------------------------------------
    // Category + variant selection
    // ------------------------------------------------------------------

    private void ShowCategory(string category, bool animate)
    {
        CancelSwipe();
        _category = category;
        foreach (var name in new[] { "Clock", "Calendar", "Media", "Photos" })
            this.FindControl<Button>($"{name}CategoryButton")?.Classes.Set("Active", name == category);
        if (this.FindControl<Control>("AddToShelfButton") is { } shelf) shelf.IsVisible = category == "Media";

        if (!_slideCache.TryGetValue(category, out var slides))
            _slideCache[category] = slides = BuildSlides(category);
        _slides = slides;
        _track.Children.Clear();
        foreach (var slide in _slides) _track.Children.Add(slide.Card);

        BuildDots();
        LayoutSlides();
        _index = 0;
        UpdateChrome(animate);
        if (animate && MotionEnabled)
        {
            // Enter from slightly right of the first card so a family switch
            // reads as the carousel gliding into place, not a hard cut.
            _position = -0.45;
            Render();
            AnimateTo(0, 0);
        }
        else
        {
            _position = 0;
            Render();
        }
    }

    private void BuildDots()
    {
        var dots = this.FindControl<StackPanel>("PageDots")!;
        dots.Children.Clear();
        for (var i = 0; i < _slides.Count; i++)
        {
            var target = i;
            var dot = new Border { Classes = { "PageDot" } };
            dot.PointerPressed += (_, e) => { GoTo(target); e.Handled = true; };
            dots.Children.Add(dot);
        }
    }

    private void UpdateChrome(bool animate)
    {
        var variant = _slides[_index].Variant;
        this.FindControl<TextBlock>("VariantTitle")!.Text = variant.Title;
        this.FindControl<TextBlock>("VariantSizeText")!.Text = $"{variant.Columns} × {variant.Rows}";
        this.FindControl<TextBlock>("VariantDescription")!.Text = variant.Description;
        var dots = this.FindControl<StackPanel>("PageDots")!;
        for (var i = 0; i < dots.Children.Count; i++) dots.Children[i].Classes.Set("Active", i == _index);
        if (animate && MotionEnabled && this.FindControl<Control>("VariantHeader") is { } header)
            _ = GuiMotion.RevealAsync(header, 4);
    }

    /// <summary>Moves to a variant with the shared settle curve.</summary>
    internal void GoTo(int index)
    {
        if (_slides.Count == 0) return;
        index = Math.Clamp(index, 0, _slides.Count - 1);
        CancelSwipe();
        AnimateTo(index, 0);
    }

    private void SetIndex(int index)
    {
        if (index == _index) return;
        _index = index;
        UpdateChrome(true);
    }

    // ------------------------------------------------------------------
    // Layout + rendering
    // ------------------------------------------------------------------

    private void FitLibrary()
    {
        if (this.FindControl<Control>("LibraryFrame") is { } frame)
        {
            // Bottom chrome has a 3px screen inset; leave another 3px above it.
            var chromeHeight = this.TryFindResource("ShellChromeHeight", out var value) && value is double height ? height : 52;
            frame.Margin = new Thickness(0, 0, 0, chromeHeight + 6);
            frame.Width = Math.Min(860, Math.Max(0, Bounds.Width - 32));
            frame.Height = Math.Min(540, Math.Max(0, Bounds.Height - frame.Margin.Bottom - 32));
        }
    }

    private void LayoutSlides()
    {
        if (_slides.Count == 0) return;
        var metrics = OwnerDesktop?.CurrentMetrics;
        double cellWidth = metrics?.CellWidth ?? 74;
        double cellHeight = metrics?.CellHeight ?? 88;
        double width = Math.Max(1, _viewport.Bounds.Width);
        double height = Math.Max(1, _viewport.Bounds.Height);
        // Leave room for the caret buttons and the peeking neighbours.
        double maxWidth = Math.Max(1, width - 2 * 56);
        double maxHeight = Math.Max(1, height - 16);

        _anchors = new double[_slides.Count];
        double previousWidth = 0;
        for (var i = 0; i < _slides.Count; i++)
        {
            var slide = _slides[i];
            double naturalWidth = slide.Variant.Columns * cellWidth;
            double naturalHeight = slide.Variant.Rows * cellHeight;
            double scale = Math.Clamp(Math.Min(Math.Min(maxWidth * 0.82 / naturalWidth, maxHeight / naturalHeight), 1), 0.05, 1);
            slide.Content.Width = naturalWidth;
            slide.Content.Height = naturalHeight;
            slide.Card.Width = naturalWidth * scale;
            slide.Card.Height = naturalHeight * scale;
            if (i > 0)
                _anchors[i] = _anchors[i - 1] + (previousWidth + slide.Card.Width) * SideScale / 2 + 28;
            previousWidth = slide.Card.Width;
        }
        Render();
    }

    private double AnchorAt(double position)
    {
        if (_anchors.Length == 0) return 0;
        if (_anchors.Length == 1) return position * 200;
        if (position <= 0) return _anchors[0] + position * (_anchors[1] - _anchors[0]);
        var last = _anchors.Length - 1;
        if (position >= last) return _anchors[last] + (position - last) * (_anchors[last] - _anchors[last - 1]);
        var i = (int)Math.Floor(position);
        return _anchors[i] + (position - i) * (_anchors[i + 1] - _anchors[i]);
    }

    private double StrideFrom(int index, int direction)
    {
        if (_anchors.Length < 2) return Math.Max(1, _viewport.Bounds.Width * 0.5);
        var other = Math.Clamp(index + direction, 0, _anchors.Length - 1);
        if (other == index) other = index == 0 ? 1 : index - 1;
        return Math.Max(1, Math.Abs(_anchors[other] - _anchors[index]));
    }

    private void Render()
    {
        if (_slides.Count == 0 || _anchors.Length != _slides.Count) return;
        var center = AnchorAt(_position);
        for (var i = 0; i < _slides.Count; i++)
        {
            var slide = _slides[i];
            var distance = i - _position;
            var amount = Math.Min(Math.Abs(distance), 1);
            var focus = 1 - amount;
            slide.Translate.X = _anchors[i] - center;
            var scale = 1 - (1 - SideScale) * amount;
            slide.Scale.ScaleX = slide.Scale.ScaleY = scale;
            // Side cards turn toward the viewer; the focused card carries the
            // continuous ambient 3D tilt animation.
            slide.Tilt.AngleY = Math.Clamp(-distance * MaxSideAngle, -MaxSideAngle, MaxSideAngle) + focus * _ambientY;
            slide.Tilt.AngleX = focus * _ambientX;
            var abs = Math.Abs(distance);
            slide.Card.Opacity = abs <= 1 ? 1 - 0.55 * abs : Math.Max(0, 0.45 - (abs - 1) * 0.9);
            slide.Card.ZIndex = 100 - (int)Math.Round(abs * 10);
            slide.Card.IsHitTestVisible = slide.Card.Opacity > 0.05;
        }
    }

    private bool MotionEnabled => GuiMotion.Duration(this, true) != TimeSpan.Zero;

    private void AnimateTo(int target, double velocityPerMs)
    {
        var generation = ++_animationGeneration;
        SetIndex(target);
        var from = _position;
        if (!MotionEnabled || TopLevel.GetTopLevel(this) is not { } host || from == target)
        {
            _position = target;
            Render();
            return;
        }
        var stride = StrideFrom(target, from < target ? -1 : 1);
        var distancePx = (target - from) * stride;
        var velocityPx = velocityPerMs * stride;
        var duration = GestureSettlement.Duration(distancePx, velocityPx, 240, 420);
        TimeSpan? start = null;
        void Frame(TimeSpan time)
        {
            if (generation != _animationGeneration) return;
            start ??= time;
            var elapsed = (time - start.Value).TotalMilliseconds;
            _position = from + GestureSettlement.Position(0, distancePx, velocityPx, elapsed, duration) / stride;
            Render();
            if (elapsed >= duration) { _position = target; Render(); }
            else host.RequestAnimationFrame(Frame);
        }
        host.RequestAnimationFrame(Frame);
    }

    private void StartAmbient()
    {
        if (_ambientRunning || !MotionEnabled || TopLevel.GetTopLevel(this) is not { } host) return;
        _ambientRunning = true;
        void Tick(TimeSpan time)
        {
            if (!IsVisible || !MotionEnabled)
            {
                _ambientRunning = false;
                _ambientX = _ambientY = 0;
                return;
            }
            var seconds = time.TotalSeconds;
            // Continuous, organic 3D Lissajous tilt animation:
            // strong enough to be clearly noticeable in depth and perspective.
            _ambientY = Math.Sin(seconds * 0.9) * 11.0;
            _ambientX = Math.Sin(seconds * 0.65 + 1.1) * 7.0 + 2.0;
            Render();
            host.RequestAnimationFrame(Tick);
        }
        host.RequestAnimationFrame(Tick);
    }

    // ------------------------------------------------------------------
    // Swipe + wheel + keys
    // ------------------------------------------------------------------

    private void OnWheel(object? sender, PointerWheelEventArgs e)
    {
        if (_slides.Count < 2) return;
        if (e.Source is Visual source && !source.GetSelfAndVisualAncestors().Contains(_viewport)) return;
        e.Handled = true;
        if (e.IsPrecise)
        {
            var time = e.Timestamp > 0 ? (long)e.Timestamp : Environment.TickCount64;
            DragBy(e.GetScrollDelta(), time);
            if (e.IsScrollEnd) SettleSwipe(time);
            return;
        }
        var now = Environment.TickCount64;
        if (now - _lastWheelStep < 220) return;
        var delta = Math.Abs(e.Delta.X) > Math.Abs(e.Delta.Y) ? e.Delta.X : e.Delta.Y;
        if (Math.Abs(delta) < 0.5) return;
        var target = _index + (delta < 0 ? 1 : -1);
        if (target < 0 || target >= _slides.Count) return;
        _lastWheelStep = now;
        GoTo(target);
    }

    private void DragBy(Vector delta, long time)
    {
        if (!double.IsFinite(delta.X) || !double.IsFinite(delta.Y)) return;
        _swipeIdle.Stop();
        _swipeIdle.Start(); // Fallback for devices that do not send axis_stop.
        _axisTravel += delta;
        if (_horizontal is null)
        {
            if (Math.Max(Math.Abs(_axisTravel.X), Math.Abs(_axisTravel.Y)) < 4) return;
            _horizontal = Math.Abs(_axisTravel.X) >= Math.Abs(_axisTravel.Y);
            delta = _axisTravel;
        }
        var movement = _horizontal.Value ? delta.X : delta.Y;
        if (movement == 0) return;
        if (!_dragging)
        {
            ++_animationGeneration; // Interrupt settling from where it is.
            _dragging = true;
            _dragPx = (_index - _position) * StrideFrom(_index, _position > _index ? 1 : -1);
        }
        _velocity.Add(movement, time);
        _dragPx += movement;
        var direction = _dragPx < 0 ? 1 : -1;
        var raw = _index - _dragPx / StrideFrom(_index, direction);
        var last = _slides.Count - 1;
        // Rubber band beyond the first and last variants.
        if (raw < 0) raw = -0.35 * -raw / (-raw + 0.35);
        else if (raw > last) raw = last + 0.35 * (raw - last) / (raw - last + 0.35);
        _position = raw;
        Render();
    }

    private void SettleSwipe(long? releaseTime)
    {
        _swipeIdle.Stop();
        _horizontal = null;
        _axisTravel = default;
        if (!_dragging) { _velocity.Reset(); return; }
        _dragging = false;
        var velocity = _velocity.VelocityAt(releaseTime ?? _velocity.LastMotionTime);
        _velocity.Reset();
        var direction = _dragPx < 0 ? 1 : -1;
        var stride = StrideFrom(_index, direction);
        int target;
        if (Math.Abs(_dragPx) > stride * 1.1)
            target = (int)Math.Round(_position);
        else
            target = LauncherSwipePhysics.ShouldCommit(_dragPx, velocity, stride * 2.4) ? _index + direction : _index;
        target = Math.Clamp(target, 0, _slides.Count - 1);
        _dragPx = 0;
        AnimateTo(target, -velocity / stride);
    }

    private void CancelSwipe()
    {
        _swipeIdle.Stop();
        _dragging = false;
        _dragPx = 0;
        _horizontal = null;
        _axisTravel = default;
        _velocity.Reset();
    }

    private void OnLibraryKeyDown(object? sender, KeyEventArgs e)
    {
        if (!IsOpen) return;
        if (e.Key == Key.Left) { GoTo(_index - 1); e.Handled = true; }
        else if (e.Key == Key.Right) { GoTo(_index + 1); e.Handled = true; }
        else if (e.Key == Key.Enter) { AddCurrent(); e.Handled = true; }
    }

    // ------------------------------------------------------------------
    // Open / dismiss
    // ------------------------------------------------------------------

    public void Open()
    {
        var frame = this.FindControl<Control>("LibraryFrame");
        if (frame is null)
            IsVisible = true;
        else
            // Scale + fade the sheet instead of snapping a full-screen surface in.
            // The host stays non-hit-testable until the animation owns input.
            _ = GuiMotion.SetScaleVisibleAsync(this, frame, true, closedScale: 0.96);

        FitLibrary();
        LayoutSlides();
        Render();
        Focus();
        StartAmbient();
    }

    public void Dismiss()
    {
        // A second dismiss while the sheet is already leaving must not hide the
        // host early or report the dismissal twice.
        if (_dismissing) return;
        CancelSwipe();
        var frame = this.FindControl<Control>("LibraryFrame");
        if (frame is null || !GuiMotion.IsOpen(this))
        {
            if (frame is not null) GuiMotion.Cancel(frame);
            if (!IsVisible) { Dismissed?.Invoke(); return; }
            IsVisible = false;
            Dismissed?.Invoke();
            return;
        }
        _dismissing = true;
        _ = DismissAsync(frame);
    }

    private async Task DismissAsync(Control frame)
    {
        try
        {
            await GuiMotion.SetScaleVisibleAsync(this, frame, false, closedScale: 0.96);
            // Reopening cancels the exit. Only report a dismissal that actually
            // finished so callers cannot tear down state the sheet still owns.
            if (!GuiMotion.IsOpen(this)) Dismissed?.Invoke();
        }
        finally
        {
            _dismissing = false;
        }
    }

    private void OnBackdropPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var card = this.FindControl<Border>("LibraryCard");
        if (card != null)
        {
            var posInCard = e.GetPosition(card);
            if (new Rect(0, 0, card.Bounds.Width, card.Bounds.Height).Contains(posInCard))
                return;
        }

        Dismiss();
        e.Handled = true;
    }

    // ------------------------------------------------------------------
    // Handlers
    // ------------------------------------------------------------------

    private void OnClockCategoryClicked(object? sender, RoutedEventArgs e) => ShowCategory("Clock", true);
    private void OnCalendarCategoryClicked(object? sender, RoutedEventArgs e) => ShowCategory("Calendar", true);
    private void OnMediaCategoryClicked(object? sender, RoutedEventArgs e) => ShowCategory("Media", true);
    private void OnPhotosCategoryClicked(object? sender, RoutedEventArgs e) => ShowCategory("Photos", true);
    private void OnDoneClicked(object? sender, RoutedEventArgs e) => Dismiss();
    private void OnAddWidgetClicked(object? sender, RoutedEventArgs e) => AddCurrent();

    private void AddCurrent()
    {
        if (_slides.Count == 0) return;
        var variant = _slides[_index].Variant;
        WidgetAddRequested?.Invoke(variant.Type, variant.Columns, variant.Rows);
    }

    private void OnCardPressed(Slide slide, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(slide.Card).Properties.IsLeftButtonPressed) return;
        e.Handled = true;
        var index = _slides.IndexOf(slide);
        if (index != _index)
        {
            GoTo(index);
            return;
        }
        StartDragForPreview(slide.Variant.Type, slide.Variant.Columns, slide.Variant.Rows, slide.Card, e);
    }

    private void StartDragForPreview(string widgetType, int colSpan, int rowSpan, Control card, PointerPressedEventArgs e)
    {
        var surface = this.FindAncestorOfType<Desktop.DesktopView>();
        Point posInSurface = surface != null ? e.GetPosition(surface) : e.GetPosition(this);
        Point grabOffset = e.GetPosition(card);
        Size cardSize = card.Bounds.Size;

        WidgetDragRequested?.Invoke(widgetType, colSpan, rowSpan, posInSurface, grabOffset, cardSize);
    }

    private void OnAddMediaToBottomShelfClicked(object? sender, RoutedEventArgs e)
    {
        var desktopView = OwnerDesktop;
        desktopView?.ViewModel?.MoveMediaWidgetToBottomShelf();
        Dismiss();
    }
}
