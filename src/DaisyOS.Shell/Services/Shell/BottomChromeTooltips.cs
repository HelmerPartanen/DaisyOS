using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Services.Shell;

/// <summary>One passive label above the bottom chrome, never an input-owning popup.</summary>
public sealed class BottomChromeTooltips : Canvas
{
    private readonly Border _card;
    private readonly TextBlock _label;
    private readonly DispatcherTimer _delay = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private Control? _owner;
    private Control? _dismissedOwner;
    private Func<Control, bool>? _suppress;

    public BottomChromeTooltips()
    {
        IsHitTestVisible = false;
        ZIndex = 1000;
        _label = new TextBlock { MaxWidth = 360, TextWrapping = TextWrapping.Wrap };
        _label.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("TypeSizeCaption"));
        _label.Bind(TextBlock.FontFamilyProperty, this.GetResourceObservable("ShellFontFamily"));
        _label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        _card = new Border
        {
            Child = _label, Padding = new Thickness(12, 7), CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1), IsVisible = false, IsHitTestVisible = false
        };
        _card.Bind(Border.BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));
        _card.Bind(Border.BorderBrushProperty, this.GetResourceObservable("StrokeSubtleBrush"));
        Children.Add(_card);
        _delay.Tick += (_, _) => ShowPending();
        DetachedFromVisualTree += (_, _) => Dismiss();
    }

    public void Attach(UserControl root, Func<Control, bool>? suppress = null)
    {
        // Keep Tip bindings as the source of localized/dynamic text, but disable
        // Avalonia's popup service for all current and future descendants.
        root.Styles.Add(new Style(selector => selector.Is<Control>())
        {
            Setters = { new Setter(ToolTip.ShowDelayProperty, int.MaxValue), new Setter(ToolTip.BetweenShowDelayProperty, -1) }
        });
        root.AddHandler(InputElement.PointerMovedEvent, (_, e) =>
        {
            var owner = (e.Source as Visual)?.GetSelfAndVisualAncestors().OfType<Control>()
                .TakeWhile(control => control != root).FirstOrDefault(control => ToolTip.GetTip(control) is string { Length: > 0 });
            if (owner == _owner) return;
            Dismiss();
            _dismissedOwner = null;
            _owner = owner;
            _suppress = suppress;
            if (owner is not null) _delay.Start();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.PointerPressedEvent, (_, _) => DismissUntilExit(), RoutingStrategies.Tunnel, handledEventsToo: true);
        root.AddHandler(InputElement.PointerWheelChangedEvent, (_, _) => DismissUntilExit(), RoutingStrategies.Tunnel, handledEventsToo: true);
        root.PointerExited += (_, _) => { Dismiss(); _dismissedOwner = null; };
        root.PropertyChanged += (_, e) =>
        {
            if (e.Property == IsVisibleProperty && !root.IsVisible &&
                _owner?.GetSelfAndVisualAncestors().Contains(root) == true) Dismiss();
        };
        root.DetachedFromVisualTree += (_, _) => Dismiss();
    }

    private void DismissUntilExit()
    {
        _dismissedOwner = _owner;
        _delay.Stop();
        _card.IsVisible = false;
    }

    public void Dismiss()
    {
        _delay.Stop();
        _card.IsVisible = false;
        _owner = null;
    }

    private void ShowPending()
    {
        if (_owner is not { IsEffectivelyVisible: true, IsPointerOver: true } owner || owner == _dismissedOwner || _suppress?.Invoke(owner) == true)
        {
            _card.IsVisible = false;
            return;
        }
        if (ToolTip.GetTip(owner) is not string { Length: > 0 } text) { Dismiss(); return; }
        var top = TopLevel.GetTopLevel(owner);
        if (top is null || top.Bounds.Width <= 0) return;
        var point = owner.TranslatePoint(default, top);
        if (point is null) return;
        _label.Text = text;
        _card.IsVisible = true;
        _card.Measure(new Size(Math.Max(0, Bounds.Width - 16), double.PositiveInfinity));
        var size = _card.DesiredSize;
        var center = (point.Value.X + owner.Bounds.Width / 2) / top.Bounds.Width * Bounds.Width;
        SetLeft(_card, Math.Clamp(center - size.Width / 2, 8, Math.Max(8, Bounds.Width - size.Width - 8)));
        // The reserved strip includes the 3px clearance above the 52px taskbar frame.
        SetTop(_card, Bounds.Height - ShellSurfacePlan.PrimaryPanelExclusiveZone - size.Height);
    }
}
