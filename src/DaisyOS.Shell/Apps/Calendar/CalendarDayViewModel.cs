using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media;

namespace DaisyOS.Shell.Apps.Calendar;

public class CalendarDayViewModel : INotifyPropertyChanged
{
    private DateTime _date;
    private string _dayName = string.Empty;
    private string _dayNumber = string.Empty;
    private bool _isToday;

    public DateTime Date
    {
        get => _date;
        set => SetField(ref _date, value);
    }

    public string DayName
    {
        get => _dayName;
        set => SetField(ref _dayName, value);
    }

    public string DayNumber
    {
        get => _dayNumber;
        set => SetField(ref _dayNumber, value);
    }

    public bool IsToday
    {
        get => _isToday;
        set => SetField(ref _isToday, value);
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (global::System.Collections.Generic.EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
