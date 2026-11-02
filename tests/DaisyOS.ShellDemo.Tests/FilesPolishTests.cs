using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class FilesPolishTests
{
    [AvaloniaFact]
    public async Task RefreshRetriesAFailedFolderWithoutLosingItsSearch()
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-refresh-" + Guid.NewGuid());
        using var tab = new FilesTabViewModel(folder, navigateImmediately: false, watchFiles: false);
        try
        {
            await tab.NavigateToAsync(folder);
            Assert.True(tab.HasLoadError);
            tab.SearchQuery = "keep";
            Directory.CreateDirectory(folder);
            await File.WriteAllTextAsync(Path.Combine(folder, "keep.txt"), "text");
            await tab.RefreshAsync();
            await tab.ViewUpdateTask;
            Assert.False(tab.HasLoadError);
            Assert.False(tab.IsLoading);
            Assert.Equal("keep", tab.SearchQuery);
            Assert.Single(tab.FilteredEntries);
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
    }

    [AvaloniaTheory]
    [InlineData(false, 740, 100)]
    [InlineData(true, 1040, 100)]
    [InlineData(false, 740, 150)]
    [InlineData(true, 1040, 150)]
    public async Task SidebarAndListRowsMatchAndEmptyStatesHaveUsefulActions(bool dark, int width, int scale)
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-files-polish-" + Guid.NewGuid());
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "Projects"));
        await File.WriteAllTextAsync(Path.Combine(folder, "A project document with a deliberately long name for layout verification.txt"), "test");
        TypographyPolicy.Apply(Application.Current!.Resources, TypographyPolicy.DefaultFamilyName, scale);
        var window = new FilesWindow(folder, false) { Width = width, Height = 720,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var vm = (FilesViewModel)window.DataContext!;
            var tab = vm.ActiveTab!;
            await tab.NavigateToAsync(folder);
            tab.SetGridViewFromUser(false);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            using (window.CaptureRenderedFrame()) { }
            var list = window.FindControl<ListBox>("EntriesList")!;
            var row = list.GetRealizedContainers().OfType<ListBoxItem>().First();
            var rowContent = row.GetVisualDescendants().OfType<Border>().First(border => border.Classes.Contains("FileDropTarget"));
            var sidebar = window.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("SettingsNavItem") && button.IsEffectivelyVisible).ToArray();
            Assert.NotEmpty(sidebar);
            Assert.All(sidebar, button =>
            {
                Assert.Equal(rowContent.Bounds.Height, button.Bounds.Height);
                Assert.Equal(row.CornerRadius, button.CornerRadius);
            });
            Assert.True(tab.VisibleBreadcrumbs[^1].IsCurrent);
            Assert.Equal(folder, tab.VisibleBreadcrumbs[^1].Path);
            if (width == 740 && scale == 150) Assert.Single(tab.VisibleBreadcrumbs);
            var toolbar = window.FindControl<Border>("SearchTools")!;
            var toolbarPoint = toolbar.TranslatePoint(default, window)!.Value;
            Assert.True(toolbarPoint.X + toolbar.Bounds.Width <= window.ClientSize.Width + 1);
            Directory.CreateDirectory("/tmp/daisy-files-polish");
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-files-polish/list-{dark}-{width}-{scale}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            tab.SetGridViewFromUser(true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-files-polish/grid-{dark}-{width}-{scale}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            tab.SetGridViewFromUser(false);
            vm.TogglePreviewPane();
            window.UpdateLayout();
            Assert.True(window.FindControl<Grid>("FileContentGrid")!.ColumnDefinitions[0].ActualWidth > 100);
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-files-polish/preview-{dark}-{width}-{scale}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            vm.TogglePreviewPane();
            tab.SearchQuery = "no-such-file-294852";
            await tab.ViewUpdateTask;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Assert.Equal(Loc.Get("No matching files"), window.FindControl<TextBlock>("EmptyStateTitle")!.Text);
            Assert.Equal(Loc.Get("Clear search"), tab.EmptyActionLabel);
            Assert.False(tab.ShowSearchClearInStatusBar);
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-files-polish/search-{dark}-{width}-{scale}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            window.FindControl<Button>("EmptyStateAction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await tab.ViewUpdateTask;
            Assert.False(tab.HasSearchQuery);
            Assert.Equal(2, tab.FilteredEntries.Count);
            await tab.NavigateToAsync(Path.Combine(folder, "missing"));
            window.UpdateLayout();
            Assert.True(tab.HasLoadError);
            Assert.Equal(Loc.Get("Try again"), tab.EmptyActionLabel);
            Assert.NotEqual(Loc.Get("This folder is empty"), tab.EmptyTitle);
            Directory.CreateDirectory(Path.Combine(folder, "missing"));
            window.FindControl<Button>("EmptyStateAction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            for (var i = 0; i < 100 && tab.IsLoading; i++) await Task.Delay(10);
            Assert.False(tab.IsLoading);
            Assert.False(tab.HasLoadError);
            Assert.Equal(Loc.Get("This folder is empty"), tab.EmptyTitle);
            tab.ShowHiddenFiles = false;
            await File.WriteAllTextAsync(Path.Combine(folder, "missing", ".hidden-file"), "hidden");
            await tab.NavigateToAsync(Path.Combine(folder, "missing"), addHistory: false);
            Assert.True(tab.HasHiddenEntriesOnly);
            Assert.Equal(Loc.Get("Show hidden files"), tab.EmptyActionLabel);
            window.FindControl<Button>("EmptyStateAction")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await tab.ViewUpdateTask;
            Assert.Single(tab.FilteredEntries);
        }
        finally
        {
            window.Close();
            TypographyPolicy.Apply(Application.Current!.Resources, TypographyPolicy.DefaultFamilyName, 100);
            Directory.Delete(folder, true);
        }
    }
}
