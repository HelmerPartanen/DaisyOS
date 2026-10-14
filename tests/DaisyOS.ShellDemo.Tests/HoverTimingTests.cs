using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Files;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class HoverTimingTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FilesPointerMovementClearsPreviousRowPromptlyWithoutLosingSelection(bool dark)
    {
        var directory = Path.Combine(Path.GetTempPath(), "daisy-hover-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "First", "Second", "Selected" }) Directory.CreateDirectory(Path.Combine(directory, name));
        var window = new FilesWindow(directory, false) { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            var list = window.FindControl<ListBox>("EntriesList")!;
            window.FindControl<ListBox>("EntriesGrid")!.IsVisible = false;
            list.IsVisible = true;
            list.ItemTemplate = new FuncDataTemplate<FileEntryViewModel>((item, _) => new TextBlock { Text = item?.Name, Height = 40 });
            window.Show();
            for (var attempt = 0; attempt < 250 && list.ItemCount != 3; attempt++)
            {
                await Task.Delay(20);
                Dispatcher.UIThread.RunJobs();
            }
            Assert.Equal(3, list.ItemCount);
            window.UpdateLayout();
            list.SelectedIndex = 2;
            var rows = list.GetVisualDescendants().OfType<ListBoxItem>().ToArray();
            Assert.Equal(3, rows.Length);
            var presenters = rows.Select(row => row.GetVisualDescendants().OfType<ContentPresenter>().Single(p => p.Name == "PART_ContentPresenter")).ToArray();
            foreach (var index in new[] { 0, 1 })
            {
                var point = rows[index].TranslatePoint(new Point(rows[index].Bounds.Width / 2, rows[index].Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(90);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            }
            Assert.Equal(0, ((ISolidColorBrush)presenters[0].Background!).Color.A);
            Assert.True(rows[1].TryFindResource("StateHoverBrush", rows[1].ActualThemeVariant, out var hover));
            Assert.Equal(((ISolidColorBrush)hover!).Color, ((ISolidColorBrush)presenters[1].Background!).Color);
            Assert.True(rows[2].IsSelected);
            Assert.True(rows[2].TryFindResource("StateSelectedBrush", rows[2].ActualThemeVariant, out var selected));
            Assert.Equal(((ISolidColorBrush)selected!).Color, ((ISolidColorBrush)presenters[2].Background!).Color);
            Assert.All(rows, row => Assert.Equal(0, ((ISolidColorBrush)row.Background!).Color.A));
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }
}
