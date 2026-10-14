using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Reactive;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Adds missing color transitions without replacing interaction or layout motion.</summary>
public sealed class ThemeTransitionPolicy : AvaloniaObject
{
    private static readonly ConditionalWeakTable<Control, State> States = new();
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<ThemeTransitionPolicy, Control, bool>("Enabled");

    static ThemeTransitionPolicy()
    {
        EnabledProperty.Changed.AddClassHandler<Control>((control, change) =>
        {
            if (change.NewValue is true) States.GetValue(control, value => new State(value)).Refresh();
            else if (States.TryGetValue(control, out var state)) state.Refresh();
        });
    }

    public static bool GetEnabled(Control control) => control.GetValue(EnabledProperty);
    public static void SetEnabled(Control control, bool value) => control.SetValue(EnabledProperty, value);

    private sealed class State
    {
        private readonly Control _control;
        private bool _updating;
        private TimeSpan _duration;
        private readonly HashSet<ITransition> _owned = new();

        public State(Control control)
        {
            _control = control;
            if (control is Image image) _ = new ThemeImageTransition(image);
            control.AttachedToVisualTree += (_, _) => AddTransitions();
            control.PropertyChanged += (_, change) =>
            {
                if (change.Property == Animatable.TransitionsProperty) AddTransitions();
            };
            control.GetResourceObservable("MotionDurationBase").Subscribe(new AnonymousObserver<object?>(value =>
            {
                if (value is not TimeSpan duration || duration == _duration) return;
                _duration = duration;
                foreach (var transition in _owned.OfType<TransitionBase>()) transition.Duration = duration;
                AddTransitions();
            }));
            AddTransitions();
        }

        public void Refresh() => AddTransitions();

        private void AddTransitions()
        {
            if (_updating) return;
            var properties = AvaloniaPropertyRegistry.Instance.GetRegistered(_control.GetType())
                .Where(property => !property.IsDirect && (property.PropertyType == typeof(IBrush) ||
                    (property.PropertyType == typeof(Color) && property.Name != "CurrentColor")))
                // PopupRoot starts white before its transparent theme applies.
                // Its native-surface background must never fade into transparency.
                .Where(property => _control is not PopupRoot || property != TemplatedControl.BackgroundProperty)
                .Distinct().ToArray();
            var existing = _control.Transitions;
            _owned.IntersectWith(existing?.Cast<ITransition>() ?? Enumerable.Empty<ITransition>());
            if (_duration <= TimeSpan.Zero || !GetEnabled(_control))
            {
                if (existing == null || !existing.Any(transition => _owned.Contains(transition))) return;
                var immediate = new Transitions();
                foreach (var transition in existing.Where(transition => !_owned.Contains(transition))) immediate.Add(transition);
                _updating = true;
                try { _control.SetCurrentValue(Animatable.TransitionsProperty, immediate); _owned.Clear(); }
                finally { _updating = false; }
                return;
            }
            var missing = properties.Where(property => existing == null || !existing.Any(transition => transition.Property == property)).ToArray();
            if (missing.Length == 0) return;
            var transitions = new Transitions();
            if (existing != null)
                foreach (var transition in existing) transitions.Add(transition);
            foreach (var property in missing)
            {
                TransitionBase transition = property.PropertyType == typeof(Color) ? new ColorTransition() : new BrushTransition();
                transition.Property = property;
                transition.Duration = _duration;
                _owned.Add(transition);
                transitions.Add(transition);
            }
            _updating = true;
            try { _control.SetCurrentValue(Animatable.TransitionsProperty, transitions); }
            finally { _updating = false; }
        }
    }
}
