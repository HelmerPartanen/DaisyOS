using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.AppStore;

public partial class AppStoreView : UserControl
{
    public static readonly StyledProperty<int> CardColumnsProperty = AvaloniaProperty.Register<AppStoreView, int>(nameof(CardColumns), 1);
    public static readonly StyledProperty<int> FeaturedColumnsProperty = AvaloniaProperty.Register<AppStoreView, int>(nameof(FeaturedColumns), 1);
    public static readonly StyledProperty<double> CardContentWidthProperty = AvaloniaProperty.Register<AppStoreView, double>(nameof(CardContentWidth), 480);
    public double CardContentWidth { get => GetValue(CardContentWidthProperty); private set => SetValue(CardContentWidthProperty, value); }
    public int CardColumns { get => GetValue(CardColumnsProperty); private set => SetValue(CardColumnsProperty, value); }
    public int FeaturedColumns { get => GetValue(FeaturedColumnsProperty); private set => SetValue(FeaturedColumnsProperty, value); }
    private string[] _screenshots = [];
    private int _screenshotIndex;
    private Button? _screenshotSource;
    private Window? _hostWindow;
    private AppStoreViewModel? ViewModel => DataContext as AppStoreViewModel;
    public AppStoreView()
    {
        InitializeComponent();
        Focusable = true;
        AddHandler(PointerPressedEvent, (_, args) =>
        {
            if (StoreSearch.IsKeyboardFocusWithin && args.Source is Visual source &&
                !ReferenceEquals(source, StoreSearch) && !source.GetVisualAncestors().Contains(StoreSearch))
                Focus();
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        SizeChanged += (_, _) => UpdateColumns();
        ScreenshotOverlay.SizeChanged += (_, _) => UpdateScreenshotSize();
        AppsScroller.SizeChanged += (_, _) => UpdateColumns();
        AppsScroller.PropertyChanged += (_, e) => { if (e.Property == ScrollViewer.ViewportProperty) UpdateColumns(); };
    }
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _hostWindow = TopLevel.GetTopLevel(this) as Window;
        _hostWindow?.AddHandler(InputElement.KeyDownEvent, OnViewKeyDown, RoutingStrategies.Tunnel);
    }
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        CloseScreenshot();
        _hostWindow?.RemoveHandler(InputElement.KeyDownEvent, OnViewKeyDown); _hostWindow = null;
        base.OnDetachedFromVisualTree(e);
    }
    private void UpdateColumns()
    {
        CardContentWidth = Math.Max(0, (AppsScroller.Viewport.Width > 0 ? AppsScroller.Viewport.Width : Bounds.Width - 260 - 14) - 32);
        var width = Math.Max(320, CardContentWidth);
        CardColumns = Math.Clamp((int)(width / 320), 1, 4);
        FeaturedColumns = Math.Clamp(CardColumns, 1, 2);
    }
    private void OnViewKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled) return;
        if (ScreenshotOverlay.IsVisible)
        {
            switch (e.Key)
            {
                case Key.Escape: CloseScreenshot(); break;
                case Key.Left: ShowScreenshot(_screenshotIndex - 1); break;
                case Key.Right: ShowScreenshot(_screenshotIndex + 1); break;
                case Key.Home: ShowScreenshot(0); break;
                case Key.End: ShowScreenshot(_screenshots.Length - 1); break;
                default: return;
            }
            e.Handled = true;
            return;
        }
        if (e.Key == Key.F && e.KeyModifiers == KeyModifiers.Control) { StoreSearch.FocusInput(); e.Handled = true; }
        else if ((e.Key == Key.Escape || e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt) && ViewModel?.IsDetailMode == true)
        { ViewModel.BackCommand.Execute(null); e.Handled = true; }
        else if (e.Key == Key.Escape && !string.IsNullOrEmpty(StoreSearch.Text)) { StoreSearch.Text = ""; e.Handled = true; }
        else if (e.Key == Key.F5) { ViewModel?.RetryCommand.Execute(null); e.Handled = true; }
    }
    private void OnScreenshotClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: string location } button || ViewModel?.SelectedApp is not { } app) return;
        _screenshots = app.Screenshots.ToArray();
        var index = Array.IndexOf(_screenshots, location);
        if (index < 0) return;
        _screenshotSource = button;
        ScreenshotTitle.Text = app.Name;
        StorePages.IsEnabled = false;
        NavigationSidebar.IsEnabled = false;
        ScreenshotOverlay.IsVisible = true;
        UpdateScreenshotSize();
        ShowScreenshot(index);
        CloseScreenshotButton.Focus();
    }
    private void ShowScreenshot(int index)
    {
        if (index < 0 || index >= _screenshots.Length) return;
        _screenshotIndex = index;
        ScreenshotPreview.Location = _screenshots[index];
        ScreenshotCounter.Text = Loc.Format("Screenshot {0} of {1}", index + 1, _screenshots.Length);
        PreviousScreenshotButton.IsEnabled = index > 0;
        NextScreenshotButton.IsEnabled = index < _screenshots.Length - 1;
    }
    private void CloseScreenshot()
    {
        ScreenshotOverlay.IsVisible = false;
        ScreenshotPreview.Location = null;
        _screenshots = [];
        StorePages.IsEnabled = true;
        NavigationSidebar.IsEnabled = true;
        _screenshotSource?.Focus();
        _screenshotSource = null;
    }
    private void UpdateScreenshotSize()
    {
        ScreenshotOverlay.Margin = new Thickness(0, AppTitleBar.Bounds.Height, 0, 0);
        ScreenshotDialog.Width = Math.Max(0, Math.Min(960, ScreenshotOverlay.Bounds.Width - 48));
        ScreenshotDialog.Height = Math.Max(0, Math.Min(600, ScreenshotOverlay.Bounds.Height - 48));
    }
    private void OnScreenshotBackdropPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && (source == ScreenshotDialog || source.GetVisualAncestors().Contains(ScreenshotDialog))) return;
        CloseScreenshot();
        e.Handled = true;
    }
    private void OnCloseScreenshotClicked(object? sender, RoutedEventArgs e) => CloseScreenshot();
    private void OnPreviousScreenshotClicked(object? sender, RoutedEventArgs e) => ShowScreenshot(_screenshotIndex - 1);
    private void OnNextScreenshotClicked(object? sender, RoutedEventArgs e) => ShowScreenshot(_screenshotIndex + 1);
    private void OnCategoryClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppCategoryItem category } && ViewModel is { } vm)
        { vm.SelectedCategory = category; AppsScroller.Offset = default; }
    }
    private void OnCardPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (sender is not Border { DataContext: AppStoreCardViewModel card } || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Visual source && (source is Button || source.GetVisualAncestors().Any(v => v is Button))) return;
        ViewModel?.ShowDetails(card); DetailScroller.Offset = default; e.Handled = true;
    }
    private void OnDetailsClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: AppStoreCardViewModel card })
        { ViewModel?.ShowDetails(card); DetailScroller.Offset = default; }
    }
    private async void OnActionButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AppStoreCardViewModel card }) return;
        try
        {
            if (card.CanInstall && await card.PrepareInstallAsync() is { } plan)
            {
                var dialog = new Views.Dialogs.ApplicationTransactionDialog(plan);
                if (await dialog.ConfirmAsync(TopLevel.GetTopLevel(this) as Window)) await card.InstallReviewedAsync(dialog.Plan);
                return;
            }
            await card.PrimaryActionAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::System.Text.Json.JsonException)
        { (Application.Current as App)?.Feedback.Show(ex.Message); }
    }
    private void OnOpenClicked(object? sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: AppStoreCardViewModel card }) card.Launch(); }
    private void OnCancelClicked(object? sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: AppStoreCardViewModel card }) card.Cancel(); }
    private async void OnUninstallButtonClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: AppStoreCardViewModel card } || TopLevel.GetTopLevel(this) is not Window owner) return;
        try
        {
            var plan = await card.PrepareRemovalAsync();
            if (plan is null) { if (await Views.Dialogs.ConfirmationDialog.ShowApplicationRemovalAsync(owner, card.Name)) await card.UninstallAsync(); return; }
            var dialog = new Views.Dialogs.ApplicationTransactionDialog(plan);
            if (await dialog.ConfirmAsync(owner)) await card.UninstallAsync(dialog.Plan, dialog.RemoveData);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or global::System.Text.Json.JsonException)
        { (Application.Current as App)?.Feedback.Show(ex.Message); }
    }
    private void OnFlathubClicked(object? sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: AppStoreCardViewModel card }) OpenUrl(card.AppUrl); }
    private void OnWebsiteClicked(object? sender, RoutedEventArgs e)
    { if (sender is Button { DataContext: AppStoreCardViewModel card } && card.HasHomepage) OpenUrl(card.Item.Homepage); }
    private static void OpenUrl(string url)
    { if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https") new SafeProcessLauncher().Launch("xdg-open", [uri.AbsoluteUri]); }
}
