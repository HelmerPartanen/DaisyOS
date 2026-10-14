using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia;

namespace DaisyOS.Shell.Apps.Calendar;

public class CalendarEventViewModel : INotifyPropertyChanged
{
    private Guid _id;
    private string _title = string.Empty;
    private DateTime _startTime;
    private DateTime _endTime;
    private int _column;

    public Guid Id { get => _id; set => SetField(ref _id, value); }
    public string Title { get => _title; set => SetField(ref _title, value); }
    public int Column { get => _column; set => SetField(ref _column, value); }

    public DateTime StartTime
    {
        get => _startTime;
        set
        {
            if (!SetField(ref _startTime, value)) return;
            OnPropertyChanged(nameof(Margin));
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(TimeRange));
        }
    }

    public DateTime EndTime
    {
        get => _endTime;
        set
        {
            if (!SetField(ref _endTime, value)) return;
            OnPropertyChanged(nameof(Height));
            OnPropertyChanged(nameof(TimeRange));
        }
    }

    public Thickness Margin => new(4, TopOffset, 4, 0);
    public double Height => Math.Max(28, (EndTime - StartTime).TotalMinutes);
    public string TimeRange => $"{StartTime:t}–{EndTime:t}";
    private double TopOffset => Math.Max(0, (StartTime.TimeOfDay - TimeSpan.FromHours(8)).TotalMinutes);

    public event PropertyChangedEventHandler? PropertyChanged;
    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
