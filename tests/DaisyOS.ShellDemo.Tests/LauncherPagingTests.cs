using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherPagingTests
{
    [AvaloniaFact]
    public void PagesCoverCatalogAndClampAfterResizeAndSearch()
    {
        var apps = Enumerable.Range(0, 47)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        model.SetPageLayout(8, 2);
        Assert.Equal(3, model.PageCount);
        var seen = new List<LauncherItemViewModel>();
        for (var page = 0; page < model.PageCount; page++)
        {
            model.GoToPage(page);
            Assert.InRange(model.PageApps.Count, 1, 16);
            seen.AddRange(model.PageApps);
        }
        Assert.Equal(model.FilteredApps.ToArray(), seen.ToArray());
        model.GoToPage(99);
        Assert.Equal(2, model.PageIndex);
        model.SetPageLayout(8, 4);
        Assert.Equal(1, model.PageIndex);
        model.SearchText = "App 00";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0, model.PageIndex);
        Assert.Single(model.PageApps);
        Assert.Equal("App 00", model.PageApps[0].Name);
        model.SearchText = "no matching app";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(model.PageApps);
        Assert.Equal(1, model.PageCount);
        Assert.True(model.ShowNoResults);
    }

    [AvaloniaTheory]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void PageGridFitsAvailableSpaceAndWheelNavigates(double deltaX, double deltaY)
    {
        var apps = Enumerable.Range(0, 47)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        var launcher = new LauncherView(model);
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            Assert.True(model.PageCount > 1);
            Assert.True(model.PageGridWidth <= viewport.Bounds.Width);
            Assert.True(model.PageApps.Count <= (int)(viewport.Bounds.Width / 136) * (int)(viewport.Bounds.Height / 128));
            Assert.Equal(model.PageCount, launcher.FindControl<StackPanel>("LauncherPageIndicators")!.Children.Count);
            var point = viewport.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseWheel(point, new Vector(deltaX * 0.4, deltaY * 0.4));
            Assert.Equal(0, model.PageIndex);
            window.MouseWheel(point, new Vector(deltaX * 0.7, deltaY * 0.7));
            Assert.Equal(1, model.PageIndex);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void PageTransitionsFollowDirectionAndCanBeInterruptedOrDisabled()
    {
        var apps = Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationEnter"] = TimeSpan.FromMilliseconds(240);
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var incoming = launcher.FindControl<ItemsControl>("ApplicationGrid")!;
            var outgoing = launcher.FindControl<ItemsControl>("OutgoingPageGrid")!;
            var dots = launcher.FindControl<StackPanel>("LauncherPageIndicators")!;
            var firstDot = dots.Children[0];
            var previousApps = model.PageApps.ToArray();
            model.GoToPage(1);
            Assert.True(outgoing.IsVisible);
            Assert.Equal(previousApps, outgoing.ItemsSource!.Cast<LauncherItemViewModel>().ToArray());
            Assert.Equal(launcher.FindControl<Grid>("LauncherPageViewport")!.Bounds.Width, incoming.RenderTransform!.Value.M31);
            Assert.Same(firstDot, dots.Children[0]);
            model.GoToPage(0);
            Assert.Equal(-launcher.FindControl<Grid>("LauncherPageViewport")!.Bounds.Width, incoming.RenderTransform!.Value.M31);
            launcher.Resources["MotionDurationEnter"] = TimeSpan.Zero;
            model.GoToPage(1);
            Assert.False(outgoing.IsVisible);
            Assert.Null(outgoing.ItemsSource);
            Assert.Null(incoming.RenderTransform);
            Assert.Equal(1, incoming.Opacity);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TrackpadPagesFollowEveryDeltaReverseAndSettleOnRelease()
    {
        var apps = Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            var current = launcher.FindControl<ItemsControl>("ApplicationGrid")!;
            var neighbour = launcher.FindControl<ItemsControl>("OutgoingPageGrid")!;
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            void Scroll(double x, bool end = false) => viewport.RaiseEvent(new PointerWheelEventArgs(
                viewport, pointer, window, new Point(100, 100), 0, new PointerPointProperties(),
                KeyModifiers.None, new Vector(x, 0)) { IsPrecise = true, IsScrollEnd = end });
            Scroll(-40);
            Assert.Equal(0, model.PageIndex);
            Assert.Equal(-40, current.RenderTransform!.Value.M31);
            Assert.Equal(viewport.Bounds.Width - 40, neighbour.RenderTransform!.Value.M31);
            Assert.Equal(1, current.Opacity);
            Scroll(-30);
            Assert.Equal(-70, current.RenderTransform!.Value.M31);
            Scroll(20);
            Assert.Equal(-50, current.RenderTransform!.Value.M31);
            Scroll(0, true); // Short swipe returns to the same page.
            Assert.Equal(0, model.PageIndex);
            Assert.Null(current.RenderTransform);
            Assert.False(neighbour.IsVisible);
            Scroll(-70);
            Scroll(0, true);
            Assert.Equal(1, model.PageIndex);
            Assert.Null(current.RenderTransform);
            Assert.True(current.IsHitTestVisible);
            Scroll(70);
            Scroll(0, true);
            Assert.Equal(0, model.PageIndex);
            Scroll(160); // Resist dragging past the first page.
            Assert.Equal(32, current.RenderTransform!.Value.M31);
            Assert.False(neighbour.IsVisible);
            Scroll(0, true);
            Assert.Equal(0, model.PageIndex);
        }
        finally { window.Close(); }
    }

}
