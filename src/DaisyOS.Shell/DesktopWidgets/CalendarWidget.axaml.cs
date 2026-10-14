using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Markup.Xaml;
using Avalonia.Media;
using Avalonia.Threading;

namespace DaisyOS.Shell.DesktopWidgets;

public partial class CalendarWidget : UserControl
{
    private DispatcherTimer? _timer;
    private int _lastRenderedDay = -1;

    public CalendarWidget()
    {
        InitializeComponent();
        UpdateCalendar();
        Loc.RefreshOnChange(this, UpdateCalendar);

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _timer.Tick += (s, e) =>
        {
            if (DateTime.Now.Day != _lastRenderedDay)
            {
                UpdateCalendar();
            }
        };
        _timer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _timer?.Stop();
    }

    private void UpdateCalendar()
    {
        var now = DateTime.Now;
        var culture = CultureInfo.CurrentCulture;
        _lastRenderedDay = now.Day;

        var dayOfWeek = this.FindControl<TextBlock>("DayOfWeekText");
        if (dayOfWeek != null)
        {
            dayOfWeek.Text = now.ToString("dddd", culture).ToUpper(culture);
        }

        var dayNumber = this.FindControl<TextBlock>("DayNumberText");
        if (dayNumber != null)
        {
            dayNumber.Text = now.Day.ToString(culture);
        }

        var monthHeader = this.FindControl<TextBlock>("MonthHeaderText");
        if (monthHeader != null)
        {
            monthHeader.Text = now.ToString("MMMM", culture).ToUpper(culture);
        }

        UpdateWeekdayHeaders(culture);

        var daysGrid = this.FindControl<UniformGrid>("DaysGrid");
        if (daysGrid != null)
        {
            daysGrid.Children.Clear();

            var firstDayOfMonth = new DateTime(now.Year, now.Month, 1);
            int daysInMonth = DateTime.DaysInMonth(now.Year, now.Month);

            // Calculate leading days from previous month (Monday = 1, Sunday = 7)
            int startingDayOfWeek = (int)firstDayOfMonth.DayOfWeek;
            if (startingDayOfWeek == 0) startingDayOfWeek = 7; // Sunday is 0, make it 7
            int leadingDays = startingDayOfWeek - 1;

            var prevMonth = firstDayOfMonth.AddMonths(-1);
            int prevMonthDays = DateTime.DaysInMonth(prevMonth.Year, prevMonth.Month);

            var today = DateTime.Today;

            // Fill previous month days (tertiary color)
            for (int i = 0; i < leadingDays; i++)
            {
                int day = prevMonthDays - leadingDays + i + 1;
                daysGrid.Children.Add(CreateDayCell(day, isCurrentMonth: false, isToday: false));
            }

            // Fill current month days
            for (int day = 1; day <= daysInMonth; day++)
            {
                bool isToday = now.Year == today.Year && now.Month == today.Month && day == today.Day;
                daysGrid.Children.Add(CreateDayCell(day, isCurrentMonth: true, isToday: isToday));
            }

            // Fill next month days (tertiary color)
            int filled = leadingDays + daysInMonth;
            int totalCells = filled > 35 ? 42 : 35;
            int trailingDays = totalCells - filled;
            for (int day = 1; day <= trailingDays; day++)
            {
                daysGrid.Children.Add(CreateDayCell(day, isCurrentMonth: false, isToday: false));
            }
        }
    }

