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
    public void RemoveButtonKeepsItsCircleOnHoverAndPress()
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
            ClickAndCheckShape(window, button);
            var surface = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WidgetButtonSurface");
            Assert.Equal(22, surface.Bounds.Width);
            Assert.Equal(22, surface.Bounds.Height);
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
    public void GalleryArrowsKeepCircularGeometryDuringPressAndReversal()
    {
        var library = new WidgetLibraryOverlay();
        library.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = library };
        window.Show();
        try
        {
            library.Open();
            window.UpdateLayout();
            var next = library.FindControl<Button>("NextVariantButton")!;
            var back = library.FindControl<Button>("PreviousVariantButton")!;
            for (var i = 0; i < 4; i++)
            {
                ClickAndCheckShape(window, next);
                Assert.Equal(1, library.SelectedIndex);
                ClickAndCheckShape(window, back);
                Assert.Equal(0, library.SelectedIndex);
            }
        }
        finally { window.Close(); }
    }

    private static void ClickAndCheckShape(Window window, Button button)
    {
        var surface = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "WidgetButtonSurface");
        var bounds = surface.Bounds;
        var corner = surface.CornerRadius;
        var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(point);
        window.MouseDown(point, MouseButton.Left);
        window.UpdateLayout();
        Assert.Equal(bounds, surface.Bounds);
        Assert.Equal(corner, surface.CornerRadius);
        Assert.True(button.RenderTransform is null || button.RenderTransform.Value.IsIdentity);
        window.MouseUp(point, MouseButton.Left);
        window.UpdateLayout();
    }
}
