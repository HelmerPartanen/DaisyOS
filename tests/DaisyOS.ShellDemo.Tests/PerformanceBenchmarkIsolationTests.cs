using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class PerformanceBenchmarkIsolationTests
{
    [AvaloniaFact]
    public void ReadinessRejectsAnEmptySettingsPage()
    {
        var window = new DaisyOS.Shell.Apps.System.Settings.SettingsWindow();
        Assert.False(DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.ContentReady(window, "settings"));
        window.Show();
        try
        {
            window.SelectCategory("Notifications");
            Dispatcher.UIThread.RunJobs();
            Assert.True(DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.ContentReady(window, "settings"));
            window.FindControl<DaisyOS.Shell.Apps.System.Settings.SettingsView>("SettingsContent")!.FindControl<StackPanel>("CategoryContent")!.Children.Clear();
            Assert.False(DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.ContentReady(window, "settings"));
            Assert.False(DaisyOS.Shell.Services.Diagnostics.ApplicationPerformanceProbe.Enabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task InjectedCatalogSurvivesWindowOpeningAndLanguageRefresh()
    {
        var entries = Enumerable.Range(0, 2000).Select(i => new AppEntry($"fixture-{i}", $"Application {i:D4}", "Fixture", "", false)).ToArray();
        var directory = Directory.CreateTempSubdirectory("daisy-performance-test-").FullName;
        var viewModel = new LauncherViewModel(new ShellSessionState(Path.Combine(directory, "state.json")), entries);
        var view = new LauncherView(viewModel);
        var window = new Window { Width = 720, Height = 600, Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            await viewModel.ReloadApplicationsAsync();
            Loc.Apply(Application.Current!.Resources, "fi-FI");
            Dispatcher.UIThread.RunJobs();
            await viewModel.ReloadApplicationsAsync();
            Assert.Equal(2000, viewModel.AllApps.Count);
            Assert.All(viewModel.AllApps, item => Assert.StartsWith("fixture-", item.App.Id));
            viewModel.SearchText = "Application 012";
            Dispatcher.UIThread.RunJobs();
            // Token matching also includes Application 0012 and Application 1012.
            Assert.Equal(12, viewModel.FilteredApps.Count);
        }
        finally { window.Close(); Loc.Apply(Application.Current!.Resources, "en-US"); Directory.Delete(directory, true); }
    }
}