    private void UpdateWeekdayHeaders(CultureInfo culture)
    {
        var headers = this.FindControl<UniformGrid>("WeekdayHeaders");
        if (headers == null) return;

        headers.Children.Clear();
        for (var offset = 0; offset < 7; offset++)
        {
            var day = (DayOfWeek)(((int)DayOfWeek.Monday + offset) % 7);
            var label = culture.DateTimeFormat.GetAbbreviatedDayName(day);
            if (label.Length > 2) label = label[..2];

            var text = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
            {
                Text = label,
                FontSize = 8.5,
                FontWeight = FontWeight.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center
            }, "Caption");
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentDisabledBrush"));
            headers.Children.Add(text);
        }
    }

    private Control CreateDayCell(int day, bool isCurrentMonth, bool isToday)
    {
        var textBlock = DaisyOS.Shell.Services.Theming.TypographyPolicy.Bind(new TextBlock
        {
            Text = day.ToString(CultureInfo.InvariantCulture),
            FontSize = 10,
            FontWeight = isToday ? FontWeight.Bold : FontWeight.Normal,
            TextAlignment = TextAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        }, "Caption");

        var cell = new Panel
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };

        if (isToday)
        {
            var badge = new Border
            {
                Width = 18,
                Height = 18,
                CornerRadius = new CornerRadius(9),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badge.Bind(Border.BackgroundProperty, this.GetResourceObservable("CalendarTodayBackgroundBrush"));
            textBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("CalendarTodayForegroundBrush"));

            cell.Children.Add(badge);
            cell.Children.Add(textBlock);
        }
        else
        {
            if (isCurrentMonth)
            {
                textBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentPrimaryBrush"));
            }
            else
            {
                textBlock.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentTertiaryBrush"));
            }

            cell.Children.Add(textBlock);
        }

        return cell;
    }

    public void SetGridSize(int columnSpan, int rowSpan)
    {
        // Calendar intentionally supports only the useful medium and large
        // presentations. Normalize legacy 1x1 state instead of reviving the
        // removed small layout.
        columnSpan = Math.Max(3, columnSpan);
        rowSpan = Math.Max(2, rowSpan);

        var container = this.FindControl<DaisyOS.Shell.Controls.WidgetContainer>("Container");
        if (container != null)
        {
            container.ColumnSpan = columnSpan;
            container.RowSpan = rowSpan;
        }

        var dayOfWeek = this.FindControl<TextBlock>("DayOfWeekText");
        var leftColumn = this.FindControl<Grid>("LeftColumnContent");
        var dayNumber = this.FindControl<TextBlock>("DayNumberText");
        var eventsSummary = this.FindControl<TextBlock>("EventsSummaryText");
        var monthHeader = this.FindControl<TextBlock>("MonthHeaderText");
        var monthContent = this.FindControl<Grid>("MonthCalendarContent");
        var layoutGrid = this.FindControl<Grid>("CalendarLayoutGrid");

        if (columnSpan <= 3)
        {
            // Medium (3x2) size: Left part hidden, Right part (Month calendar) full width
            if (layoutGrid != null)
            {
                layoutGrid.ColumnDefinitions = new ColumnDefinitions("0, *");
                layoutGrid.Margin = new Thickness(2, 0);
            }
            if (container != null) container.Padding = new Thickness(12, 10, 12, 10);

            if (dayOfWeek != null) dayOfWeek.IsVisible = false;
            if (leftColumn != null) leftColumn.IsVisible = false;
            if (eventsSummary != null) eventsSummary.IsVisible = false;

            if (monthHeader != null)
            {
                monthHeader.IsVisible = true;
                monthHeader.Margin = new Thickness(0, 0, 0, 2);
                monthHeader.HorizontalAlignment = HorizontalAlignment.Left;
            }

            if (monthContent != null)
            {
                monthContent.IsVisible = true;
                monthContent.Margin = new Thickness(0, 2, 0, 0);
            }
        }
        else
        {
            // Large (4x2) size: 2-column layout (Day + Events on left, Month view on right)
            if (layoutGrid != null)
            {
                layoutGrid.ColumnDefinitions = new ColumnDefinitions("105, *");
                layoutGrid.Margin = new Thickness(2, 0);
            }
            if (container != null) container.Padding = new Thickness(14, 10, 12, 10);

            if (dayOfWeek != null)
            {
                dayOfWeek.IsVisible = true;
                dayOfWeek.HorizontalAlignment = HorizontalAlignment.Left;
                dayOfWeek.TextAlignment = TextAlignment.Left;
                dayOfWeek.FontSize = 11;
                dayOfWeek.LetterSpacing = 0.8;
                dayOfWeek.Margin = new Thickness(0);
                dayOfWeek.VerticalAlignment = VerticalAlignment.Top;
            }

            if (leftColumn != null)
            {
                leftColumn.IsVisible = true;
                leftColumn.Margin = new Thickness(0, 0, 8, 0);
                leftColumn.HorizontalAlignment = HorizontalAlignment.Stretch;
                leftColumn.VerticalAlignment = VerticalAlignment.Stretch;
            }

            if (dayNumber != null)
            {
                dayNumber.FontSize = 36;
                dayNumber.FontWeight = FontWeight.Normal;
                dayNumber.HorizontalAlignment = HorizontalAlignment.Left;
                dayNumber.TextAlignment = TextAlignment.Left;
                dayNumber.Margin = new Thickness(0, -4, 0, 0);
                dayNumber.VerticalAlignment = VerticalAlignment.Top;
            }

            if (eventsSummary != null) eventsSummary.IsVisible = true;

            if (monthHeader != null)
            {
                monthHeader.IsVisible = true;
                monthHeader.Margin = new Thickness(4, 0, 0, 0);
                monthHeader.HorizontalAlignment = HorizontalAlignment.Left;
            }

            if (monthContent != null)
            {
                monthContent.IsVisible = true;
                monthContent.Margin = new Thickness(4, 3, 0, 0);
            }
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }
}
