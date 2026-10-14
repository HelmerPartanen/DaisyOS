using System;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;

namespace DaisyOS.Shell.Services.Theming;

/// <summary>Resolves a data-bound semantic foreground key whenever its theme resource changes.</summary>
public sealed class ThemeResourceBrush : AvaloniaObject
{
    private static readonly ConditionalWeakTable<Control, BindingState> Bindings = new();
    public static readonly AttachedProperty<string?> ForegroundKeyProperty =
        AvaloniaProperty.RegisterAttached<ThemeResourceBrush, Control, string?>("ForegroundKey");

    static ThemeResourceBrush() => ForegroundKeyProperty.Changed.AddClassHandler<Control>((control, change) =>
    {
        var state = Bindings.GetValue(control, _ => new BindingState());
        state.Subscription?.Dispose();
        state.Subscription = change.NewValue is string key && key.Length > 0
            ? control.Bind(TextElement.ForegroundProperty, control.GetResourceObservable(key)) : null;
    });

    public static string? GetForegroundKey(Control control) => control.GetValue(ForegroundKeyProperty);
    public static void SetForegroundKey(Control control, string? value) => control.SetValue(ForegroundKeyProperty, value);

    private sealed class BindingState { public IDisposable? Subscription; }
}
