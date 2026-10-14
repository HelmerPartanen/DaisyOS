using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace DaisyOS.Shell.Views.Components.SystemBar;

public partial class CustomCalendarView : UserControl
{
    private DateTime _displayDate;
    private bool _monthsVisible;

    public CustomCalendarView()
    {
        InitializeComponent();
        _displayDate = DateTime.Now;
        RenderCalendar();
        UpdateTodayFooter();
        Loc.RefreshOnChange(this, () => { RenderCalendar(); UpdateTodayFooter(); });
    }

    private void OnPreviousMonthClicked(object? sender, RoutedEventArgs e)
    {
        _displayDate = _monthsVisible ? _displayDate.AddYears(-1) : _displayDate.AddMonths(-1);
        RenderCalendar();
    }

    private void OnNextMonthClicked(object? sender, RoutedEventArgs e)
    {
        _displayDate = _monthsVisible ? _displayDate.AddYears(1) : _displayDate.AddMonths(1);
        RenderCalendar();
    }

    private void UpdateTodayFooter()
    {
        var now = DateTime.Now;
        TodayDayText.Text = now.ToString("dd");
        TodayMonthText.Text = now.ToString("MMM");
        TodayYearText.Text = now.ToString("yyyy");
    }

    private void OnViewToggleClicked(object? sender, RoutedEventArgs e)
    {
        _monthsVisible = !_monthsVisible;
        RenderCalendar();
    }

    private void OnTodayClicked(object? sender, RoutedEventArgs e)
    {
        _displayDate = DateTime.Today;
        _monthsVisible = false;
        RenderCalendar();
        UpdateTodayFooter();
    }

    public void RenderCalendar()
    {
        MonthText.Text = _displayDate.ToString("MMMM");
        YearText.Text = _displayDate.ToString("yyyy");
        MonthText.IsVisible = !_monthsVisible;
        DaysView.IsVisible = !_monthsVisible;
        YearView.IsVisible = _monthsVisible;
        MonthsGrid.IsVisible = _monthsVisible;
        YearText.FontSize = 16;
        YearText.FontWeight = _monthsVisible ? FontWeight.SemiBold : FontWeight.Regular;
        ToolTip.SetTip(ViewToggle, _monthsVisible ? Loc.Get("Show days") : Loc.Get("Choose month"));
        ToolTip.SetTip(PreviousButton, _monthsVisible ? Loc.Get("Previous year") : Loc.Get("Previous month"));
        ToolTip.SetTip(NextButton, _monthsVisible ? Loc.Get("Next year") : Loc.Get("Next month"));
        MonthsGrid.Children.Clear();
        if (_monthsVisible)
        {
            for (var month = 1; month <= 12; month++)
            {
                var date = new DateTime(_displayDate.Year, month, 1);
                var content = new StackPanel { Spacing = 8 };
                var title = new TextBlock
                {
                    Text = date.ToString("MMM"), FontSize = 16, FontWeight = FontWeight.SemiBold,
                    Margin = new Thickness(2, 0, 0, 0)
                };
                title.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
                content.Children.Add(title);
                var days = new Avalonia.Controls.Primitives.UniformGrid { Columns = 7, Rows = 6 };
                var leading = ((int)date.DayOfWeek + 6) % 7;
                var count = DateTime.DaysInMonth(date.Year, month);
                for (var cell = 0; cell < 42; cell++)
                {
                    var day = cell - leading + 1;
                    var valid = day >= 1 && day <= count;
                    var miniToday = valid && date.Year == DateTime.Today.Year && month == DateTime.Today.Month && day == DateTime.Today.Day;
                    var label = new TextBlock
                    {
                        Text = valid ? day.ToString(CultureInfo.CurrentCulture) : string.Empty,
                        FontSize = 11, FontWeight = miniToday ? FontWeight.Bold : FontWeight.Regular,
                        HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
                    };
                    label.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable(miniToday ? "CalendarTodayForegroundBrush" : "ContentPrimaryBrush"));
                    var cellBorder = new Border { Height = 16, CornerRadius = new CornerRadius(8), Child = label };
                    if (miniToday) cellBorder.Bind(Border.BackgroundProperty, this.GetResourceObservable("CalendarTodayBackgroundBrush"));
                    days.Children.Add(cellBorder);
                }
                content.Children.Add(days);
                var button = new Button
                {
                    Content = content, Height = double.NaN, MinHeight = 0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Padding = new Thickness(6), Margin = new Thickness(4, 0, 4, 12),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch,
                    VerticalContentAlignment = VerticalAlignment.Top,
                    CornerRadius = new CornerRadius(8)
                };
                button.Classes.Add("Ghost");
                ToolTip.SetTip(button, date.ToString("MMMM yyyy"));
                button.Click += (_, _) =>
                {
                    _displayDate = date;
                    _monthsVisible = false;
                    RenderCalendar();
                };
                MonthsGrid.Children.Add(button);
            }
        }

        DaysGrid.Children.Clear();

        var firstDayOfMonth = new DateTime(_displayDate.Year, _displayDate.Month, 1);
        int daysInMonth = DateTime.DaysInMonth(_displayDate.Year, _displayDate.Month);

        // Calculate leading days from previous month (Monday = 1, Sunday = 7)
        int startingDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
        if (startingDayOfWeek == 0) startingDayOfWeek = 7; // Sunday is 0, make it 7
        int leadingDays = startingDayOfWeek - 1;

        var prevMonth = firstDayOfMonth.AddMonths(-1);
        int prevMonthDays = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

        var today = DateTime.Today;

        // Fill previous month days
        for (int i = 0; i < leadingDays; i++)
        {
            int day = prevMonthDays - leadingDays + i + 1;
            DaysGrid.Children.Add(CreateDayCell(day, isCurrentMonth: false, isToday: false));
        }

        // Fill current month days
        for (int i = 1; i <= daysInMonth; i++)
        {
            bool isToday = _displayDate.Year == today.Year && _displayDate.Month == today.Month && i == today.Day;
            DaysGrid.Children.Add(CreateDayCell(i, isCurrentMonth: true, isToday: isToday));
        }

        // Fill next month days
        int trailingDays = 42 - (leadingDays + daysInMonth);
        for (int i = 1; i <= trailingDays; i++)
        {
            DaysGrid.Children.Add(CreateDayCell(i, isCurrentMonth: false, isToday: false));
        }
    }

    private Control CreateDayCell(int day, bool isCurrentMonth, bool isToday)
    {
        var border = new Border
        {
            Width = 36,
            Height = 36,
            CornerRadius = new CornerRadius(18)
        };

        var textBlock = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = day.ToString(),
            FontSize = 14,
            FontWeight = isToday ? FontWeight.Bold : FontWeight.Regular,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, "Body");

        if (isToday)
        {
            border.Bind(Border.BackgroundProperty, this.GetResourceObservable("CalendarTodayBackgroundBrush"));
            textBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("CalendarTodayForegroundBrush"));
        }
        else if (isCurrentMonth)
        {
            textBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
        }
        else
        {
            textBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentTertiaryBrush"));
        }

        border.Child = textBlock;
        return border;
    }
}
