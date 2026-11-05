using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views;
using DaisyOS.Shell.Views.Components.Widgets;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WidgetInteractionRegressionTests
{
    [AvaloniaFact]
    public void RemoveButtonUsesSubtleStyleAndRemainsClickable()
    {
        var item = new DaisyOS.Shell.ViewModels.DesktopItemViewModel(
            new DaisyOS.Core.Desktop.DesktopItemState("desktop.widget.test", "primary", 0, 0, 0))
        {
            IsWidgetCard = true,
            WidgetContent = new Border { Background = Avalonia.Media.Brushes.DarkSlateGray }
        };
        var view = new DesktopItemView { DataContext = item, Width = 222, Height = 176 };
        view.Resources["MotionDurationBase"] = TimeSpan.Zero;
        var window = new Window { Width = 300, Height = 240, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            var button = view.FindControl<Button>("RemoveWidgetButton")!;
            window.MouseMove(view.TranslatePoint(new Point(40, 40), window)!.Value);
            Assert.True(button.IsHitTestVisible);
            Assert.Contains("Subtle", button.Classes);
            Assert.DoesNotContain(button.GetVisualDescendants().OfType<Border>(), b => b.Name == "WidgetButtonSurface");
            var point = button.TranslatePoint(new Point(14, 14), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.UpdateLayout();
            Assert.Equal(28, button.Bounds.Width);
            Assert.Equal(28, button.Bounds.Height);
            window.MouseUp(point, MouseButton.Left);
            Assert.False(view.ClipToBounds);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ShelfMoveWorksAfterNativeReparentAndRepeatedRoundTrips()
    {
        var path = Path.Combine(Path.GetTempPath(), $"daisy-widget-move-{Guid.NewGuid():N}.json");
        var previous = Environment.GetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH");
        Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", path);
        Window? desktopWindow = null;
        Window? shelfWindow = null;
        try
        {
            var shell = new ShellView();
            shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
            shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
            var desktop = shell.FindControl<DaisyOS.Shell.Views.Components.Desktop.DesktopView>("Desktop")!;
            desktop.ViewModel.ClearItems();
            var shelf = shell.MediaWidget;
            ((Panel)shelf.Parent!).Children.Remove(shelf);
            desktopWindow = new Window { Width = 1280, Height = 900, Content = shell };
            shelfWindow = new Window { Width = 1280, Height = 96, Content = shelf };
            desktopWindow.Show();
            shelfWindow.Show();
            desktopWindow.UpdateLayout();
            var move = shelf.FindControl<StackPanel>("MediaContextMenuItems")!.Children.OfType<MenuItem>().First();
            for (var attempt = 0; attempt < 3; attempt++)
            {
                move.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                desktopWindow.UpdateLayout();
                var item = Assert.Single(desktop.ViewModel.Items, item => item.IsMediaWidget);
                Assert.False(desktop.ViewModel.BottomMediaWidgetVisible);
                Assert.True(desktop.CurrentMetrics!.IsValid(desktop.ViewModel.GetCommittedCell(item.Id)));
                var card = Assert.Single(desktop.GetVisualDescendants().OfType<DesktopItemView>(), view => ReferenceEquals(view.DataContext, item));
                Assert.True(card.IsEffectivelyVisible);
                Assert.True(card.Bounds.Width > 0 && card.Bounds.Height > 0);
                Assert.Equal(1, card.Opacity);
                desktop.ViewModel.MoveMediaWidgetToBottomShelf(item);
                Assert.True(shelf.IsVisible);
                Assert.Equal(1, shelf.Opacity);
            }
        }
        finally
        {
            shelfWindow?.Close();
            desktopWindow?.Close();
            Environment.SetEnvironmentVariable("DAISYOS_DESKTOP_LAYOUT_PATH", previous);
            File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void GalleryPageDotsNavigateInBothDirections()
    {
        var library = new WidgetLibraryOverlay();
        library.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = library };
        window.Show();
        try
        {
            library.Open();
            window.UpdateLayout();
            var dots = library.FindControl<StackPanel>("PageDots")!;
            void ClickDot(Border dot)
            {
                var point = dot.TranslatePoint(new Point(dot.Bounds.Width / 2, dot.Bounds.Height / 2), window)!.Value;
                window.MouseMove(point);
                window.MouseDown(point, MouseButton.Left);
                window.MouseUp(point, MouseButton.Left);
                window.UpdateLayout();
            }
            for (var i = 0; i < 4; i++)
            {
                ClickDot((Border)dots.Children[1]);
                Assert.Equal(1, library.SelectedIndex);
                ClickDot((Border)dots.Children[0]);
                Assert.Equal(0, library.SelectedIndex);
            }
        }
        finally { window.Close(); }
    }

}
