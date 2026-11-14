using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DaisyOS.Shell.Apps.Files.Picker;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Views.Dialogs;

namespace DaisyOS.Shell.Apps.AppInstaller;

public partial class AppInstallerWindow : Window
{
    private readonly SystemAppWindowChrome _chrome;
    private readonly bool _ownsViewModel;

    public AppInstallerWindow() : this(new AppInstallerViewModel()) { }

    internal AppInstallerWindow(AppInstallerViewModel viewModel) : this(viewModel, ownsViewModel: true) { }

    internal AppInstallerWindow(AppInstallerViewModel viewModel, bool ownsViewModel)
    {
        _ownsViewModel = ownsViewModel;
        InitializeComponent();
        DataContext = viewModel;
        _chrome = new SystemAppWindowChrome(this,
            this.FindControl<AppWindowTitleBar>("AppTitleBar")!,
            this.FindControl<Grid>("MainPaneLayout")!, normalRowDefinitions: "Auto,*");
        DragDrop.SetAllowDrop(this, true);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        Opened += (_, _) => _ = viewModel.InitializeAsync();
    }

    private AppInstallerViewModel? ViewModel => DataContext as AppInstallerViewModel;

    public Task OpenFileAsync(string path) => ViewModel?.SelectFileAsync(path) ?? Task.CompletedTask;

    public async Task InstallFileAsync(string path)
    {
        await OpenFileAsync(path);
        if (ViewModel is { } vm)
        {
            if (vm.SelectedPlan is { } plan && !await new ApplicationTransactionDialog(plan).ConfirmAsync(this)) return;
            await vm.InstallAsync();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _chrome.Dispose();
        if (_ownsViewModel) ViewModel?.Dispose();
        base.OnClosed(e);
    }

    private async void OnChooseFileClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is not App app) return;
        var files = await app.PickFilesAsync(this, new DaisyFilePickerOptions(
            Loc.Get("Choose an application package"), false, false, [".AppImage", ".appimage", ".flatpakref", ".flatpak", ".deb", ".rpm", ".zst", ".xz", ".gz"]));
        if (files?.FirstOrDefault() is { } path) await OpenFileAsync(path);
    }

    private async void OnInstallClicked(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is not { } viewModel) return;
        if (viewModel.SelectedPlan is { } plan && !await new ApplicationTransactionDialog(plan).ConfirmAsync(this)) return;
        await viewModel.InstallAsync();
    }

    private void OnQuitClicked(object? sender, RoutedEventArgs e) => Close();

    private async void OnBrowseDestinationClicked(object? sender, RoutedEventArgs e)
    {
        if (Application.Current is App app)
        {
            var folders = await app.PickFilesAsync(this, new DaisyFilePickerOptions(
                Title: Loc.Get("Choose an installation folder"),
                AllowMultiple: false,
                AllowFolderContents: true,
                AllowedExtensions: Array.Empty<string>(),
                SelectDirectory: true));
            if (folders?.FirstOrDefault() is { } folder && ViewModel is { } vm)
            {
                vm.CustomDestinationDirectory = folder;
            }
        }
        else if (StorageProvider.CanPickFolder)
        {
            var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
            {
                Title = Loc.Get("Choose an installation folder"),
                AllowMultiple = false
            });
            if (folders.Count > 0 && folders[0].TryGetLocalPath() is { } folderPath && ViewModel is { } vm)
            {
                vm.CustomDestinationDirectory = folderPath;
            }
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private void OnLaunchSelectedClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.LaunchSelected();
        Close();
    }

    private void OnClearSelectionClicked(object? sender, RoutedEventArgs e)
    {
        ViewModel?.ClearSelection();
    }

    private void OnLaunchClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: InstalledAppImageViewModel item })
            ViewModel?.Launch(item);
    }

    private async void OnRemoveClicked(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button { DataContext: InstalledAppImageViewModel item } || ViewModel is not { } viewModel)
            return;
        var confirmed = await new ConfirmationDialog(
            Loc.Get("Remove AppImage"),
            Loc.Format("Remove {0}?", item.Name),
            Loc.Get("The application will be removed. The original AppImage file you selected is not deleted."),
            Loc.Get("Remove"),
            Loc.Get("Keep application")).ShowDialog<bool>(this);
        if (confirmed) await viewModel.RemoveAsync(item);
    }

    private void OnDragOver(object? sender, DragEventArgs e)
    {
        e.DragEffects = e.DataTransfer.Contains(DataFormat.File)
            ? DragDropEffects.Copy
            : DragDropEffects.None;
        e.Handled = true;
    }

    private async void OnDrop(object? sender, DragEventArgs e)
    {
        var files = e.DataTransfer.TryGetFiles();
        var path = files?
            .Select(file => file.TryGetLocalPath())
            .FirstOrDefault(candidate => !string.IsNullOrWhiteSpace(candidate) &&
                DaisyOS.System.Applications.ApplicationManagementService.SupportsFile(candidate));
        if (path is not null) await OpenFileAsync(path);
        e.Handled = true;
    }
}