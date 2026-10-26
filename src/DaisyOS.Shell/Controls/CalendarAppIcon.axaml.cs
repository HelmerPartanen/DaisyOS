using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DaisyOS.Shell.Controls;

public partial class CalendarAppIcon : UserControl
{
    private readonly DispatcherTimer _dateTimer = new() { Interval = TimeSpan.FromMinutes(1) };
    private int _displayedDay;

    public CalendarAppIcon()
    {
        InitializeComponent();
        UpdateDate();
        _dateTimer.Tick += OnDateTimerTick;
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        UpdateDate();
        _dateTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _dateTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnDateTimerTick(object? sender, EventArgs e) => UpdateDate();

    private void UpdateDate()
    {
        var day = DateTime.Now.Day;
        if (day == _displayedDay) return;
        _displayedDay = day;
        if (this.FindControl<TextBlock>("DayText") is { } text)
            text.Text = day.ToString(CultureInfo.InvariantCulture);
    }
}
