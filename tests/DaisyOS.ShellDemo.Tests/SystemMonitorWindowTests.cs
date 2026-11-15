using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Theming;
using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Services.Windows;
using Avalonia.Styling;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.System.Diagnostics;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SystemMonitorWindowTests
{
    [AvaloniaFact]
    public void LiveCardsHaveStableSurfacesWithoutSemanticBrushTransitions()
    {
        var window = new TaskManagerWindow();
        try
        {
            window.Show(); window.UpdateLayout();
            var cards = window.GetVisualDescendants().OfType<MonitorMetricCard>().ToArray();
            Assert.Equal(4, cards.Length);
            Assert.All(cards, card =>
            {
                Assert.False(ThemeTransitionPolicy.GetEnabled(card));
                Assert.All(card.GetVisualDescendants().OfType<UsageHistory>(), chart =>
                {
                    Assert.False(ThemeTransitionPolicy.GetEnabled(chart));
                    Assert.True(chart.Transitions is null || chart.Transitions.Count == 0);
                });
            });
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(1100, false, 1)]
    [InlineData(1360, true, 1)]
    [InlineData(1100, false, 40)]
    [InlineData(1360, true, 40)]
    public void ProcessHeaderCellsMatchRowsAndHaveConsistentWidths(int width, bool advanced, int count)
    {
        var window = new TaskManagerWindow { Width = width };
        try
        {
            window.Show();
            var model = Assert.IsType<TaskManagerViewModel>(window.DataContext);
            model.SetActive(false); model.Navigate(2); model.AdvancedColumns = advanced;
            model.FilteredProcesses.Clear();
            for (var i = 0; i < count; i++) model.FilteredProcesses.Add(new ProcessRowViewModel(new(12345 + i, "An application", "/app", 2, 1024, "test")));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var header = window.GetVisualDescendants().OfType<ProcessTableGrid>().Single(g => g.Name == "ProcessHeader");
            var row = window.GetVisualDescendants().OfType<ProcessTableGrid>().First(g => g.Classes.Contains("ProcessDataRow"));
            Assert.Single(row.GetVisualDescendants().OfType<MonitorAppIcon>());
            Assert.Equal(header.Bounds.Width, row.Bounds.Width, 1);
            for (var i = 1; i < (advanced ? 10 : 6); i++)
            {
                Assert.Equal(header.ColumnDefinitions[i].ActualWidth, row.ColumnDefinitions[i].ActualWidth, 1);
                var heading = header.Children.Single(child => Grid.GetColumn(child) == i);
                var value = row.Children.Single(child => Grid.GetColumn(child) == i);
                Assert.Equal(heading.TranslatePoint(default, window)!.Value.X, value.TranslatePoint(default, window)!.Value.X, 1);
                Assert.Equal(header.Children.Single(child => Grid.GetColumn(child) == 1).Bounds.Width, heading.Bounds.Width, 1);
                Assert.Equal(heading.Bounds.Width, value.Bounds.Width, 1);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(WindowState.Normal)]
    [InlineData(WindowState.Maximized)]
    public void TaskbarRestoresMonitorAndPreservesPreviousWindowState(WindowState restoreState)
    {
        var tracker = new NativeAppWindowTracker();
        var window = new TaskManagerWindow();
        try
        {
            window.Show();
            tracker.Register("taskmanager", window);
            window.WindowState = restoreState;
            window.WindowState = WindowState.Minimized;
            Assert.True(tracker.ToggleFromTaskbar("taskmanager"));
            Assert.Equal(restoreState, window.WindowState);
            Assert.Equal(NativeAppWindowState.Active, tracker.GetState("taskmanager"));
            Assert.True(tracker.ToggleFromTaskbar("taskmanager"));
            Assert.Equal(WindowState.Minimized, window.WindowState);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SystemMonitorCanBeConstructedShownAndClosed(bool dark)
    {
        var window = new TaskManagerWindow { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            window.Show();
            window.UpdateLayout();
            Assert.True(window.IsVisible);
            var model = Assert.IsType<TaskManagerViewModel>(window.DataContext);
            model.SetActive(false);
            foreach (var section in new[] { 0, 1, 2, 3 })
            {
                model.Navigate(section);
                model.FilteredProcesses.Clear();
                model.FilteredProcesses.Add(new ProcessRowViewModel(new ProcessItemInfo(12345, "Test application", "/test/application", 0, 1024, "test")));
                window.UpdateLayout();
                Assert.True(window.IsVisible);
            }
        }
        finally { window.Close(); }
    }
}
