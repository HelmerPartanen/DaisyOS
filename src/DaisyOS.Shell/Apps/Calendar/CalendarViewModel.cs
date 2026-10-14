using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using Avalonia;
using Avalonia.Threading;
using DaisyOS.Core.Helpers;

namespace DaisyOS.Shell.Apps.Calendar;

public class CalendarViewModel : INotifyPropertyChanged, IDisposable
{
    private DateTime _currentDate;
    private DateTime _currentWeekStart;
    private readonly DispatcherTimer _timeTimer;
    private readonly CalendarEventStore _eventStore = new();
    private readonly List<CalendarEventRecord> _storedEvents;
    private double _currentTimeTopOffset;
    private bool _isEventComposerOpen;
    private string _newEventTitle = string.Empty;
    private DateTimeOffset? _newEventDate;
    private string _newEventStartTime = "09:00";
    private string _newEventEndTime = "10:00";
    private string? _eventComposerError;

    public ObservableCollection<CalendarDayViewModel> WeekDays { get; } = new();
    public ObservableCollection<CalendarEventViewModel> Events { get; } = new();

    public ICommand NextWeekCommand { get; }
    public ICommand PreviousWeekCommand { get; }
    public ICommand GoToTodayCommand { get; }
    public ICommand OpenEventComposerCommand { get; }
    public ICommand CancelEventComposerCommand { get; }
    public ICommand SaveEventCommand { get; }

    public DateTime CurrentDate
    {
        get => _currentDate;
        set
        {
            if (SetField(ref _currentDate, value))
            {
                UpdateWeekView();
                OnPropertyChanged(nameof(CurrentMonthYear));
                OnPropertyChanged(nameof(WeekRange));
                OnPropertyChanged(nameof(IsCurrentWeek));
            }
        }
    }

    public string CurrentMonthYear => _currentWeekStart.ToString("MMMM yyyy");

    public string WeekRange
    {
        get
        {
            var weekEnd = _currentWeekStart.AddDays(6);
            return _currentWeekStart.Month == weekEnd.Month
                ? $"{_currentWeekStart:d}–{weekEnd:d MMMM}"
                : $"{_currentWeekStart:d MMM} – {weekEnd:d MMM}";
        }
    }

    public bool IsCurrentWeek =>
        DateTime.Today >= _currentWeekStart && DateTime.Today < _currentWeekStart.AddDays(7);

    public double CurrentTimeTopOffset
    {
        get => _currentTimeTopOffset;
        set => SetField(ref _currentTimeTopOffset, value);
    }

    public bool IsEventComposerOpen
    {
        get => _isEventComposerOpen;
        private set
        {
            if (!SetField(ref _isEventComposerOpen, value)) return;
            OnPropertyChanged(nameof(IsEventComposerClosed));
        }
    }

    public bool IsEventComposerClosed => !IsEventComposerOpen;

    public string NewEventTitle
    {
        get => _newEventTitle;
        set => SetField(ref _newEventTitle, value);
    }

    public DateTimeOffset? NewEventDate
    {
        get => _newEventDate;
        set => SetField(ref _newEventDate, value);
    }

    public string NewEventStartTime
    {
        get => _newEventStartTime;
        set => SetField(ref _newEventStartTime, value);
    }

    public string NewEventEndTime
    {
        get => _newEventEndTime;
        set => SetField(ref _newEventEndTime, value);
    }

    public string? EventComposerError
    {
        get => _eventComposerError;
        private set
        {
            if (!SetField(ref _eventComposerError, value)) return;
            OnPropertyChanged(nameof(HasEventComposerError));
        }
    }

    public bool HasEventComposerError => !string.IsNullOrWhiteSpace(EventComposerError);

    public string StartTimePlaceholder => FormatTime(TimeSpan.FromHours(9));
    public string EndTimePlaceholder => FormatTime(TimeSpan.FromHours(10));
    public string Time8 => FormatTime(TimeSpan.FromHours(8));
    public string Time9 => FormatTime(TimeSpan.FromHours(9));
    public string Time10 => FormatTime(TimeSpan.FromHours(10));
    public string Time11 => FormatTime(TimeSpan.FromHours(11));
    public string Time12 => FormatTime(TimeSpan.FromHours(12));
    public string Time13 => FormatTime(TimeSpan.FromHours(13));
    public string Time14 => FormatTime(TimeSpan.FromHours(14));
    public string Time15 => FormatTime(TimeSpan.FromHours(15));
    public string Time16 => FormatTime(TimeSpan.FromHours(16));
    public string Time17 => FormatTime(TimeSpan.FromHours(17));
    public string Time18 => FormatTime(TimeSpan.FromHours(18));

