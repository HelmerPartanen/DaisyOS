using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Desktop;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.ViewModels;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopRenameLayoutTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NewlyAddedFilesUseContentIconsAndShowTheirNames(bool dark)
    {
        var directory = Path.Combine(Path.GetTempPath(), "desktop-new-items-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var vm = new DesktopViewModel(new DaisyOS.Shell.Services.Desktop.DesktopLayoutStore(Path.Combine(directory, "layout.json")));
        vm.ClearItems();
        var row = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 100 };
        var window = new Window { Width = 520, Height = 200, Content = row, Background = Brushes.DarkSlateBlue,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        try
        {
            foreach (var name in new[] { "New Document.txt", "New Folder" })
            {
                var path = Path.Combine(directory, name);
                if (name.EndsWith(".txt")) File.WriteAllText(path, "");
                else Directory.CreateDirectory(path);
                vm.AddDesktopPath(path);
                var item = vm.Items.Single(i => i.State.Id == path);
                Assert.DoesNotContain("AppIcons/Files", item.IconPath);
                Assert.True(item.IconBitmap is not null, $"Icon could not be decoded: {item.IconPath}");
                item.IsRenaming = true;
                item.IsSelected = true;
                row.Children.Add(new DesktopItemView { DataContext = item, Width = 74, Height = 88,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(50, 20, 0, 0) });
            }
            Assert.NotEqual(vm.Items[0].IconPath, vm.Items[1].IconPath);
            window.Show();
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            Directory.CreateDirectory("/tmp/desktop-new-items");
            using (var preview = window.CaptureRenderedFrame()) preview!.Save($"/tmp/desktop-new-items/{dark}.png");
            foreach (var view in row.Children.Cast<DesktopItemView>())
            {
                var editor = view.FindControl<TextBox>("RenameBox")!;
                var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single();
                Assert.Single(presenter.TextLayout.TextLines);
                var icon = view.FindControl<Border>("IconWrapper")!;
                Assert.True(editor.TranslatePoint(default, view)!.Value.Y >= icon.TranslatePoint(default, view)!.Value.Y + icon.Bounds.Height, $"editor={editor.TranslatePoint(default, view)} {editor.Bounds}; icon={icon.TranslatePoint(default, view)} {icon.Bounds}");
                Assert.True(presenter.TextLayout.Height <= editor.Bounds.Height);
            }
            Assert.True(row.Children.Cast<DesktopItemView>().Last().FindControl<TextBox>("RenameBox")!.IsFocused);
            Directory.CreateDirectory("/tmp/desktop-new-items");
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/desktop-new-items/{dark}.png");
        }
        finally { window.Close(); Directory.Delete(directory, true); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DesktopRenameShowsWholeNameWithoutScrollingOrBackground(bool dark, bool longName)
    {
        var name = longName ? string.Join(" ", Enumerable.Repeat("Long desktop filename", 10)) : "Visual Studio Code";
        var vm = new DesktopItemViewModel(new DesktopItemState("/tmp/rename-layout-fixture", "primary", 0, 0, 0)) { Label = name, IsSelected = true };
        var view = new DesktopItemView { DataContext = vm, Width = 74, Height = 88 };
        var canvas = new Canvas(); canvas.Children.Add(view);
        Canvas.SetLeft(view, 6); Canvas.SetTop(view, longName ? 160 : 6);
        var window = new Window { Width = 360, Height = 300, Content = canvas, Background = Brushes.DarkSlateBlue, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            vm.IsRenaming = true; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextBox>("RenameBox")!;
            editor.Focus(); editor.SelectAll();
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var scroll = editor.GetVisualDescendants().OfType<ScrollViewer>().Single();
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.HorizontalScrollBarVisibility);
            Assert.Equal(ScrollBarVisibility.Disabled, scroll.VerticalScrollBarVisibility);
            Assert.True(scroll.Extent.Height <= scroll.Viewport.Height + 1, $"extent={scroll.Extent}, viewport={scroll.Viewport}");
            Assert.Equal(name, editor.Text);
            var border = editor.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "PART_BorderElement");
            Assert.Equal(0, ((ISolidColorBrush)border.Background!).Color.A);
            var position = editor.TranslatePoint(default, window)!.Value;
            Assert.InRange(position.X, 0, window.Bounds.Width - editor.Bounds.Width);
            Assert.InRange(position.Y, 0, window.Bounds.Height - editor.Bounds.Height);
            Assert.Equal(view.Bounds.Width, editor.Bounds.Width);
            Assert.InRange(editor.Bounds.Height, 24, 48);
            if (!longName)
            {
                var presenter = editor.GetVisualDescendants().OfType<TextPresenter>().Single();
                Assert.Equal(1, presenter.TextLayout.TextLines.Count);
            }
            Assert.Equal(new Size(74, 88), view.Bounds.Size);
            Assert.Equal(50, view.ZIndex);
            Assert.DoesNotContain(editor.GetVisualAncestors().TakeWhile(ancestor => ancestor != window), ancestor => ancestor.ClipToBounds);
            Directory.CreateDirectory("/tmp/desktop-rename-layout");
            using var frame = window.CaptureRenderedFrame(); frame!.Save($"/tmp/desktop-rename-layout/{dark}-{longName}.png");
        }
        finally { vm.IsRenaming = false; window.Close(); }
    }
}
