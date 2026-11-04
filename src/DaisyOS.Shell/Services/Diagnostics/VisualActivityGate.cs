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
    private bool? _active;
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
    private void Update() => SetActive(_control.IsEffectivelyVisible && _ancestors.All(ancestor => ancestor.Opacity > 0));
    private void SetActive(bool active)
    {
        // Opacity changes on every animation frame. Consumers start timers and
        // preload batches here, so only publish actual activity transitions.
        if (_active == active) return;
        _active = active;
        _changed(active);
    }
    private void Detach()
    {
        foreach (var ancestor in _ancestors) ancestor.PropertyChanged -= OnChanged;
        _ancestors.Clear(); SetActive(false);
    }
}
