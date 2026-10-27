using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Disks;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class DiskManagerTests
{
    [AvaloniaTheory]
    [InlineData(false, 1080)]
    [InlineData(true, 1080)]
    [InlineData(false, 820)]
    [InlineData(true, 820)]
    public async Task OpaqueNativeLayoutAndDeviceSelectionWorkInBothThemes(bool dark, int width)
    {
        var window = new DiskManagerWindow { Width = width, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var list = window.GetVisualDescendants().OfType<ListBox>().Single();
            for (var i = 0; i < 100 && list.ItemCount == 0; i++) { await Task.Delay(20); Dispatcher.UIThread.RunJobs(); }
            Assert.True(list.ItemCount > 0);
            Assert.IsType<DiskItem>(list.SelectedItem);
            var physical = list.Items.OfType<DiskItem>().First(x => x.Type == "disk" && !x.ReadOnly);
            list.SelectedItem = physical;
            Assert.True(window.FindControl<WrapPanel>("ActionToolbar")!.Children.OfType<Button>().Single(x => x.Name == "ActionErase").IsEnabled);
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.Single(window.GetVisualDescendants().OfType<AppWindowTitleBar>());
            Assert.Equal(255, Assert.IsAssignableFrom<ISolidColorBrush>(window.Background).Color.A);
            var toolbar = window.FindControl<WrapPanel>("ActionToolbar")!;
            Assert.Equal(7, toolbar.Children.Count);
            foreach (var button in toolbar.Children.OfType<Button>())
            {
                Assert.True(button.Bounds.Width > 0);
                Assert.Contains("Small", button.Classes);
                var contents = Assert.IsType<StackPanel>(button.Content);
                Assert.IsType<DaisyIcon>(contents.Children[0]);
                Assert.IsType<TextBlock>(contents.Children[1]);
            }
            Assert.Contains("Danger", toolbar.Children.OfType<Button>().Single(x => x.Name == "ActionErase").Classes);
            Assert.Contains("Primary", toolbar.Children.OfType<Button>().Single(x => x.Name == "ActionMount").Classes);
            Assert.Contains("Secondary", toolbar.Children.OfType<Button>().Single(x => x.Name == "ActionRefresh").Classes);
            Assert.True(list.Bounds.Height > 200);
            var details = window.FindControl<StackPanel>("DeviceDetails")!;
            Assert.True(details.Children.OfType<Border>().Count() + details.Children.OfType<StackPanel>().Count(x => x.Children.OfType<Border>().Any()) >= 2);
            Assert.Single(details.GetVisualDescendants().OfType<StorageUsageView>());
            Directory.CreateDirectory("/tmp/disk-manager-style");
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame()) frame!.Save($"/tmp/disk-manager-style/storage-disk-{(dark ? "dark" : "light")}-{width}.png");
            var volumeButton = details.GetVisualDescendants().OfType<Button>().FirstOrDefault(x => x.Tag is string);
            if (volumeButton is not null)
            {
                var scroll = window.FindControl<ScrollViewer>("DetailsScroll")!;
                scroll.Offset = new Vector(0, 1000); window.UpdateLayout();
                volumeButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(volumeButton.Tag, ((DiskItem)list.SelectedItem!).Path);
                Assert.Equal(0, scroll.Offset.Y);
                list.SelectedIndex = 0; window.UpdateLayout();
            }
            var refresh = toolbar.Children.OfType<Button>().Single(x => x.Name == "ActionRefresh");
            Assert.Equal("arrows-clockwise", ((DaisyIcon)((StackPanel)refresh.Content!).Children[0]).Icon);
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var navigation = list.GetVisualDescendants().OfType<Button>().Where(x => x.Classes.Contains("SettingsNavItem")).ToArray();
            Assert.True(navigation.Length >= 2);
            var hoverButton = navigation[1];
            var presenter = hoverButton.GetVisualDescendants().OfType<ContentPresenter>().Single(x => x.Name == "PART_ContentPresenter");
            var transition = Assert.Single(presenter.Transitions!.OfType<BrushTransition>(), x => x.Property == ContentPresenter.BackgroundProperty);
            Assert.Equal(TimeSpan.FromMilliseconds(50), transition.Duration);
            var position = hoverButton.TranslatePoint(new Point(hoverButton.Bounds.Width / 2, hoverButton.Bounds.Height / 2), window)!.Value;
            window.MouseMove(position);
            await Task.Delay(90);
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(hoverButton.IsPointerOver);
            Assert.True(hoverButton.TryFindResource("StateHoverBrush", window.ActualThemeVariant, out var hover));
            Assert.Equal(((ISolidColorBrush)hover!).Color, ((ISolidColorBrush)presenter.Background!).Color);
            hoverButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(hoverButton.DataContext, list.SelectedItem);
            window.MouseMove(new Point(window.Width - 10, window.Height - 10));
            await Task.Delay(90);
            Dispatcher.UIThread.RunJobs();
            Directory.CreateDirectory("/tmp/disk-manager-style");
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using (var frame = window.CaptureRenderedFrame()) frame!.Save($"/tmp/disk-manager-style/{(dark ? "dark" : "light")}-{width}.png");
            var mounted = list.Items.OfType<DiskItem>().Where(x => x.Used.HasValue && x.Free.HasValue).OrderByDescending(x => x.Size).FirstOrDefault();
            if (mounted is not null)
            {
                list.SelectedItem = mounted; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
                var text = details.GetVisualDescendants().OfType<TextBlock>().Select(x => x.Text).ToArray();
                Assert.Contains("Used", text); Assert.Contains("Free", text);
                Assert.Contains(DiskStorageSummary.FormatName(mounted.Format), text);
                Assert.Contains(string.Join(", ", mounted.Mounts.Distinct().OrderBy(x => x.Length)), text);
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                using var frame = window.CaptureRenderedFrame(); frame!.Save($"/tmp/disk-manager-style/storage-mounted-{(dark ? "dark" : "light")}-{width}.png");
            }
            if (list.ItemCount > 1)
            {
                list.SelectedIndex = 1;
                Dispatcher.UIThread.RunJobs();
                var selected = Assert.IsType<DiskItem>(list.SelectedItem);
                Assert.Contains(details.GetVisualDescendants().OfType<TextBlock>(), x => x.Text == selected.Name);
            }
        }
        finally { window.Close(); }
    }
}
