using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Platform;
using Avalonia.Styling;
using DaisyOS.Core.Models;
using DaisyOS.Core.Services;
using DaisyOS.Shell.Apps.AppStore;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Views.Dialogs;
using DaisyOS.System.AppStore;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class WorkflowRecoveryTests
{
    [AvaloniaFact]
    public async Task CatalogFallbackIconsAreAvailableWithoutSystemIconThemes()
    {
        var service = new LinuxAppStoreService(new UnavailablePackagesRunner());
        foreach (var category in Enum.GetValues<AppCategory>())
        {
            foreach (var app in await service.GetAppsByCategoryAsync(category))
            {
                var icon = new DaisyIcon { Icon = app.IconGlyph };
                Assert.True(AssetLoader.Exists(new Uri(icon.Path!)), $"Missing icon for {app.Name}: {app.IconGlyph}");
            }
        }
    }

    [AvaloniaFact]
    public void EveryAppStoreCategoryHasABundledIcon()
    {
        using var store = new AppStoreViewModel(new StoreStub());
        foreach (var category in store.Categories)
        {
            var icon = new DaisyIcon { Icon = category.Icon };
            Assert.True(AssetLoader.Exists(new Uri(icon.Path!)), $"Missing category icon: {category.Icon}");
        }
    }

    [AvaloniaTheory]
    [InlineData("  secret  ")]
    [InlineData("abcde")]
    public async Task WifiPassesCredentialsUnchangedAndAllowsBackendSpecificValidation(string password)
    {
        var service = new WirelessStub();
        var dialog = new WifiPasswordDialog(service, "Test network", "Secured network");
        dialog.Show();
        try
        {
            dialog.FindControl<TextBox>("PasswordBox")!.Text = password;
            await dialog.ConnectAsync();
            Assert.Equal(password, service.Password);
            Assert.Equal(1, service.Calls);
            Assert.True(dialog.FindControl<TextBox>("PasswordBox")!.IsEnabled);
            Assert.True(dialog.FindControl<Button>("CancelButton")!.IsEnabled);
        }
        finally { dialog.Close(); }
    }

    [AvaloniaFact]
    public async Task WifiCancelRemainsAvailableAndLateSuccessCannotReopenTheDialog()
    {
        var completion = new TaskCompletionSource<bool>();
        var service = new WirelessStub { Connect = _ => completion.Task };
        var dialog = new WifiPasswordDialog(service, "Test network", "WPA2");
        dialog.Show();
        dialog.FindControl<TextBox>("PasswordBox")!.Text = "test password";
        var request = dialog.ConnectAsync();
        await dialog.ConnectAsync();
        Assert.Equal(1, service.Calls);
        var cancel = dialog.FindControl<Button>("CancelButton")!;
        Assert.True(cancel.IsEnabled);
        cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(service.Token.IsCancellationRequested);
        Assert.Empty(dialog.FindControl<TextBox>("PasswordBox")!.Text!);
        completion.SetResult(true);
        await request;
        Assert.False(dialog.IsVisible);
        Assert.False(dialog.WasConnected);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WifiFailureIsReadableAndDoesNotExposeBackendDetails(bool dark)
    {
        var service = new WirelessStub { Connect = _ => throw new IOException("private backend details") };
        var dialog = new WifiPasswordDialog(service, "A network name that needs to be trimmed", "WPA2 / WPA3 Personal")
        {
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light
        };
        dialog.Show();
        try
        {
            dialog.FindControl<TextBox>("PasswordBox")!.Text = "test password";
            await dialog.ConnectAsync();
            var status = dialog.FindControl<TextBlock>("StatusMessageText")!;
            Assert.True(status.IsVisible);
            Assert.DoesNotContain("private backend", status.Text!);
            using var frame = dialog.CaptureRenderedFrame();
            Assert.NotNull(frame);
            SaveFrame(frame, $"wifi-error-{(dark ? "dark" : "light")}.png");
        }
        finally { dialog.Close(); }
    }

    [AvaloniaFact]
    public async Task StaleAppStoreRequestCannotReplaceNewResultsOrClearTheirLoadingState()
    {
        var old = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        var current = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        var service = new StoreStub { Search = query => query == "old" ? old.Task : current.Task };
        using var store = new AppStoreViewModel(service);
        store.SearchQuery = "old";
        var oldRequest = store.RefreshAsync();
        store.SearchQuery = "new";
        var currentRequest = store.RefreshAsync();
        old.SetResult([Item("old")]);
        await oldRequest;
        Assert.True(store.IsLoading);
        Assert.Empty(store.Apps);
        current.SetResult([Item("new")]);
        await currentRequest;
        Assert.Equal("new", Assert.Single(store.Apps).Name);
        Assert.False(store.IsLoading);
        Assert.False(store.HasError);
    }

    [AvaloniaFact]
    public async Task AppStoreErrorCanRetryAndCategoryNavigationClearsSearch()
    {
        var service = new StoreStub { Search = _ => throw new IOException("private details") };
        using var store = new AppStoreViewModel(service);
        store.SearchQuery = "missing";
        await store.RefreshAsync();
        Assert.True(store.HasError);
        Assert.False(store.ShowEmptyState);
        Assert.DoesNotContain("private details", store.ErrorMessage);
        service.Search = _ => Task.FromResult<IReadOnlyList<AppStoreItem>>([]);
        await store.RefreshAsync();
        Assert.False(store.HasError);
        Assert.True(store.ShowEmptyState);
        store.SelectedCategory = store.Categories.Single(category => category.Category == AppCategory.Development);
        await store.PendingLoad;
        Assert.Empty(store.SearchQuery);
        Assert.Contains(store.Apps, app => app.Name == "Development");
    }

    [AvaloniaFact]
    public async Task ClosingAppStoreDiscardsPendingResults()
    {
        var completion = new TaskCompletionSource<IReadOnlyList<AppStoreItem>>();
        var service = new StoreStub { Search = _ => completion.Task };
        var store = new AppStoreViewModel(service);
        store.SearchQuery = "pending";
        var request = store.RefreshAsync();
        store.Dispose();
        completion.SetResult([Item("late")]);
        await request;
        Assert.Empty(store.Apps);
    }

    [AvaloniaFact]
    public void StoreSearchLosesFocusWhenClickingContentAndKeepsItsQuery()
    {
        using var store = new AppStoreViewModel(new StoreStub());
        var view = new AppStoreView { DataContext = store };
        var window = new Window { Width = 900, Height = 650, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            var search = view.FindControl<DaisyOS.Shell.Controls.SearchBar>("StoreSearch")!;
            search.Text = "notes";
            search.FocusInput();
            Assert.True(search.IsKeyboardFocusWithin);
            window.MouseDown(new Avalonia.Point(800, 30), Avalonia.Input.MouseButton.Left);
            window.MouseUp(new Avalonia.Point(800, 30), Avalonia.Input.MouseButton.Left);
            Assert.False(search.IsKeyboardFocusWithin);
            Assert.Equal("notes", search.Text);
            Assert.Equal(0, Grid.GetRow(search));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 900)]
    [InlineData(true, 900)]
    [InlineData(false, 1280)]
    [InlineData(true, 1280)]
    public void AppStoreCardsUseReadableColumnsAtNarrowAndWideSizes(bool dark, int width)
    {
        using var store = new AppStoreViewModel(new StoreStub());
        var view = new AppStoreView { DataContext = store };
        var window = new Window { Width = width, Height = 650, Content = view, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            using var frame = window.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Assert.InRange(view.CardColumns, 1, 4);
            Assert.True((view.FindControl<ScrollViewer>("AppsScroller")!.Bounds.Width - 40) / view.CardColumns >= 300);
            Assert.True((view.FindControl<ScrollViewer>("AppsScroller")!.Bounds.Width - 40) / view.FeaturedColumns >= 420);
            SaveFrame(frame, $"store-{(dark ? "dark" : "light")}-{width}.png");
        }
        finally { window.Close(); }
    }

    private static void SaveFrame(Avalonia.Media.Imaging.WriteableBitmap frame, string name)
    {
        if (Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS") is not { Length: > 0 } directory) return;
        Directory.CreateDirectory(directory);
        frame.Save(Path.Combine(directory, name), Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
    }

    private static AppStoreItem Item(string name) => new(name, name, name, "An app for everyday work", "Description", AppCategory.Productivity,
        "", "file-text", "1.0", "Publisher", "12 MB", 4.5, false, false);

    private sealed class StoreStub : IAppStoreService
    {
        public Func<string, Task<IReadOnlyList<AppStoreItem>>> Search { get; set; } = query => Task.FromResult<IReadOnlyList<AppStoreItem>>([Item(query)]);
        public Task<IReadOnlyList<AppStoreItem>> GetFeaturedAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppStoreItem>>([Item("A featured application with a longer name"), Item("Another featured app")]);
        public Task<IReadOnlyList<AppStoreItem>> GetAppsByCategoryAsync(AppCategory category, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppStoreItem>>([Item(category.ToString()), Item("Another app")]);
        public Task<IReadOnlyList<AppStoreItem>> SearchAppsAsync(string query, CancellationToken cancellationToken = default) => Search(query);
        public Task<IReadOnlyList<AppStoreItem>> GetInstalledAppsAsync(CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<AppStoreItem>>([]);
        public Task<bool> InstallAppAsync(string packageName, Action<double>? progressCallback = null, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> UninstallAppAsync(string packageName, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> IsPackageInstalledAsync(string packageName, CancellationToken cancellationToken = default) => Task.FromResult(false);
    }

    private sealed class WirelessStub : IWirelessNetworkService
    {
        public string? Password { get; private set; }
        public int Calls { get; private set; }
        public CancellationToken Token { get; private set; }
        public Func<CancellationToken, Task<bool>> Connect { get; set; } = _ => Task.FromResult(false);
        public Task<bool> ConnectToNetworkAsync(string ssid, string? password = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            Password = password;
            Token = cancellationToken;
            return Connect(cancellationToken);
        }
        public Task<WirelessNetworkStatus> GetNetworksAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<WirelessRadioStatus> GetRadioStatusAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> DisconnectFromNetworkAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ForgetNetworkAsync(string ssid, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> SetEnabledAsync(bool enabled, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class UnavailablePackagesRunner : ICommandRunner
    {
        public Task<CommandResult> RunAsync(string command, IReadOnlyList<string> arguments, TimeSpan timeout,
            CancellationToken cancellationToken = default) => Task.FromResult(CommandResult.Failed("Not available", 1));
    }
}
