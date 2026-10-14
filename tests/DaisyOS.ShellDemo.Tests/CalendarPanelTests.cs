using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.VisualTree;
using System.Linq;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class CalendarPanelTests
{
    [AvaloniaFact]
    public void MonthPickerChangesYearAndReturnsToDaysWithoutResizing()
    {
        var calendar = new CustomCalendarView();
        var window = new Window { Width = 340, Content = new Border { Padding = new Avalonia.Thickness(16), Child = calendar, Background = Avalonia.Media.Brushes.Black }, RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark, SizeToContent = SizeToContent.Height };
        window.Show();
        try
        {
            window.UpdateLayout();
            var height = calendar.Bounds.Height;
            calendar.FindControl<Button>("ViewToggle")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            var months = calendar.FindControl<UniformGrid>("MonthsGrid")!;
            Assert.True(months.IsVisible);
            Assert.Equal(2, months.Columns);
            Assert.Equal(12, months.Children.Count);
            window.UpdateLayout();
            foreach (var month in months.Children.Cast<Button>())
            {
                var mini = month.GetVisualDescendants().OfType<UniformGrid>().Single();
                Assert.Equal(42, mini.Children.Count);
                Assert.True(mini.Bounds.Width / 7 >= 17, $"Mini calendar width: {mini.Bounds.Width}");
                Assert.All(mini.GetVisualDescendants().OfType<TextBlock>(), label => Assert.True(label.FontSize >= 11));
            }
            using (var frame = window.CaptureRenderedFrame()) frame!.Save("/tmp/daisy-calendar-year.png");
            calendar.FindControl<Button>("NextButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal((DateTime.Today.Year + 1).ToString(), calendar.FindControl<TextBlock>("YearText")!.Text);
            Assert.IsType<Button>(months.Children[0]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(months.IsVisible);
            Assert.True(calendar.FindControl<Grid>("DaysView")!.IsVisible);
            Assert.Equal(42, calendar.FindControl<UniformGrid>("DaysGrid")!.Children.Count);
            window.UpdateLayout();
            Assert.Equal(height, calendar.Bounds.Height);
            var header = calendar.FindControl<Button>("ViewToggle")!;
            foreach (var name in new[] { "ViewToggle", "PreviousButton", "NextButton", "TodayButton" })
            {
                var button = calendar.FindControl<Button>(name)!;
                Assert.Contains("Ghost", button.Classes);
                Assert.DoesNotContain("Subtle", button.Classes);
                Assert.Contains("Small", button.Classes);
                Assert.Equal(32, button.Bounds.Height);
            }
            Assert.Equal(32, calendar.FindControl<Button>("NextButton")!.Bounds.Width);
            Assert.True(header.Padding.Left >= 8);
            calendar.FindControl<Button>("TodayButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            window.UpdateLayout();
            Assert.Equal(DateTime.Today.ToString("MMMM"), calendar.FindControl<TextBlock>("MonthText")!.Text);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save("/tmp/daisy-calendar-days.png");
        }
        finally { window.Close(); }
    }
}
