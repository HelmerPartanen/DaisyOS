using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Appearance;

namespace DaisyOS.Shell.Controls;

/// <summary>Measures the centre dock between the two end components, on either axis.</summary>
public sealed class DockComponentsPanel : Panel
{
    public DockEdge Edge { get; set; }
    public DockAlignment DockAlignment { get; set; } = DockAlignment.Center;
    public double ReservedExtent { get; set; } = 58;
    public bool IncludeMedia { get; set; } = true;
    public bool IncludeSystemBar { get; set; } = true;
    private Size _media, _taskbar, _system;
    private readonly Rect[] _slots = new Rect[3];
    public Rect GetComponentSlot(int index) => _slots[index];
    public int ComponentAtEdgePosition(double position, double extent)
    {
        var span = Along(Bounds.Size);
        if (span <= 0 || extent <= 0) return -1;
        var along = position / extent * span;
        for (var index = 0; index < _slots.Length; index++)
        {
            if ((index == 0 && !IncludeMedia) || (index == 2 && !IncludeSystemBar)) continue;
            var slot = _slots[index];
            var start = Vertical ? slot.Y : slot.X;
            var length = Vertical ? slot.Height : slot.Width;
            if (length > 0 && along >= start && along <= start + length) return index;
        }
        return -1;
    }
    private bool Vertical => Edge is DockEdge.Left or DockEdge.Right;
    private double Along(Size size) => Vertical ? size.Height : size.Width;
    private double Across(Size size) => Vertical ? size.Width : size.Height;
    private Size SizeFor(double along, double across) => Vertical ? new(across, along) : new(along, across);

    private static Size MeasureComponent(Control control, Size available)
    {
        control.Measure(available);
        if (control.IsVisible) return control.DesiredSize;
        // Auto-hide does not change layout: measure the still-present content,
        // rather than making components slide sideways when a neighbour reveals.
        if (control is ContentControl { Content: Control content })
        {
            content.Measure(available);
            return new Size(double.IsFinite(control.Width) ? control.Width : content.DesiredSize.Width,
                double.IsFinite(control.Height) ? control.Height : content.DesiredSize.Height);
        }
        return control.DesiredSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        if (Children.Count != 3) return availableSize;
        var along = Math.Max(1, Along(availableSize));
        var componentSpace = SizeFor(along, ReservedExtent);
        _media = IncludeMedia ? MeasureComponent(Children[0], componentSpace) : default;
        _system = IncludeSystemBar ? MeasureComponent(Children[2], componentSpace) : default;
        var centreSpace = Math.Max(40, along - Along(_media) - Along(_system) - 32);
        _taskbar = MeasureComponent(Children[1], SizeFor(centreSpace, ReservedExtent));
        return availableSize;
    }
    protected override Size ArrangeOverride(Size finalSize)
    {
        if (Children.Count != 3) return finalSize;
        var span = Along(finalSize);
        var min = IncludeMedia ? Along(_media) + 14 : 6;
        var max = Math.Max(min, span - (IncludeSystemBar ? Along(_system) + 14 : 6) - Along(_taskbar));
        var centre = DockAlignment switch
        {
            DockAlignment.Start => min, DockAlignment.End => max,
            _ => Math.Clamp((span - Along(_taskbar)) / 2, min, max)
        };
        ArrangeComponent(Children[0], _media, 6, finalSize);
        ArrangeComponent(Children[1], _taskbar, centre, finalSize);
        ArrangeComponent(Children[2], _system, Math.Max(6, span - Along(_system) - 6), finalSize);
        return finalSize;
    }
    private void ArrangeComponent(Control control, Size desired, double along, Size finalSize)
    {
        var cross = (ReservedExtent - Across(desired)) / 2;
        if (Edge is DockEdge.Bottom or DockEdge.Right) cross += Across(finalSize) - ReservedExtent;
        var rect = Vertical ? new Rect(cross, along, desired.Width, desired.Height) : new Rect(along, cross, desired.Width, desired.Height);
        _slots[Children.IndexOf(control)] = rect;
        control.Arrange(rect);
    }
}
