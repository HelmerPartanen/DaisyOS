using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Apps.AppStore;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AppStoreWorkflowTests
{
    private static AppStoreItem Item(int n, AppInstallState state = AppInstallState.NotInstalled) => new(
        $"org.example.App{n}", $"org.example.App{n}", $"Application {n}", "An application for everyday work", "A real description.",
        AppCategory.Productivity, "", "package", "1.0", "Example", "", 0, n < 4, false, state) { IsVerified = n % 2 == 0 };
    [AvaloniaTheory]
    [InlineData(800, 540)]
    [InlineData(1120, 760)]
    [InlineData(2300, 1440)]
    public async Task ScreenshotViewerNavigatesAndEscapeReturnsToDetails(int width, int height)
    {
        using var vm = new AppStoreViewModel(new StoreFake()); await vm.PendingLoad;
        var window = new AppStoreWindow(vm, ownsViewModel: false) { Width = width, Height = height }; window.Show();
        try
        {
            await vm.RefreshAsync();
            var card = vm.Apps[0];
            card.UpdateItem(card.Item with { Screenshots = new[] { "/tmp/preview-one.png", "/tmp/preview-two.png" } });
            vm.ShowDetails(card);
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var view = window.GetVisualDescendants().OfType<AppStoreView>().Single();
            var source = view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.DataContext, "/tmp/preview-two.png"));
            Assert.Equal(280, source.Bounds.Height);
            source.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var dialog = view.FindControl<Grid>("ScreenshotDialog")!;
            Assert.InRange(dialog.Bounds.Width, Math.Min(width - 48, 960), 960);
            Assert.InRange(dialog.Bounds.Height, 1, 600);
            var pages = view.FindControl<Grid>("StorePages")!;
            Assert.True(pages.IsVisible); Assert.False(pages.IsEnabled);
            var imageFrame = view.FindControl<Border>("ScreenshotImageFrame")!;
            Assert.Null(imageFrame.Background);
            Assert.True(imageFrame.Bounds.Width > source.Bounds.Width);
            var overlay = view.FindControl<Grid>("ScreenshotOverlay")!;
            var preview = view.FindControl<AsyncImage>("ScreenshotPreview")!;
            Assert.True(overlay.IsVisible); Assert.Equal("/tmp/preview-two.png", preview.Location);
            Assert.False(view.FindControl<Button>("NextScreenshotButton")!.IsEnabled);
            window.KeyPressQwerty(PhysicalKey.ArrowLeft, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal("/tmp/preview-one.png", preview.Location);
            Assert.False(view.FindControl<Button>("PreviousScreenshotButton")!.IsEnabled);
            window.KeyPressQwerty(PhysicalKey.ArrowLeft, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal("/tmp/preview-one.png", preview.Location);
            window.KeyPressQwerty(PhysicalKey.ArrowRight, Avalonia.Input.RawInputModifiers.None);
            Assert.Equal("/tmp/preview-two.png", preview.Location);
            window.KeyPressQwerty(PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            Assert.False(overlay.IsVisible); Assert.Null(preview.Location);
            Assert.True(vm.IsDetailMode); Assert.Same(card, vm.SelectedApp);
            Assert.True(view.FindControl<Grid>("StorePages")!.IsVisible);
            Assert.True(source.IsFocused);
            source.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var backdropPoint = overlay.TranslatePoint(new Point(16, 16), window)!.Value;
            window.MouseDown(backdropPoint, MouseButton.Left);
            window.MouseUp(backdropPoint, MouseButton.Left);
            Assert.False(overlay.IsVisible);
            Assert.True(pages.IsEnabled);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task PaginationSortingAndVerifiedFilterDoNotRefetchTheCatalog()
    {
        var fake = new StoreFake(); using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        Assert.Equal(24, vm.Apps.Count); Assert.True(vm.HasMore);
        vm.LoadMoreCommand.Execute(null); Assert.Equal(48, vm.Apps.Count);
        var queries = fake.Queries;
        vm.VerifiedOnly = true;
        Assert.All(vm.Apps, a => Assert.True(a.IsVerified));
        Assert.Equal(24, vm.Apps.Count);
        vm.SortIndex = 1;
        Assert.Equal(vm.Apps.Select(a => a.Name).OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase), vm.Apps.Select(a => a.Name));
        Assert.Equal(queries, fake.Queries);
    }
    [AvaloniaFact]
    public async Task RefreshKeepsCardsVisibleAndDiscoveryDoesNotFetchInstalledStateTwice()
    {
        var fake = new StoreFake();
        using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        Assert.Equal(0, fake.FeaturedQueries);
        var cards = vm.Apps.ToArray();
        var pending = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        fake.Category = () => pending.Task;
        var refresh = vm.RefreshAsync();
        Assert.True(vm.IsLoading);
        Assert.True(vm.HasResults);
        Assert.False(vm.ShowSkeleton);
        Assert.Equal(cards, vm.Apps);
        pending.SetException(new IOException("offline")); await refresh;
        Assert.True(vm.HasResults);
        Assert.True(vm.HasCatalogWarning);
        Assert.Equal(cards, vm.Apps);
    }

    [AvaloniaFact]
    public async Task NewPageShowsSkeletonAndNeverShowsPreviousCategoryCards()
    {
        var pending = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        var fake = new StoreFake();
        using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        fake.Category = () => pending.Task;
        vm.SelectedCategory = vm.Categories[1];
        Assert.True(vm.ShowSkeleton);
        Assert.False(vm.HasResults);
        Assert.Empty(vm.Apps);
        foreach (var theme in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            var window = new AppStoreWindow(vm, ownsViewModel: false) { Width = 800, Height = 540, RequestedThemeVariant = theme };
            window.Show();
            try
            {
                Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
                var skeleton = window.GetVisualDescendants().OfType<SkeletonLoader>().Single();
                Assert.True(skeleton.IsEffectivelyVisible);
                Assert.True(skeleton.Bounds.Width > 0);
                Assert.False(skeleton.IsHitTestVisible);
                Assert.Equal(6, skeleton.Children.Count);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save($"/tmp/daisy-store-loading-{theme}.png");
            }
            finally { window.Close(); }
        }
        pending.SetResult([Item(1)]); await vm.PendingLoad;
        Assert.False(vm.ShowSkeleton);
        Assert.Single(vm.Apps);
    }

    [AvaloniaFact]
    public async Task DetailsUseTheSameCardAndInstallStateSurvivesNavigation()
    {
        var fake = new StoreFake(); using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        var app = vm.Apps.First(); vm.ShowDetails(app);
        Assert.True(vm.IsDetailMode); Assert.Same(app, vm.SelectedApp);
        await app.InstallAsync(); Assert.True(app.CanOpen); Assert.True(app.CanUninstall);
        vm.BackCommand.Execute(null); Assert.False(vm.IsDetailMode);
        vm.SelectedCategory = vm.LibraryCategories[0]; await vm.PendingLoad;
        Assert.Same(app, Assert.Single(vm.Apps));
        await app.UninstallAsync(); Assert.Empty(vm.Apps); Assert.False(app.CanOpen);
    }
    [AvaloniaFact]
    public async Task FailedInstallLeavesRetryAvailableAndCancellationReleasesBusyState()
    {
        var fake = new StoreFake { FailInstall = true }; using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        var card = vm.Apps.First(); await card.InstallAsync();
        Assert.True(card.HasError); Assert.True(card.CanInstall); Assert.False(card.IsBusy);
        Assert.DoesNotContain("private", card.ErrorMessage);
        fake.FailInstall = false; fake.WaitForCancel = true;
        var operation = card.InstallAsync(); Assert.True(card.IsBusy); Assert.True(vm.HasActiveOperations);
        card.Cancel(); await operation; Assert.False(card.IsBusy); Assert.False(vm.HasActiveOperations);
        Assert.True(card.CanInstall);
    }
    [AvaloniaFact]
    public async Task UpdateAllIncludesUpdatesBeyondTheFirstPage()
    {
        var fake = new StoreFake(); using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        vm.SelectedCategory = vm.LibraryCategories[1]; await vm.PendingLoad;
        Assert.Equal(24, vm.Apps.Count);
        vm.UpdateAllCommand.Execute(null);
        // Fake updates complete synchronously; all 60 applications must be processed.
        Assert.Equal(60, fake.Updated.Count);
        Assert.Empty(vm.Apps);
    }
    [AvaloniaFact]
    public async Task ClosingSharedWindowKeepsTransactionsAndModelAvailableForReopening()
    {
        var fake = new StoreFake { WaitForCancel = true };
        using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        var window = new AppStoreWindow(vm, ownsViewModel: false); window.Show();
        var card = vm.Apps[0]; var operation = card.InstallAsync();
        window.Close(); Assert.True(card.IsBusy); Assert.True(vm.HasActiveOperations);
        var reopened = new AppStoreWindow(vm, ownsViewModel: false); reopened.Show();
        Assert.Same(vm, reopened.DataContext); Assert.True(vm.HasActiveOperations);
        card.Cancel(); await operation; reopened.Close();
        Assert.False(vm.HasActiveOperations);
        await vm.RefreshAsync(); Assert.NotEmpty(vm.Apps);
    }
    [AvaloniaFact]
    public async Task SupersededSearchCannotReplaceCurrentResultsAndDisposedLoadsStayQuiet()
    {
        var old = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        var current = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        var fake = new StoreFake { Search = query => query == "old" ? old.Task : current.Task };
        using var vm = new AppStoreViewModel(fake); await vm.PendingLoad;
        vm.SearchQuery = "old"; var oldLoad = vm.RefreshAsync();
        vm.SearchQuery = "new"; var newLoad = vm.RefreshAsync();
        old.SetResult([Item(1)]); await oldLoad;
        Assert.True(vm.IsLoading); Assert.Empty(vm.Apps);
        current.SetResult([Item(2)]); await newLoad;
        Assert.Equal(Item(2).Id, Assert.Single(vm.Apps).Id);
        var late = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        fake.Search = _ => late.Task; vm.SearchQuery = "late"; var pending = vm.RefreshAsync();
        vm.Dispose(); late.SetResult([Item(3)]); await pending;
        Assert.Empty(vm.Apps);
    }
    [AvaloniaFact]
    public async Task NavigationAndCardLabelsFollowLiveLanguageChanges()
    {
        using var vm = new AppStoreViewModel(new StoreFake()); await vm.PendingLoad;
        try
        {
            DaisyOS.Shell.Services.Localization.Loc.Apply(Application.Current!.Resources, "fi-FI");
            Assert.Equal("Löydä", vm.Categories[0].Name);
            Assert.Equal("Asenna", vm.Apps[0].ActionText);
        }
        finally { DaisyOS.Shell.Services.Localization.Loc.Apply(Application.Current!.Resources, "en-US"); }
    }
    [AvaloniaTheory]
    [InlineData(800, false)][InlineData(800, true)][InlineData(1120, false)][InlineData(1120, true)]
    public async Task NativeChromeFullHeightSidebarAndKeyboardNavigation(int width, bool dark)
    {
        using var vm = new AppStoreViewModel(new StoreFake()); await vm.PendingLoad;
        var window = new AppStoreWindow(vm) { Width = width, Height = 720, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            var sidebar = window.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "NavigationSidebar");
            Assert.Equal(((Grid)sidebar.Parent!).Bounds.Height, sidebar.Bounds.Height, 1);
            Assert.Single(window.GetVisualDescendants().OfType<AppWindowTitleBar>());
            Assert.True(sidebar.TryFindResource("SurfaceRaisedSubtleBrush", window.RequestedThemeVariant, out var brush));
            Assert.Same(brush, sidebar.Background);
            vm.ShowDetails(vm.Apps[0]); window.KeyPressQwerty(PhysicalKey.Escape, Avalonia.Input.RawInputModifiers.None);
            Assert.False(vm.IsDetailMode);
            window.WindowState = WindowState.Maximized; Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowCaptionKind.Restore, window.GetVisualDescendants().OfType<WindowCaptionGlyph>().Single(g => g.Name == "MaximizeIcon").Kind);
            window.KeyPressQwerty(PhysicalKey.F11, Avalonia.Input.RawInputModifiers.None);
            Assert.False(window.GetVisualDescendants().OfType<AppWindowTitleBar>().Single().IsVisible);
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public async Task MinimizedStoreCanBeRestoredFromItsNativeTaskbarSlot()
    {
        using var vm = new AppStoreViewModel(new StoreFake()); await vm.PendingLoad;
        var tracker = new DaisyOS.Shell.Services.Windows.NativeAppWindowTracker();
        var window = new AppStoreWindow(vm);
        tracker.Register("appstore", window); window.Show();
        try
        {
            window.WindowState = WindowState.Minimized;
            Assert.Equal(DaisyOS.Shell.Services.Windows.NativeAppWindowState.Minimized, tracker.GetState("appstore"));
            Assert.True(tracker.ToggleFromTaskbar("appstore"));
            Assert.Equal(WindowState.Normal, window.WindowState);
            Assert.Equal(DaisyOS.Shell.Services.Windows.NativeAppWindowState.Active, tracker.GetState("appstore"));
        }
        finally { window.Close(); }
        Assert.Equal(DaisyOS.Shell.Services.Windows.NativeAppWindowState.NotRunning, tracker.GetState("appstore"));
    }

    [AvaloniaTheory]
    [InlineData(800)][InlineData(1120)][InlineData(1360)][InlineData(1920)]
    public async Task LongCardContentStaysInsideHorizontalViewport(int width)
    {
        using var vm = new AppStoreViewModel(new StoreFake { LongText = true }); await vm.PendingLoad;
        var window = new AppStoreWindow(vm) { Width = width, Height = 760 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var scroller = window.GetVisualDescendants().OfType<ScrollViewer>().Single(v => v.Name == "AppsScroller");
            var cards = scroller.GetVisualDescendants().OfType<Border>().Where(b => b.Classes.Contains("StoreCard")).ToArray();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                var point = card.TranslatePoint(default, scroller)!.Value;
                Assert.True(point.X >= -1, $"Left overflow: {point.X}");
                Assert.True(point.X + card.Bounds.Width <= scroller.Viewport.Width + 1,
                    $"Right overflow at {width}: {point.X} + {card.Bounds.Width} > {scroller.Viewport.Width}");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task InstalledCardUsesPrimaryOpenButtonAndReactsToStateChanges()
    {
        var service = new StoreFake(); service.Installed.Add(Item(0).Id);
        using var vm = new AppStoreViewModel(service); await vm.PendingLoad;
        var window = new AppStoreWindow(vm) { Width = 1120, Height = 760 }; window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var card = vm.Apps.Single(c => c.Id == Item(0).Id);
            var buttons = window.GetVisualDescendants().OfType<Button>().Where(b => ReferenceEquals(b.DataContext, card) && b.Classes.Contains("Compact")).ToArray();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons) { Assert.Contains("Primary", button.Classes); Assert.DoesNotContain("Secondary", button.Classes); }
            card.InstallState = AppInstallState.NotInstalled; Dispatcher.UIThread.RunJobs();
            foreach (var button in buttons) { Assert.Contains("Secondary", button.Classes); Assert.DoesNotContain("Primary", button.Classes); }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task AppIconHidesFallbackAndMissingImageRestoresItInCardsAndDetails()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + ".png");
        using (var source = Avalonia.Platform.AssetLoader.Open(new Uri("avares://DaisyOS.Shell/Assets/AppIcons/Store_Light.png")))
        using (var output = File.Create(path)) source.CopyTo(output);
        using var vm = new AppStoreViewModel(new StoreFake()); await vm.PendingLoad;
        var card = vm.Apps[0];
        var window = new AppStoreWindow(vm) { Width = 1120, Height = 760 }; window.Show();
        try
        {
            await vm.RefreshAsync(); card.UpdateItem(card.Item with { IconPath = path });
            Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick(); Dispatcher.UIThread.RunJobs();
            var image = window.GetVisualDescendants().OfType<AsyncImage>().First(i => i.Name == "AppIconImage" && ReferenceEquals(i.DataContext, card));
            await WaitForImage(image);
            Assert.False(((Panel)image.Parent!).Children.OfType<DaisyIcon>().Single().IsVisible);
            vm.ShowDetails(card); Dispatcher.UIThread.RunJobs();
            var detail = window.GetVisualDescendants().OfType<AsyncImage>().Single(i => i.Name == "SelectedAppIconImage");
            await WaitForImage(detail);
            Assert.False(((Panel)detail.Parent!).Children.OfType<DaisyIcon>().Single().IsVisible);
            card.UpdateItem(card.Item with { IconPath = path + ".missing" }); Dispatcher.UIThread.RunJobs();
            Assert.False(detail.HasImage);
            Assert.True(((Panel)detail.Parent!).Children.OfType<DaisyIcon>().Single().IsVisible);
            vm.BackCommand.Execute(null); Dispatcher.UIThread.RunJobs();
            foreach (var visibleImage in window.GetVisualDescendants().OfType<AsyncImage>().Where(i => i.Name == "AppIconImage" && ReferenceEquals(i.DataContext, card)))
            {
                Assert.False(visibleImage.HasImage);
                Assert.True(((Panel)visibleImage.Parent!).Children.OfType<DaisyIcon>().Single().IsVisible);
            }
        }
        finally { window.Close(); File.Delete(path); }
        static async Task WaitForImage(AsyncImage image)
        {
            for (var n = 0; n < 200 && !image.HasImage; n++) { await Task.Delay(10); Dispatcher.UIThread.RunJobs(); }
            Assert.True(image.HasImage);
        }
    }

    private sealed class StoreFake : IAppStoreService
    {
        public Func<string, Task<IReadOnlyList<AppStoreItem>>>? Search;
        public Func<Task<IReadOnlyList<AppStoreItem>>>? Category;
        public int FeaturedQueries;
        public int Queries;
        public bool LongText;
        public bool FailInstall, WaitForCancel;
        public HashSet<string> Installed = [];
        public HashSet<string> Updated = [];
        private IReadOnlyList<AppStoreItem> All => Enumerable.Range(0, 60).Select(n => Item(n, Installed.Contains(Item(n).Id) ? AppInstallState.Installed : AppInstallState.NotInstalled)).Select(a => LongText ? a with { Name = new string('W', 200), Publisher = new string('W', 200), Summary = new string('W', 500) } : a).ToArray();
        public Task<IReadOnlyList<AppStoreItem>> GetAppsByCategoryAsync(AppCategory category, CancellationToken cancellationToken = default)
        { Queries++; return Category?.Invoke() ?? Task.FromResult(All); }
        public Task<IReadOnlyList<AppStoreItem>> GetFeaturedAppsAsync(CancellationToken cancellationToken = default)
        { FeaturedQueries++; return Task.FromResult<IReadOnlyList<AppStoreItem>>(All.Take(4).ToArray()); }
        public Task<IReadOnlyList<AppStoreItem>> SearchAppsAsync(string query, CancellationToken cancellationToken = default) => Search?.Invoke(query) ?? Task.FromResult(All);
        public Task<IReadOnlyList<AppStoreItem>> GetInstalledAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppStoreItem>>(All.Where(a => Installed.Contains(a.Id)).ToArray());
        public Task<IReadOnlyList<AppStoreItem>> GetUpdatesAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppStoreItem>>(All.Where(a => !Updated.Contains(a.Id)).Select(a => a with { InstallState = AppInstallState.UpdateAvailable }).ToArray());
        public async Task<bool> InstallAppAsync(string packageName, Action<double>? progressCallback = null, CancellationToken cancellationToken = default)
        { if (FailInstall) throw new IOException("private details"); if (WaitForCancel) await Task.Delay(Timeout.Infinite, cancellationToken); Installed.Add(packageName); return true; }
        public Task<bool> UninstallAppAsync(string packageName, CancellationToken cancellationToken = default) { Installed.Remove(packageName); return Task.FromResult(true); }
        public Task<bool> UpdateAppAsync(string appId, Action<double>? progressCallback = null, CancellationToken cancellationToken = default) { Updated.Add(appId); Installed.Add(appId); return Task.FromResult(true); }
        public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default) => Task.FromResult(Installed.Contains(packageName));
    }
}
