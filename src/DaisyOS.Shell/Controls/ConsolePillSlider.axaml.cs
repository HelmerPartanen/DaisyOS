using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

public partial class ConsolePillSlider : UserControl
{
    public static readonly StyledProperty<string> IconProperty =
        AvaloniaProperty.Register<ConsolePillSlider, string>(nameof(Icon), "speaker-high");

    public static readonly StyledProperty<double> ValueProperty =
        AvaloniaProperty.Register<ConsolePillSlider, double>(nameof(Value), 50.0, defaultBindingMode: Avalonia.Data.BindingMode.TwoWay);

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<ConsolePillSlider, double>(nameof(Minimum), 0.0);

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<ConsolePillSlider, double>(nameof(Maximum), 100.0);

    public static readonly StyledProperty<double> StepProperty =
        AvaloniaProperty.Register<ConsolePillSlider, double>(nameof(Step), 10.0);

    public event EventHandler<double>? ValueChanged;

    public string Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public double Value
    {
        get => GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
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

    public double Step
    {
        get => GetValue(StepProperty);
        set => SetValue(StepProperty, value);
    }

    static ConsolePillSlider()
    {
        IconProperty.Changed.AddClassHandler<ConsolePillSlider>((s, e) => s.UpdateIcon());
        ValueProperty.Changed.AddClassHandler<ConsolePillSlider>((s, e) => s.UpdatePills());
    }

    public ConsolePillSlider()
    {
        InitializeComponent();
        UpdateIcon();
        UpdatePills();

        GotFocus += (s, e) => { if (FocusBorder != null) FocusBorder.IsVisible = true; };
        LostFocus += (s, e) => { if (FocusBorder != null) FocusBorder.IsVisible = false; };
    }

    private void UpdateIcon()
    {
        if (IconTextBlock != null)
        {
            IconTextBlock.Icon = Icon;
        }
    }

    private IBrush GetFilledBrush()
    {
        if (Application.Current?.TryGetResource("ContentPrimaryBrush", null, out var res) == true && res is IBrush b)
            return b;
        return Brushes.Transparent;
    }

    private IBrush GetEmptyBrush()
    {
        if (Application.Current?.TryGetResource("StrokeDefaultBrush", null, out var res) == true && res is IBrush b)
            return b;
        return Brushes.Transparent;
    }

    public void UpdatePills()
    {
        if (PillContainer == null || ValueText == null) return;

        var val = Math.Clamp(Value, Minimum, Maximum);
        var percent = (int)Math.Round(val);
        ValueText.Text = $"{percent}%";

        var filledCount = Math.Clamp((int)Math.Round((val - Minimum) / ((Maximum - Minimum) / 10.0)), 0, 10);

        var filled = GetFilledBrush();
        var empty = GetEmptyBrush();

        for (int i = 0; i < PillContainer.Children.Count; i++)
        {
            if (PillContainer.Children[i] is Border pill)
            {
                pill.Background = i < filledCount ? filled : empty;
            }
        }

        ValueChanged?.Invoke(this, val);
    }

    private void OnDecrementClicked(object? sender, RoutedEventArgs e)
    {
        Value = Math.Max(Minimum, Value - Step);
    }

    private void OnIncrementClicked(object? sender, RoutedEventArgs e)
    {
        Value = Math.Min(Maximum, Value + Step);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Left)
        {
            Value = Math.Max(Minimum, Value - Step);
            e.Handled = true;
        }
        else if (e.Key == Key.Right)
        {
            Value = Math.Min(Maximum, Value + Step);
            e.Handled = true;
        }
        base.OnKeyDown(e);
    }
}
