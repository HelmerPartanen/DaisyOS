using System.Collections.Specialized;
using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DaisyOS.Shell.Controls;

/// <summary>Keeps closed selectors stable during page scrolling and sizes them for their longest label.</summary>
public sealed class ComboBoxBehavior : AvaloniaObject
{
    private sealed class MeasurementState
    {
        public bool Pending;
    }

    private static readonly ConditionalWeakTable<ComboBox, MeasurementState> Measurements = new();
    public static readonly AttachedProperty<bool> UseAdaptiveWidthProperty =
        AvaloniaProperty.RegisterAttached<ComboBoxBehavior, ComboBox, bool>("UseAdaptiveWidth");

    static ComboBoxBehavior()
    {
        UseAdaptiveWidthProperty.Changed.AddClassHandler<ComboBox>((comboBox, change) =>
        {
            if (change.NewValue is not true) return;
            comboBox.AddHandler(InputElement.PointerWheelChangedEvent, OnPointerWheelChanged,
                RoutingStrategies.Tunnel, handledEventsToo: true);
            comboBox.AttachedToVisualTree += (_, _) => ScheduleMeasure(comboBox);
            if (comboBox.Items is INotifyCollectionChanged observable)
                observable.CollectionChanged += (_, _) => ScheduleMeasure(comboBox);
            ScheduleMeasure(comboBox);
        });
    }

    public static bool GetUseAdaptiveWidth(AvaloniaObject target) => target.GetValue(UseAdaptiveWidthProperty);
    public static void SetUseAdaptiveWidth(AvaloniaObject target, bool value) => target.SetValue(UseAdaptiveWidthProperty, value);

    private static void OnPointerWheelChanged(ComboBox sender, PointerWheelEventArgs e)
    {
        if (!sender.IsDropDownOpen) e.Handled = true;
    }

    private static void ScheduleMeasure(ComboBox comboBox)
    {
        // A catalog is commonly populated one item at a time. Measure its final
        // contents once instead of queuing a full scan for every added item.
        var state = Measurements.GetOrCreateValue(comboBox);
        if (state.Pending) return;
        state.Pending = true;
        Dispatcher.UIThread.Post(() =>
        {
            state.Pending = false;
            MeasureLongestLabel(comboBox);
        }, DispatcherPriority.Loaded);
    }

    private static void MeasureLongestLabel(ComboBox comboBox)
    {
        if (!comboBox.IsAttachedToVisualTree()) return;
        var longest = 0d;
        foreach (var item in comboBox.Items)
        {
            var text = item switch
            {
                ComboBoxItem { Content: TextBlock block } => block.Text,
                ComboBoxItem { Content: not null } option => option.Content.ToString(),
                TextBlock block => block.Text,
                _ => item?.ToString()
            };
            if (string.IsNullOrWhiteSpace(text)) continue;
            var probe = new TextBlock
            {
                Text = text,
                FontFamily = comboBox.FontFamily,
                FontSize = comboBox.FontSize,
                FontWeight = comboBox.FontWeight,
                TextWrapping = Avalonia.Media.TextWrapping.NoWrap
            };
            probe.Measure(Size.Infinity);
            longest = Math.Max(longest, probe.DesiredSize.Width);
        }

        if (longest <= 0) return;
        // Label + left padding + glyph column. Cap only at a window-safe size;
        // popup items still retain their full text and can use their tooltip.
        // Respect explicit compact widths (for example the Notes toolbar).
        // A minimum wider than Width otherwise silently enlarges the whole row.
        var limit = double.IsNaN(comboBox.Width) ? 520 : comboBox.Width;
        limit = Math.Min(limit, comboBox.MaxWidth);
        comboBox.MinWidth = Math.Min(limit, Math.Max(comboBox.MinWidth, Math.Ceiling(longest + 58)));
    }
}
