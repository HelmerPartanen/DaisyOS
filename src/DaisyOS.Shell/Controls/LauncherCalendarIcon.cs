using Avalonia;
using Avalonia.Controls;

namespace DaisyOS.Shell.Controls;

/// <summary>Builds the live date icon only for a calendar tile.</summary>
public sealed class LauncherCalendarIcon : Decorator
{
    public static readonly StyledProperty<bool> IsCalendarProperty =
        AvaloniaProperty.Register<LauncherCalendarIcon, bool>(nameof(IsCalendar));

    public bool IsCalendar
    {
        get => GetValue(IsCalendarProperty);
        set => SetValue(IsCalendarProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == IsCalendarProperty)
            Child = IsCalendar ? new CalendarAppIcon() : null;
    }
}
