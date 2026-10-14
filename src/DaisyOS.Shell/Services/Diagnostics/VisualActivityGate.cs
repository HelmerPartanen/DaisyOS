using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Observes ancestor visibility, including overlays that remain attached while hidden.</summary>
internal sealed class VisualActivityGate
{
    private readonly Control _control;
    private readonly Action<bool> _changed;
    private readonly List<Visual> _ancestors = [];
    public VisualActivityGate(Control control, Action<bool> changed)
    {
        _control = control; _changed = changed;
        control.Loaded += (_, _) => Attach();
        control.Unloaded += (_, _) => Detach();
    }
    private void Attach()
    {
        Detach();
        _ancestors.AddRange(_control.GetVisualAncestors().Prepend(_control));
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged += OnChanged;
        Update();
    }
    private void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Visual.IsVisibleProperty || e.Property == Visual.OpacityProperty) Update();
    }
    private void Update() => _changed(_control.IsEffectivelyVisible && _ancestors.All(ancestor => ancestor.Opacity > 0));
    private void Detach()
    {
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= OnChanged;
        _ancestors.Clear(); _changed(false);
    }
}
