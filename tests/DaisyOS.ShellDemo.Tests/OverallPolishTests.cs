using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views.Components.SystemBar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class OverallPolishTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TodayHasExactHighContrastColorsInBothThemes(bool dark)
    {
        var calendar = new CustomCalendarView();
        var window = new Window { Content = calendar, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var day = calendar.FindControl<Avalonia.Controls.Primitives.UniformGrid>("DaysGrid")!.Children
                .OfType<Border>().Single(border => border.Child is TextBlock { FontWeight: var weight } && weight == FontWeight.Bold);
            Assert.Equal(dark ? Colors.White : Colors.Black, Assert.IsAssignableFrom<ISolidColorBrush>(day.Background).Color);
            Assert.Equal(dark ? Colors.Black : Colors.White, Assert.IsAssignableFrom<ISolidColorBrush>(((TextBlock)day.Child!).Foreground).Color);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PickerHandlesAnyFileAndFilteredResultsPackWithoutEmptySlots(bool dark)
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-picker-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "Subfolder"));
        foreach (var name in new[] { "a.txt", "b.bin", "c.unknown", "d.TXT" }) File.WriteAllText(Path.Combine(folder, name), "sample");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose files", true, false, [], InitialFolder: folder))
            { Width = 760, Height = 540, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        picker.Show();
        try
        {
            var entries = picker.FindControl<ListBox>("EntriesGridView")!;
            if (!entries.IsVisible) entries = picker.FindControl<ListBox>("EntriesListView")!;
            for (var attempt = 0; attempt < 200 && entries.ItemCount != 5; attempt++) await Task.Delay(10);
            Assert.Equal(5, entries.ItemCount);
            var search = picker.FindControl<SearchBar>("SearchBox")!;
            search.Text = "b.bin";
            picker.UpdateLayout();
            Assert.Equal(1, entries.ItemCount);
            var button = entries.GetVisualDescendants().OfType<Button>().Single(b => b.DataContext is PickerEntry);
            var entry = Assert.IsType<PickerEntry>(button.DataContext);
            Assert.Equal("b.bin", entry.Name);
            Assert.False(entry.IsDirectory);
            Assert.False(entry.HasThumbnail);
            ((ListBox)entries).SelectedItem = entry;
            Assert.Equal(Path.Combine(folder, "b.bin"), Assert.Single(picker.SelectedPaths));
            var sidebar = picker.FindControl<Border>("NavigationSidebar")!;
            Assert.Equal(picker.ClientSize.Height - picker.FindControl<DaisyOS.Shell.Controls.AppWindowTitleBar>("PickerTitleBar")!.Bounds.Height, sidebar.Bounds.Height);
            var locations = picker.FindControl<StackPanel>("LocationsPanel")!;
            Assert.Equal(4, locations.Spacing);
            foreach (var location in locations.Children.OfType<Button>()) Assert.Equal(locations.Bounds.Width, location.Bounds.Width);
            using var frame = picker.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-picker-polish-{dark}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { picker.Close(); Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public async Task PickerNormalizesRequestedExtensionsAndKeepsFoldersNavigable()
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-filter-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        Directory.CreateDirectory(Path.Combine(folder, "Subfolder"));
        File.WriteAllText(Path.Combine(folder, "included.TXT"), "sample");
        File.WriteAllText(Path.Combine(folder, "excluded.bin"), "sample");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose text", false, false, ["*.txt"], InitialFolder: folder));
        picker.Show();
        try
        {
            var entries = picker.FindControl<ListBox>("EntriesGridView")!;
            if (!entries.IsVisible) entries = picker.FindControl<ListBox>("EntriesListView")!;
            for (var attempt = 0; attempt < 200 && entries.ItemCount != 2; attempt++) await Task.Delay(10);
            var items = entries.ItemsSource!.Cast<PickerEntry>().ToArray();
            Assert.Equal(2, items.Length);
            Assert.Contains(items, item => item.IsDirectory);
            Assert.Contains(items, item => item.Name == "included.TXT");
            Assert.DoesNotContain(items, item => item.Name == "excluded.bin");
        }
        finally { picker.Close(); Directory.Delete(folder, true); }
    }

    [AvaloniaFact]
    public async Task SingleFilePickerAcceptsAFileOnDoubleClick()
    {
        var folder = Path.Combine(Path.GetTempPath(), "daisy-double-pick-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var file = Path.Combine(folder, "report.txt");
        File.WriteAllText(file, "sample");
        var picker = new DaisyFilePickerWindow(new DaisyFilePickerOptions("Choose", false, false, [".txt"], InitialFolder: folder));
        picker.Show();
        try
        {
            var entries = picker.FindControl<ListBox>("EntriesGridView")!;
            if (!entries.IsVisible) entries = picker.FindControl<ListBox>("EntriesListView")!;
            for (var attempt = 0; attempt < 200 && entries.ItemCount != 1; attempt++) await Task.Delay(10);
            picker.UpdateLayout();
            var button = entries.GetVisualDescendants().OfType<Button>().Single(control => control.DataContext is PickerEntry);
            button.RaiseEvent(new RoutedEventArgs(InputElement.DoubleTappedEvent));
            Assert.True(picker.WasConfirmed);
            Assert.Equal(file, Assert.Single(picker.SelectedPaths));
        }
        finally
        {
            if (picker.IsVisible) picker.Close();
            Directory.Delete(folder, true);
        }
    }
}
