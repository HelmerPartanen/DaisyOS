using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherVisualRegressionTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void LauncherUsesTheTaskbarMaterialWithXxlCorners(bool dark)
    {
        var launcher = new LauncherView(new LauncherViewModel(null, []));
        var taskbar = new TaskbarView();
        var host = new Grid();
        host.Children.Add(launcher);
        host.Children.Add(taskbar);
        var window = new Window { Width = 1280, Height = 800, Content = host,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout();
            var frame = launcher.FindControl<Border>("LauncherRootBorder")!;
            var taskbarFrame = taskbar.FindControl<Border>("TaskbarBody")!;
            Assert.IsType<ShellFrameBorder>(frame);
            Assert.Equal(taskbarFrame.Background, frame.Background);
            Assert.Equal(new CornerRadius(32), frame.CornerRadius);
            Assert.Equal(taskbarFrame.Padding, frame.Padding);
            var inner = launcher.FindControl<Border>("LauncherInnerFrame")!;
            var taskbarInner = taskbar.FindControl<Border>("TaskbarInnerFrame")!;
            Assert.IsType<ShellFrameBorder>(inner);
            Assert.Equal(taskbarInner.Background, inner.Background);
            Assert.Equal(taskbarInner.BorderBrush, inner.BorderBrush);
            Assert.Equal(taskbarInner.BackgroundSizing, inner.BackgroundSizing);
            Assert.Equal(taskbarInner.BorderThickness, inner.BorderThickness);
            Assert.Equal(new CornerRadius(31), inner.CornerRadius);
            var fill = launcher.FindControl<Border>("LauncherPanelClip")!;
            var taskbarFill = taskbar.FindControl<Border>("TaskbarFill")!;
            Assert.Equal(taskbarFill.Background, fill.Background);
            Assert.Equal(new CornerRadius(30), fill.CornerRadius);
            launcher.SetSolidBackdrop(true);
            Assert.True(fill.Classes.Contains("SolidBackdrop"));
            Assert.Equal(taskbarFrame.Background, frame.Background);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public void PagingRealizesCachedIconsBeforeTheFirstTransitionFrame()
    {
        using var stream = AssetLoader.Open(new Uri("avares://DaisyOS.Shell/Assets/AppIcons/Launcher.png"));
        using var icon = new Bitmap(stream);
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"Application {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        var window = new Window { Width = 1280, Height = 800, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            foreach (var item in model.AllApps) item.IconBitmap = icon;
            using (window.CaptureRenderedFrame()) { }
            foreach (var page in new[] { 1, 2, 1, 0, 2 })
            {
                model.GoToPage(page);
                // No test-supplied layout pass: the transition must prepare its
                // tile containers before asking the compositor to move them.
                foreach (var grid in new[] { launcher.CurrentPageGrid, launcher.NeighbourPageGrid })
                {
                    Assert.True(grid.IsVisible);
                    Assert.NotNull(grid.ContainerFromIndex(0));
                    var images = grid.GetVisualDescendants().OfType<Image>()
                        .Where(image => image.IsEffectivelyVisible).ToArray();
                    Assert.Equal(grid.ItemCount, images.Length);
                    Assert.All(images, image => Assert.Same(icon, image.Source));
                }
                using (window.CaptureRenderedFrame()) { }
            }
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public void SwipePreparesNeighbourIconsAndCommitsWithoutAnEmptyPage()
    {
        using var stream = AssetLoader.Open(new Uri("avares://DaisyOS.Shell/Assets/AppIcons/Launcher.png"));
        using var icon = new Bitmap(stream);
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"Application {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 800, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            foreach (var item in model.AllApps) item.IconBitmap = icon;
            using (window.CaptureRenderedFrame()) { }
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            ulong time = 1000;
            void Scroll(double pixels, bool end = false) => viewport.RaiseEvent(new PointerWheelEventArgs(
                viewport, pointer, window, new Point(100, 100), time += 16,
                new PointerPointProperties(), KeyModifiers.None, new Vector(pixels / 4, 0))
                { IsPrecise = true, IsScrollEnd = end });
            Scroll(-viewport.Bounds.Width * 0.75);
            var neighbour = launcher.NeighbourPageGrid;
            Assert.True(neighbour.IsVisible);
            Assert.NotNull(neighbour.ContainerFromIndex(0));
            Assert.Equal(neighbour.ItemCount, neighbour.GetVisualDescendants().OfType<Image>().Count(image => image.IsEffectivelyVisible));
            using (window.CaptureRenderedFrame()) { }
            Scroll(0, true);
            Assert.Equal(1, model.PageIndex);
            var current = launcher.CurrentPageGrid;
            Assert.NotNull(current.ContainerFromIndex(0));
            var images = current.GetVisualDescendants().OfType<Image>().Where(image => image.IsEffectivelyVisible).ToArray();
            Assert.Equal(model.PageApps.Count, images.Length);
            Assert.All(images, image => Assert.Same(icon, image.Source));
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public void TranslatedPageKeepsEveryTileRealized()
    {
        var grid = new LauncherItemsControl
        {
            Width = 272, Height = 640,
            ItemsSource = Enumerable.Range(0, 10).ToArray(),
            ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<int>((item, _) => new TextBlock { Text = item.ToString() }),
            ItemsPanel = new Avalonia.Controls.Templates.FuncTemplate<Panel>(() => new VirtualizingLauncherGrid())
        };
        var viewport = new Border { Width = 272, Height = 128, ClipToBounds = true, Child = grid };
        var window = new Window { Width = 400, Height = 200, Content = viewport };
        window.Show();
        try
        {
            window.UpdateLayout();
            grid.RenderTransform = new TranslateTransform(0, -384);
            using (window.CaptureRenderedFrame()) { }
            window.UpdateLayout();
            Assert.Equal(10, Assert.IsType<VirtualizingLauncherGrid>(grid.ItemsPanelRoot).RealizedCount);
            Assert.All(Enumerable.Range(0, 10), index => Assert.NotNull(grid.ContainerFromIndex(index)));
        }
        finally { window.Close(); }
    }
}