    public CalendarViewModel()
    {
        _storedEvents = new();
        Dispatcher.UIThread.Post(() => _ = LoadEventsAsync(), DispatcherPriority.Background);

        NextWeekCommand = new RelayCommand(NextWeek);
        PreviousWeekCommand = new RelayCommand(PreviousWeek);
        GoToTodayCommand = new RelayCommand(GoToToday);
        OpenEventComposerCommand = new RelayCommand(OpenEventComposer);
        CancelEventComposerCommand = new RelayCommand(CancelEventComposer);
        SaveEventCommand = new RelayCommand(SaveEvent);

        CurrentDate = DateTime.Today;
        _timeTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(1) };
        _timeTimer.Tick += (_, _) => UpdateCurrentTime();
        _timeTimer.Start();
        UpdateCurrentTime();
        if (Application.Current is App app)
        {
            app.SessionState.Changed += OnSessionStateChanged;
        }
    }

    private readonly CancellationTokenSource _loadLifetime = new();
    private bool _isLoading = true;
    public bool IsLoading { get => _isLoading; private set => SetField(ref _isLoading, value); }
    private async Task LoadEventsAsync()
    {
        var token = _loadLifetime.Token;
        try
        {
            var events = await Task.Run(() => _eventStore.Load().ToList(), token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                _storedEvents.AddRange(events);
                IsLoading = false;
                RefreshEvents();
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        { IsLoading = false; global::System.Console.Error.WriteLine($"[calendar] Load failed: {ex.Message}"); }
    }
    public void SetActive(bool active) { if (active) { _timeTimer.Start(); UpdateCurrentTime(); } else _timeTimer.Stop(); }

    private void UpdateCurrentTime()
    {
        var timeSince8Am = DateTime.Now.TimeOfDay - TimeSpan.FromHours(8);
        CurrentTimeTopOffset = Math.Clamp(timeSince8Am.TotalMinutes, 0, 600);
    }

    public void NextWeek() => CurrentDate = CurrentDate.AddDays(7);

    public void PreviousWeek() => CurrentDate = CurrentDate.AddDays(-7);

    public void GoToToday() => CurrentDate = DateTime.Today;

    private void OpenEventComposer()
    {
        NewEventTitle = string.Empty;
        NewEventDate = new DateTimeOffset(CurrentDate.Date);
        NewEventStartTime = StartTimePlaceholder;
        NewEventEndTime = EndTimePlaceholder;
        EventComposerError = null;
        IsEventComposerOpen = true;
    }

    private void CancelEventComposer()
    {
        EventComposerError = null;
        IsEventComposerOpen = false;
    }

    private void SaveEvent()
    {
        if (IsLoading) { EventComposerError = "Events are still loading."; return; }
        var title = NewEventTitle.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            EventComposerError = "Add a title for the event.";
            return;
        }

        if (NewEventDate is null)
        {
            EventComposerError = "Choose a date for the event.";
            return;
        }

        if (!DateTime.TryParse(NewEventStartTime, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedStart) ||
            !DateTime.TryParse(NewEventEndTime, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out var parsedEnd))
        {
            EventComposerError = "Use times such as 09:00 and 10:00.";
            return;
        }

        var eventDate = NewEventDate.Value.Date;
        var start = eventDate + parsedStart.TimeOfDay;
        var end = eventDate + parsedEnd.TimeOfDay;
        if (end <= start)
        {
            EventComposerError = "The end time must be after the start time.";
            return;
        }

        _storedEvents.Add(new CalendarEventRecord(Guid.NewGuid(), title, start, end));
        try
        {
            _eventStore.Save(_storedEvents);
        }
        catch (Exception)
        {
            _storedEvents.RemoveAt(_storedEvents.Count - 1);
            EventComposerError = "Could not save this event. Please try again.";
            return;
        }

        CurrentDate = eventDate;
        RefreshEvents();
        IsEventComposerOpen = false;
    }

    private void UpdateWeekView()
    {
        var diff = (7 + (CurrentDate.DayOfWeek - FirstDayOfWeek)) % 7;
        _currentWeekStart = CurrentDate.AddDays(-diff).Date;

        WeekDays.Clear();
        for (var i = 0; i < 7; i++)
        {
            var date = _currentWeekStart.AddDays(i);
            WeekDays.Add(new CalendarDayViewModel
            {
                Date = date,
                DayName = date.ToString("ddd"),
                DayNumber = date.Day.ToString(CultureInfo.CurrentCulture),
                IsToday = date.Date == DateTime.Today
            });
        }

        RefreshEvents();
    }

    private static string FormatTime(TimeSpan time)
    {
        var useTwelveHourClock = string.Equals(
            (Application.Current as App)?.GetPreference("time.clock-format", "24-hour"),
            "12-hour",
            StringComparison.Ordinal);
        return DateTime.Today.Add(time).ToString(
            useTwelveHourClock ? "h:mm tt" : "HH:mm",
            CultureInfo.CurrentCulture);
    }

    private static DayOfWeek FirstDayOfWeek =>
        (Application.Current as App)?.GetPreference("time.first-day", "Monday") switch
        {
            "Sunday" => DayOfWeek.Sunday,
            "Saturday" => DayOfWeek.Saturday,
            _ => DayOfWeek.Monday
        };

    private void OnSessionStateChanged(object? sender, EventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            UpdateWeekView();
            foreach (var property in new[]
                     {
                         nameof(StartTimePlaceholder), nameof(EndTimePlaceholder), nameof(Time8), nameof(Time9),
                         nameof(Time10), nameof(Time11), nameof(Time12), nameof(Time13), nameof(Time14),
                         nameof(Time15), nameof(Time16), nameof(Time17), nameof(Time18)
                     })
            {
                OnPropertyChanged(property);
            }
        });
    }

    private void RefreshEvents()
    {
        Events.Clear();
        var weekEnd = _currentWeekStart.AddDays(7);
        foreach (var calendarEvent in _storedEvents
                     .Where(item => item.StartTime.Date >= _currentWeekStart && item.StartTime.Date < weekEnd)
                     .OrderBy(item => item.StartTime))
        {
            Events.Add(new CalendarEventViewModel
            {
                Id = calendarEvent.Id,
                Title = calendarEvent.Title,
                StartTime = calendarEvent.StartTime,
                EndTime = calendarEvent.EndTime,
                Column = (calendarEvent.StartTime.Date - _currentWeekStart).Days
            });
        }
    }

    public void Dispose()
    {
        _loadLifetime.Cancel();
        _timeTimer.Stop();
        if (Application.Current is App app)
        {
            app.SessionState.Changed -= OnSessionStateChanged;
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
