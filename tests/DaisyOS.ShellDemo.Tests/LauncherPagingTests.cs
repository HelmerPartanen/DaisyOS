using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.ViewModels;
using DaisyOS.Shell.Views.Components.Launcher;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class LauncherPagingTests
{
    [AvaloniaFact]
    public void ClickingEmptyLauncherSpaceKeepsSearchFocusedThroughoutTheClick()
    {
        var launcher = new LauncherView(new LauncherViewModel(null, Array.Empty<AppEntry>()));
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var search = launcher.GetVisualDescendants().OfType<TextBox>()
                .Single(input => input.Name == "SearchInput");
            var content = launcher.FindControl<Grid>("LauncherInteractiveContent")!;
            var point = content.TranslatePoint(new Point(8, 8), window)!.Value;
            window.MouseMove(point);
            var hit = window.InputHitTest(point) as Visual;
            Assert.True(hit is not null, $"Empty-space point {point}; window {window.Bounds}; content {content.Bounds}");
            Assert.True(launcher.IsWithinInteractiveContent(hit!));
            search.Focus();
            var focusLosses = 0;
            search.LostFocus += (_, _) => focusLosses++;
            for (var i = 0; i < 3; i++)
            {
                window.MouseDown(point, MouseButton.Left);
                Assert.True(search.IsFocused);
                window.MouseUp(point, MouseButton.Left);
                Assert.True(search.IsFocused);
            }
            Assert.Equal(0, focusLosses);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public void ResolvingAQueuedSearchDoesNotReapplyItAfterPageNavigation()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 3000)
            .Select(i => new AppEntry($"app-{i}", $"App {i:0000}", "", "", false)).ToArray());
        model.SearchText = "App";
        Assert.NotNull(model.ResolveSearchResult());
        model.GoToPage(1);
        var page = model.PageApps.ToArray();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(1, model.PageIndex);
        Assert.Equal(page, model.PageApps.ToArray());
    }

    [AvaloniaFact]
    public void LiveCalendarIconIsCreatedOnlyForCalendarTiles()
    {
        var icon = new DaisyOS.Shell.Controls.LauncherCalendarIcon();
        Assert.Null(icon.Child);
        icon.IsCalendar = true;
        Assert.IsType<DaisyOS.Shell.Controls.CalendarAppIcon>(icon.Child);
        icon.IsCalendar = false;
        Assert.Null(icon.Child);
    }

    [AvaloniaFact]
    public void ReopeningFromALaterPageDoesNotAnimateAnOutgoingPage()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            launcher.AnimateIn();
            model.GoToPage(1);
            launcher.CloseImmediate();
            launcher.AnimateIn();
            Assert.Equal(0, model.PageIndex);
            var outgoing = launcher.NeighbourPageGrid;
            Assert.False(outgoing.IsVisible);
            Assert.Null(outgoing.ItemsSource);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public void PagesAndSearchReuseTilesAndUpdateTheirBindings()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var grid = launcher.CurrentPageGrid;
            Button[] Tiles() => grid.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Classes.Contains("LauncherTile") && button.IsEffectivelyVisible).ToArray();
            void CheckBindings(Button[] tiles)
            {
                Assert.Equal(model.PageApps.Count, tiles.Length);
                foreach (var item in model.PageApps)
                {
                    var tile = Assert.Single(tiles, button => ReferenceEquals(button.DataContext, item));
                    Assert.Contains(tile.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == item.Name);
                    Assert.Equal(item.IsTopSearchResult, tile.Classes.Contains("SearchSelected"));
                }
            }
            Assert.IsType<DaisyOS.Shell.Controls.LauncherItemsControl>(grid);
            Assert.IsType<DaisyOS.Shell.Controls.VirtualizingLauncherGrid>(grid.ItemsPanelRoot);
            var original = Tiles();
            Assert.NotEmpty(original);
            var containers = Enumerable.Range(0, model.PageApps.Count).Select(grid.ContainerFromIndex).ToArray();
            model.GoToPage(1);
            window.UpdateLayout();
            Assert.All(Enumerable.Range(0, model.PageApps.Count).Select(grid.ContainerFromIndex), container => Assert.Contains(container, containers));
            var next = Tiles();
            CheckBindings(next);
            Assert.All(next, tile => Assert.Contains(tile, original));
            model.SearchText = "App 99";
            window.UpdateLayout();
            var result = Assert.Single(Tiles());
            Assert.Contains(result, original);
            CheckBindings([result]);
            Assert.Equal("app-99", model.ResolveSearchResult()?.App.Id);
            model.SearchText = "missing application";
            window.UpdateLayout();
            Assert.Empty(Tiles());
            model.SearchText = "";
            window.UpdateLayout();
            CheckBindings(Tiles());
            Assert.All(Tiles(), tile => Assert.Contains(tile, original));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PageSnapshotsAreReusedAndInvalidatedAfterSearchAndResize()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray());
        model.SetPageLayout(4, 3);
        var original = model.GetPageApps(0);
        model.GoToPage(1);
        model.GoToPage(0);
        Assert.Same(original, model.GetPageApps(0));
        model.SearchText = "App 99";
        Assert.Single(model.GetPageApps(0));
        Assert.Equal("app-99", model.GetPageApps(0)[0].App.Id);
        model.SearchText = "";
        Assert.NotSame(original, model.GetPageApps(0));
        model.SetPageLayout(3, 2);
        Assert.Equal(6, model.GetPageApps(0).Length);
        Assert.Equal(model.PageApps.ToArray(), model.GetPageApps(0));
    }

    [AvaloniaFact]
    public void ShortQuickFlickChangesOnePageWhilePausedMovementAndEdgesDoNot()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            void Scroll(double pixels, ulong time, bool end = false) => viewport.RaiseEvent(new PointerWheelEventArgs(
                viewport, pointer, window, new Point(100, 100), time, new PointerPointProperties(),
                KeyModifiers.None, new Vector(pixels / 4, 0)) { IsPrecise = true, IsScrollEnd = end });
            Scroll(-32, 1000);
            Assert.Equal(0, model.PageIndex); // Finger-down movement never autonomously commits.
            Scroll(0, 1010, true);
            Assert.Equal(1, model.PageIndex);
            Scroll(32, 2000);
            Scroll(0, 2010, true);
            Assert.Equal(0, model.PageIndex);
            Scroll(32, 3000); // Flicking past the first page only resists and returns.
            Scroll(0, 3010, true);
            Assert.Equal(0, model.PageIndex);
            Scroll(-32, 4000);
            Scroll(0, 4200, true); // A stationary hold removes flick intent.
            Assert.Equal(0, model.PageIndex);
            Assert.True(launcher.CurrentPageGrid.IsHitTestVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void UnchangedPageAndMatchingSearchKeepTheExistingTiles()
    {
        var apps = Enumerable.Range(0, 20)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        var resets = model.PageApps.NotificationCount;
        var original = model.PageApps.ToArray();
        model.GoToPage(0);
        model.GoToPage(-1);
        Assert.Equal(resets, model.PageApps.NotificationCount);
        model.SearchText = "App";
        Assert.Equal(original, model.PageApps.ToArray());
        Assert.Equal(resets, model.PageApps.NotificationCount);
    }

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
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.FromMilliseconds(240);
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var incoming = launcher.CurrentPageGrid;
            var outgoing = launcher.NeighbourPageGrid;
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
            launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
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
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            var current = launcher.CurrentPageGrid;
            var neighbour = launcher.NeighbourPageGrid;
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            ulong time = 1000;
            void Scroll(double x, bool end = false) => viewport.RaiseEvent(new PointerWheelEventArgs(
                viewport, pointer, window, new Point(100, 100), time += 160, new PointerPointProperties(),
                KeyModifiers.None, new Vector(x / 4, 0)) { IsPrecise = true, IsScrollEnd = end });
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
            Scroll(-220);
            Scroll(0, true);
            Assert.Equal(1, model.PageIndex);
            current = launcher.CurrentPageGrid;
            neighbour = launcher.NeighbourPageGrid;
            Assert.Null(current.RenderTransform);
            Assert.True(launcher.CurrentPageGrid.IsHitTestVisible);
            Scroll(220);
            Scroll(0, true);
            Assert.Equal(0, model.PageIndex);
            current = launcher.CurrentPageGrid;
            neighbour = launcher.NeighbourPageGrid;
            Scroll(160); // Resist dragging past the first page.
            Assert.Equal(80d * 160 / 240, current.RenderTransform!.Value.M31, 5);
            Assert.False(neighbour.IsVisible);
            Scroll(0, true);
            Assert.Equal(0, model.PageIndex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PinchProgressTracksContinuouslyAndCancelledCloseRestoresInteraction()
    {
        var model = new LauncherViewModel(null, [new AppEntry("app", "App", "", "", false)]);
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            launcher.AnimateIn(interactive: true);
            launcher.BeginInteractiveReveal(true);
            var material = launcher.FindControl<Border>("LauncherRootBorder")!;
            var stage = launcher.FindControl<Grid>("LauncherStage")!;
            Assert.Equal(0, material.Opacity);
            launcher.UpdateInteractiveReveal(0.4);
            Assert.Equal(0.4, material.Opacity);
            Assert.Equal(0.928, stage.RenderTransform!.Value.M11, 5);
            launcher.UpdateInteractiveReveal(0.2); // Reverse without restarting animation.
            Assert.Equal(0.2, material.Opacity);
            Assert.False(launcher.IsHitTestVisible);
            var completed = false;
            launcher.EndInteractiveReveal(true, () => completed = true);
            Assert.True(completed);
            Assert.Equal(1, material.Opacity);
            Assert.True(launcher.IsHitTestVisible);
            launcher.BeginInteractiveReveal(false);
            launcher.UpdateInteractiveReveal(0.6);
            launcher.Resources["MotionDurationShellEnter"] = TimeSpan.FromMilliseconds(220);
            launcher.Resources["MotionDurationShellExit"] = TimeSpan.FromMilliseconds(220);
            launcher.EndInteractiveReveal(false, () => Assert.Fail("Interrupted close must not complete"));
            Assert.Equal(false, launcher.RevealSettlingTarget);
            Assert.Equal(0.6, launcher.BeginInteractiveReveal(true));
            Assert.Equal(0.6, material.Opacity); // Catch an in-flight close without a jump.
            launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
            launcher.EndInteractiveReveal(true, () => { });
            Assert.Equal(1, material.Opacity);
            Assert.True(launcher.IsHitTestVisible);
            launcher.CloseImmediate();
            launcher.UpdateInteractiveReveal(0.8); // Stale compositor sample cannot resurrect it.
            Assert.False(launcher.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PreciseSwipeKeepsItsInitialAxisDespiteDiagonalNoise()
    {
        var model = new LauncherViewModel(null, Enumerable.Range(0, 100)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray());
        var launcher = new LauncherView(model);
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        var window = new Window { Width = 1280, Height = 900, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            var viewport = launcher.FindControl<Grid>("LauncherPageViewport")!;
            var current = launcher.CurrentPageGrid;
            using var pointer = new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, true);
            ulong time = 1000;
            void Scroll(Vector delta, bool end = false) => viewport.RaiseEvent(new PointerWheelEventArgs(
                viewport, pointer, window, new Point(100, 100), time += 160, new PointerPointProperties(),
                KeyModifiers.None, delta / 4) { IsPrecise = true, IsScrollEnd = end });
            Scroll(new Vector(-30, -2));
            Scroll(new Vector(-5, 50));
            Assert.Equal(-35, current.RenderTransform!.Value.M31);
            Scroll(default, true);
            Assert.Equal(0, model.PageIndex);
            Assert.Null(current.RenderTransform);
            Assert.True(current.IsHitTestVisible);
            Scroll(new Vector(-60, 0));
            launcher.BeginInteractiveReveal(false); // Pinching interrupts page motion safely.
            Assert.Null(current.RenderTransform);
            Assert.False(launcher.NeighbourPageGrid.IsVisible);
            launcher.EndInteractiveReveal(true, () => { });
            Scroll(new Vector(0, -220)); // A new swipe may select the other axis.
            Scroll(default, true);
            Assert.Equal(1, model.PageIndex);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void SmartSearchRanksPrefixesAcronymsAndRecentUsageAboveDescriptionMatches()
    {
        var tempDir = Directory.CreateTempSubdirectory("daisy-launcher-rank-");
        try
        {
            var session = new DaisyOS.Shell.Services.ShellSessionState(Path.Combine(tempDir.FullName, "state.json"));
            session.RecordApplicationLaunch("firefox");
            session.RecordApplicationLaunch("firefox");

            var apps = new[]
            {
                new AppEntry("asset-helper", "Asset Helper", "Manage system settings and presets", "", false),
                new AppEntry("settings", "Settings", "System preferences", "", false),
                new AppEntry("files", "Files", "File manager", "", false),
                new AppEntry("firefox", "Firefox", "Web browser", "", false),
                new AppEntry("dsh", "DeepSeek Harness", "AI coding harness", "", false),
                new AppEntry("sysmon", "System Monitor", "Processes and resources", "", false)
            };

            var model = new LauncherViewModel(session, apps);

            // Name prefix ("Settings") outranks description match ("Asset Helper").
            model.SearchText = "set";
            Assert.Equal("settings", model.ResolveSearchResult()?.App.Id);

            //Acronym & camel-case initials ("sm" -> System Monitor, "dsh" -> DeepSeek Harness).
            model.SearchText = "sm";
            Assert.Equal("sysmon", model.ResolveSearchResult()?.App.Id);

            model.SearchText = "dsh";
            Assert.Equal("dsh", model.ResolveSearchResult()?.App.Id);

            // Frequent/recent launches boost "Firefox" ahead of "Files" on shared prefix "fi".
            model.SearchText = "fi";
            Assert.Equal("firefox", model.ResolveSearchResult()?.App.Id);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [AvaloniaFact]
    public void KeyboardSelectionNavigatesGridPagesAndUpdatesSearchTarget()
    {
        var apps = Enumerable.Range(0, 10)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        model.SetPageLayout(3, 2); // 6 per page -> 2 pages

        Assert.Null(model.SelectedItem);
        Assert.True(model.MoveSelection(1, 0));
        Assert.Equal("app-0", model.SelectedItem?.App.Id);

        Assert.True(model.MoveSelection(0, 1));
        Assert.Equal("app-3", model.SelectedItem?.App.Id);

        // Step across page boundary onto page 1.
        model.MoveSelection(1, 0); // app-4
        model.MoveSelection(1, 0); // app-5
        model.MoveSelection(1, 0); // app-6 (page 1)
        Assert.Equal(1, model.PageIndex);
        Assert.Equal("app-6", model.SelectedItem?.App.Id);

        // In search mode, stepping right moves the Enter target to the second result.
        model.SearchText = "App 0";
        Assert.Equal("app-0", model.ResolveSearchResult()?.App.Id);
        model.MoveSelection(1, 0);
        Assert.Equal("app-1", model.ResolveSearchResult()?.App.Id);
        Assert.True(model.SelectedItem!.IsTopSearchResult);
    }

    [AvaloniaFact]
    public void LauncherCapsGridAtFiveRowsAndUsesStandardSearchHeight()
    {
        var apps = Enumerable.Range(0, 60)
            .Select(i => new AppEntry($"app-{i}", $"App {i:00}", "", "", false)).ToArray();
        var model = new LauncherViewModel(null, apps);
        model.SetPageLayout(8, 9);
        Assert.Equal(5, model.PageRows);
        Assert.Equal(40, model.PageSize);

        var launcher = new LauncherView(model);
        var window = new Window { Width = 1920, Height = 1440, Content = launcher };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.True(model.PageRows <= 5);
            Assert.Equal(3, model.PageRows);

            var searchBar = launcher.FindControl<DaisyOS.Shell.Controls.SearchBar>("LauncherSearchBar")!;
            Assert.True(Application.Current!.Resources.TryGetResource("SearchHeightStandard", null, out var expected));
            Assert.Equal((double)expected!, searchBar.Height);
        }
        finally
        {
            window.Close();
        }
    }

}
