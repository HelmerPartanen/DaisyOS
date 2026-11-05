using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

/// <summary>
/// Shared quick-settings range control with an icon well and an optional vertical thumb.
/// </summary>
public partial class QuickSettingsSlider : UserControl
{
    private bool _isAdjustingFromTrack;
    private double _lastProgressFillWidth = double.NaN;
    private readonly Border? _trackBackground;
    private readonly Border? _progressFill;
    private readonly Border? _progressThumb;
    public event EventHandler? ValueChanged;
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, string>(nameof(Icon), "speaker-high");

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, double>(nameof(Minimum), 0d);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, double>(nameof(Maximum), 100d);

    public static readonly StyledProperty<double> StepFrequencyProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, double>(nameof(StepFrequency), 0d);
    public static readonly StyledProperty<bool> ShowTicksProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, bool>(nameof(ShowTicks));
    public static readonly StyledProperty<bool> ShowIconProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, bool>(nameof(ShowIcon), true);

    public double StepFrequency { get => GetValue(StepFrequencyProperty); set => SetValue(StepFrequencyProperty, value); }
    public bool ShowTicks { get => GetValue(ShowTicksProperty); set => SetValue(ShowTicksProperty, value); }
    public bool ShowIcon { get => GetValue(ShowIconProperty); set => SetValue(ShowIconProperty, value); }

    private double Snap(double value) => StepFrequency > 0
        ? Math.Clamp(Minimum + Math.Round((value - Minimum) / StepFrequency) * StepFrequency, Minimum, Maximum)
        : value;

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, double>(nameof(Value), 0d, defaultBindingMode: BindingMode.TwoWay, coerce: (slider, value) => ((QuickSettingsSlider)slider).Snap(value));

    public static readonly StyledProperty<bool> ShowThumbProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, bool>(nameof(ShowThumb), true);

    public static readonly StyledProperty<IBrush?> TrackBorderBrushProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, IBrush?>(nameof(TrackBorderBrush));

    public static readonly StyledProperty<Thickness> TrackBorderThicknessProperty =
        AvaloniaProperty.Register<QuickSettingsSlider, Thickness>(nameof(TrackBorderThickness), new Thickness(0));

    static QuickSettingsSlider()
    {
        ValueProperty.Changed.AddClassHandler<QuickSettingsSlider>((slider, e) =>
        {
            if (e.OldValue is double oldVal && e.NewValue is double newVal && Math.Abs(newVal - oldVal) < 0.0001)
                return;
            slider.UpdateProgressFill();
            slider.ValueChanged?.Invoke(slider, EventArgs.Empty);
        });
    }

    public bool IsDragging => _isAdjustingFromTrack;

    public QuickSettingsSlider()
    {
        InitializeComponent();
        _trackBackground = this.FindControl<Border>("TrackBackground");
        _progressFill = this.FindControl<Border>("ProgressFill");
        _progressThumb = this.FindControl<Border>("ProgressThumb");
        SizeChanged += (_, _) => { UpdateProgressFill(); UpdateTicks(); };
        PropertyChanged += (_, e) =>
        {
            if (e.Property == ShowTicksProperty || e.Property == StepFrequencyProperty || e.Property == MinimumProperty || e.Property == MaximumProperty)
            {
                CoerceValue(ValueProperty);
                UpdateTicks();
            }
        };
        AddHandler(PointerPressedEvent, OnTrackPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(PointerMovedEvent, OnTrackPointerMoved, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, OnTrackPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(PointerCaptureLostEvent, (_, _) => _isAdjustingFromTrack = false);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
    }

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    /// <summary>Controls the visual thumb without changing pointer or keyboard adjustment.</summary>
    public bool ShowThumb
    {
        get => GetValue(ShowThumbProperty);
        set => SetValue(ShowThumbProperty, value);
    }

    /// <summary>Optional border treatment for standalone range surfaces.</summary>
    public IBrush? TrackBorderBrush
    {
        get => GetValue(TrackBorderBrushProperty);
        set => SetValue(TrackBorderBrushProperty, value);
    }

    public Thickness TrackBorderThickness
    {
        get => GetValue(TrackBorderThicknessProperty);
        set => SetValue(TrackBorderThicknessProperty, value);
    }

    private void UpdateTicks()
    {
        var ticks = this.FindControl<Canvas>("TickMarks");
        if (ticks is null) return;
        ticks.Children.Clear();
        if (!ShowTicks || StepFrequency <= 0 || Maximum <= Minimum) return;
        var count = Math.Min(100, (int)Math.Floor((Maximum - Minimum) / StepFrequency));
        if (count < 1) return;
        for (var index = 0; index <= count; index++)
        {
            var mark = new Border { Width = 2, Height = 4, CornerRadius = new CornerRadius(1) };
            mark.Bind(Border.BackgroundProperty, this.GetResourceObservable("ContentTertiaryBrush"));
            Canvas.SetLeft(mark, Math.Max(0, ticks.Bounds.Width - 2) * index / count);
            ticks.Children.Add(mark);
        }
    }

    private void UpdateProgressFill()
    {
        if (_trackBackground is null || _progressFill is null || Maximum <= Minimum)
        {
            return;
        }

        var progress = Math.Clamp((Value - Minimum) / (Maximum - Minimum), 0d, 1d);
        var width = _trackBackground.Bounds.Width * progress;
        if (Math.Abs(width - _lastProgressFillWidth) < 0.01)
        {
            return;
        }

        _lastProgressFillWidth = width;
        _progressFill.Width = width;

        if (_progressThumb is not null)
        {
            // The thumb is a sibling to TrackBackground inside the same Panel.
            // TrackBackground has horizontal margins equal to half the thumb width.
            // So we simply offset the thumb by the exact fill width to perfectly align its center.
            _progressThumb.Margin = new Thickness(width, 0d, 0d, 0d);
        }
    }

    private void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _isAdjustingFromTrack = true;
        e.Pointer.Capture(this);
        UpdateValueFromPointer(e);
        e.Handled = true;
    }

    private void OnTrackPointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_isAdjustingFromTrack)
        {
            return;
        }

        UpdateValueFromPointer(e);
        e.Handled = true;
    }

    private void OnTrackPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_isAdjustingFromTrack)
        {
            return;
        }

        UpdateValueFromPointer(e);
        _isAdjustingFromTrack = false;
        e.Pointer.Capture(null);
        e.Handled = true;
    }

    private void UpdateValueFromPointer(PointerEventArgs e)
    {
        if (_trackBackground is null || _trackBackground.Bounds.Width <= 0d)
        {
            return;
        }

        var position = e.GetPosition(_trackBackground).X;
        var progress = Math.Clamp(position / _trackBackground.Bounds.Width, 0d, 1d);
        SetCurrentValue(ValueProperty, Minimum + ((Maximum - Minimum) * progress));
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var step = Math.Max(1, (Maximum - Minimum) / 20d);
        var value = e.Key switch
        {
            Key.Left or Key.Down => Value - step,
            Key.Right or Key.Up => Value + step,
            Key.Home => Minimum,
            Key.End => Maximum,
            _ => double.NaN
        };
        if (double.IsNaN(value)) return;
        SetCurrentValue(ValueProperty, Math.Clamp(value, Minimum, Maximum));
        e.Handled = true;
    }
}
