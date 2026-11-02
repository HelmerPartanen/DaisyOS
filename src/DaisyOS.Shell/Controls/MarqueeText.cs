using System;
using System.Diagnostics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;

namespace DaisyOS.Shell.Controls;

/// <summary>A single line which scrolls left only when its text exceeds the available width.</summary>
public sealed class MarqueeText : UserControl
{
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<MarqueeText, string?>(nameof(Text));

    private readonly Canvas _viewport = new() { ClipToBounds = true, IsHitTestVisible = false };
    private readonly TextBlock _text = new()
    {
        TextWrapping = TextWrapping.NoWrap,
        TextAlignment = TextAlignment.Left,
        HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left
    };
    private readonly TranslateTransform _translation = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(33) };
    private readonly Stopwatch _clock = new();
    private double _textWidth;
    private double _offset;
    private double _lastSeconds;

    public string? Text { get => GetValue(TextProperty); set => SetValue(TextProperty, value); }

    public MarqueeText()
    {
        ClipToBounds = true;
        IsHitTestVisible = false;
        HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
        _text.RenderTransform = _translation;
        _text.Bind(TextBlock.TextProperty, this.GetObservable(TextProperty));
        _text.Bind(TextBlock.FontFamilyProperty, this.GetObservable(FontFamilyProperty));
        _text.Bind(TextBlock.FontSizeProperty, this.GetObservable(FontSizeProperty));
        _text.Bind(TextBlock.FontWeightProperty, this.GetObservable(FontWeightProperty));
        _text.Bind(TextBlock.ForegroundProperty, this.GetObservable(ForegroundProperty));
        Canvas.SetLeft(_text, 0);
        _viewport.Children.Add(_text);
        Content = _viewport;
        _timer.Tick += (_, _) => Tick();
        SizeChanged += (_, _) => Refresh();
        PropertyChanged += (_, e) =>
        {
            if (e.Property == TextProperty || e.Property == FontSizeProperty || e.Property == FontFamilyProperty || e.Property == FontWeightProperty)
                Dispatcher.UIThread.Post(Refresh, DispatcherPriority.Loaded);
        };
        EffectiveViewportChanged += (_, _) => UpdateTimer();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _text.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var measuredWidth = _text.DesiredSize.Width;
        if (Math.Abs(_textWidth - measuredWidth) > 0.01)
        {
            _textWidth = measuredWidth;
            Dispatcher.UIThread.Post(UpdateTimer, DispatcherPriority.Loaded);
        }
        var width = double.IsInfinity(availableSize.Width) ? _textWidth : availableSize.Width;
        _viewport.Width = width;
        _viewport.Height = _text.DesiredSize.Height;
        base.MeasureOverride(new Size(width, _viewport.Height));
        return new Size(width, _viewport.Height);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Refresh();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _timer.Stop();
        _clock.Reset();
        base.OnDetachedFromVisualTree(e);
    }

    private void Refresh()
    {
        _offset = 0;
        _translation.X = 0;
        InvalidateMeasure();
        UpdateTimer();
    }

    private void UpdateTimer()
    {
        if (VisualRoot is null || !IsEffectivelyVisible || _textWidth <= Bounds.Width + 0.5 || Application.Current is App { ReduceMotionEnabled: true })
        {
            _timer.Stop();
            _clock.Reset();
            _translation.X = 0;
            return;
        }
        if (_timer.IsEnabled) return;
        _clock.Restart();
        _lastSeconds = 0;
        _timer.Start();
    }

    private void Tick()
    {
        if (!IsEffectivelyVisible || Application.Current is App { ReduceMotionEnabled: true }) { UpdateTimer(); return; }
        double now = _clock.Elapsed.TotalSeconds;
        _offset += Math.Min(now - _lastSeconds, 0.1) * 28;
        _lastSeconds = now;
        if (_offset > _textWidth + 32) _offset = -Bounds.Width;
        _translation.X = -_offset;
    }
}
