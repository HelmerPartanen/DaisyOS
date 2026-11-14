using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.ViewModels;
using DaisyOS.System.AppImage;
using DaisyOS.System.Applications;
using DaisyOS.Core.Models;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.AppInstaller;

public sealed class AppInstallerViewModel : INotifyPropertyChanged, IDisposable
{
    private readonly IAppImageInstallerService _service;
    private readonly SafeProcessLauncher _launcher;
    private AppImageInspection? _selected;
    private ApplicationTransactionPlan? _packagePlan;
    private readonly ApplicationManagementService _management;
    private readonly CancellationTokenSource _lifetime = new();
    public ApplicationTransactionPlan? SelectedPlan => _packagePlan;
    public bool CanChooseDestination => _packagePlan is null;
    public bool CanChooseCompatibilityImage => _packagePlan?.Application.Identity.Backend == ApplicationBackend.Container;
    public string[] CompatibilityImages => CompatibilityApplications.Images.Where(image => SelectedPath.EndsWith(".deb", StringComparison.OrdinalIgnoreCase) == image.Contains("ubuntu", StringComparison.Ordinal)).ToArray();
    public string? CompatibilityImage
    {
        get => _packagePlan?.Application.Image;
        set { if (CanChooseCompatibilityImage && !_isBusy && value is not null && value != CompatibilityImage) _ = ChangeCompatibilityImageAsync(value); }
    }
    private async Task ChangeCompatibilityImageAsync(string image)
    {
        if (_packagePlan is null) return;
        SetBusy(true);
        try { _packagePlan = await _management.InspectFileAsync(_packagePlan.Application.SourcePath, image, _lifetime.Token); PublishSelection(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { SetError(ex.Message); }
        finally { SetBusy(false); }
    }
    public bool CanInstall => !_isBusy && (_packagePlan?.CanExecute ?? _selected is not null);
    private Bitmap? _selectedIcon;
    private bool _isSelectedInstalled;
    private bool _justInstalled;
    private InstalledAppImageViewModel? _installedMatch;
    private bool _isBusy;
    private string _message = "";
    private bool _messageIsError;
    private string? _customDestinationDirectory;
    private bool _disposed;

    public AppInstallerViewModel(IAppImageInstallerService? service = null, SafeProcessLauncher? launcher = null, string? dataHome = null)
    {
        _service = service ?? new AppImageService();
        _launcher = launcher ?? new SafeProcessLauncher();
        _management = new(images: _service, dataHome: dataHome);
    }

    public ObservableCollection<InstalledAppImageViewModel> Installed { get; } = [];
    public bool HasSelection => _selected is not null || _packagePlan is not null;
    public string SelectedName => _packagePlan?.Application.Name ?? _selected?.Name ?? "";
    public string SelectedPath => _packagePlan?.Application.SourcePath ?? _selected?.SourcePath ?? "";
    public string SelectedComment => _packagePlan is not null ? _packagePlan.Error.Length > 0 ? _packagePlan.Error : Loc.Get("This application will be managed by DaisyOS.") : _selected?.Comment ?? "";
    public bool HasSelectedComment => !string.IsNullOrWhiteSpace(SelectedComment);
    public Bitmap? SelectedIcon => _selectedIcon;
    public bool HasSelectedIcon => _selectedIcon is not null;
    public bool IsSelectedInstalled => _isSelectedInstalled;
    public bool JustInstalled => _justInstalled;
    public InstalledAppImageViewModel? InstalledMatch => _installedMatch;
    public bool ShowOpenButton => _packagePlan is null && HasSelection && (JustInstalled || IsSelectedInstalled);
    public bool ShowInstallButton => HasSelection && !JustInstalled;
    public bool ShowFirstTimeInstallButton => HasSelection && !IsSelectedInstalled && !JustInstalled;
    public bool ShowReinstallButton => HasSelection && IsSelectedInstalled && !JustInstalled;
    public string InstallButtonText => IsSelectedInstalled ? Loc.Get("Reinstall") : Loc.Get("Install");

    public string? CustomDestinationDirectory
    {
        get => _customDestinationDirectory;
        set
        {
            if (_customDestinationDirectory != value)
            {
                _customDestinationDirectory = value;
                OnPropertyChanged(nameof(CustomDestinationDirectory));
                OnPropertyChanged(nameof(InstallDestinationPath));
            }
        }
    }

    public string InstallDestinationPath
    {
        get
        {
            if (_packagePlan is not null) return _packagePlan.Application.Identity.Backend == ApplicationBackend.Container ? Loc.Get("Private compatibility environment") : Loc.Get("Managed by the package system");
            if (!string.IsNullOrWhiteSpace(_customDestinationDirectory))
                return _customDestinationDirectory;
            if (_installedMatch is not null)
                return Path.GetDirectoryName(_installedMatch.AppImagePath) ?? "";
            if (_selected is not null)
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, ".local", "share", "daisyos", "appimages", _selected.DesktopId);
            }
            return "";
        }
    }

    public string SelectedDetails
    {
        get
        {
            if (_packagePlan is not null) return $"{_packagePlan.Application.Identity.Backend} • {_packagePlan.Application.Version} • {_packagePlan.Application.Architecture}";
            if (_selected is null) return "";
            var size = FormatSize(_selected.SizeBytes);
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(_selected.Version))
                parts.Add(Loc.Get("Version") + " " + _selected.Version);
            if (!string.IsNullOrWhiteSpace(_selected.Architecture))
                parts.Add(_selected.Architecture);
            parts.Add(size);
            return string.Join(" • ", parts);
        }
    }

    public bool IsBusy => _isBusy;
    public bool HasMessage => !string.IsNullOrEmpty(_message);
    public string Message => _message;
    public bool IsMessageError => _messageIsError;
    public bool HasInstalled => Installed.Count > 0;
    public bool ShowInstalledEmpty => !_isBusy && Installed.Count == 0;
    public event PropertyChangedEventHandler? PropertyChanged;

    private Task? _initializationTask;

    public Task InitializeAsync()
    {
        _initializationTask ??= DoInitializeAsync();
        return _initializationTask;
    }

    private async Task DoInitializeAsync()
    {
        SetBusy(true);
        ClearMessage();
        try
        {
            await ReloadInstalledAsync(showError: true);
            UpdateInstalledStatus();
        }
        finally { SetBusy(false); }
    }

    public async Task SelectFileAsync(string path)
    {
        if (_initializationTask is not null)
            await _initializationTask;
        else
            await InitializeAsync();

        if (_isBusy) return;
        SetBusy(true);
        ClearMessage();
        try
        {
            _packagePlan = null; _selected = null;
            if (!path.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
            {
                _packagePlan = await _management.InspectFileAsync(path, token: _lifetime.Token);
                _justInstalled = false; _isSelectedInstalled = false; UpdateSelectedIcon(); PublishSelection();
                if (!_packagePlan.CanExecute) SetError(_packagePlan.Error);
                return;
            }
            _selected = await _service.InspectAsync(path);
            _justInstalled = false;
            UpdateSelectedIcon();
            UpdateInstalledStatus();
            PublishSelection();
        }
        catch (AppImageException ex)
        {
            ClearSelection();
            SetError(Loc.Get(ex.Message));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            ClearSelection();
            SetError(Loc.Get("Couldn’t read the selected application package. Try again."));
        }
        finally { SetBusy(false); }
    }

    public async Task InstallAsync()
    {
        if (_isBusy) return;
        if (_packagePlan is not null)
        {
            SetBusy(true); ClearMessage();
            try
            {
                var result = await _management.ExecuteAsync(_packagePlan, progress: new Progress<ApplicationProgress>(p => SetMessage(Loc.Get(p.Stage), false)), token: _lifetime.Token);
                if (result.RequiresReview) _packagePlan = await _management.InspectFileAsync(_packagePlan.Application.SourcePath, token: _lifetime.Token);
                _justInstalled = result.Succeeded;
                SetMessage(Loc.Get(result.Message) + (result.Details.Length > 0 ? "\n" + result.Details : ""), !result.Succeeded);
                if (result.Succeeded) (Avalonia.Application.Current as App)?.NotifyApplicationRestored(_packagePlan.Application.DesktopIds.FirstOrDefault() ?? _packagePlan.Application.Identity.PackageId);
                PublishSelection();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            { SetError(ex.Message); }
            finally { SetBusy(false); }
            return;
        }
        if (_selected is null) return;
        var path = _selected.SourcePath;
        SetBusy(true);
        ClearMessage();
        try
        {
            var plan = await _management.InspectFileAsync(path, token: _lifetime.Token);
            if (_customDestinationDirectory is not null) plan = plan with { Application = plan.Application with { DataDirectory = _customDestinationDirectory } };
            var result = await _management.ExecuteAsync(plan, token: _lifetime.Token);
            if (!result.Succeeded) throw new IOException(result.Message + " " + result.Details);
            var installation = (await _service.GetInstalledAsync()).First(item => item.DesktopId == (result.Application?.Identity.PackageId ?? plan.Application.Identity.PackageId));
            (Avalonia.Application.Current as App)?.NotifyApplicationRestored(installation.DesktopId);
            await ReloadInstalledAsync(showError: false);
            _justInstalled = true;
            _isSelectedInstalled = true;
            _installedMatch = Installed.FirstOrDefault(item => item.DesktopId == installation.DesktopId)
                ?? new InstalledAppImageViewModel(installation);
            SetMessage(Loc.Get("AppImage installed."), isError: false);
            OnPropertyChanged(nameof(IsSelectedInstalled));
            OnPropertyChanged(nameof(JustInstalled));
            OnPropertyChanged(nameof(InstalledMatch));
            OnPropertyChanged(nameof(ShowOpenButton));
            OnPropertyChanged(nameof(ShowInstallButton));
            OnPropertyChanged(nameof(ShowFirstTimeInstallButton));
            OnPropertyChanged(nameof(ShowReinstallButton));
            OnPropertyChanged(nameof(InstallButtonText));
            OnPropertyChanged(nameof(InstallDestinationPath));
        }
        catch (AppImageException ex) { SetError(Loc.Get(ex.Message)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(Loc.Get("The AppImage could not be installed. Try again."));
        }
        finally { SetBusy(false); }
    }

    public void LaunchSelected()
    {
        if (_installedMatch is not null)
        {
            Launch(_installedMatch);
            return;
        }

        if (_selected is not null)
        {
            var match = Installed.FirstOrDefault(item => item.DesktopId == _selected.DesktopId);
            if (match is not null) Launch(match);
        }
    }

    public void ClearSelection()
    {
        _selected = null;
        _packagePlan = null;
        _customDestinationDirectory = null;
        _selectedIcon?.Dispose();
        _selectedIcon = null;
        _isSelectedInstalled = false;
        _justInstalled = false;
        _installedMatch = null;
        ClearMessage();
        PublishSelection();
    }

    public async Task RemoveAsync(InstalledAppImageViewModel item)
    {
        if (_isBusy) return;
        SetBusy(true);
        ClearMessage();
        try
        {
            var application = new InstalledApplication(new(ApplicationBackend.AppImage, item.DesktopId), item.Name)
            { DesktopIds = [item.DesktopId], OwnedLaunchers = [item.Installation.DesktopFilePath], OwnedIcons = [item.Installation.IconPath], OwnedCommands = ApplicationIntegration.Commands([item.Installation.DesktopFilePath]) };
            var removal = await _management.ExecuteAsync(await _management.PrepareUninstallAsync(application), token: _lifetime.Token);
            if (!removal.Succeeded) throw new IOException(removal.Message);
            (Avalonia.Application.Current as App)?.NotifyApplicationRemoved(item.DesktopId);
            await ReloadInstalledAsync(showError: false);
            if (_selected?.DesktopId == item.DesktopId)
            {
                _isSelectedInstalled = false;
                _justInstalled = false;
                _installedMatch = null;
                OnPropertyChanged(nameof(IsSelectedInstalled));
                OnPropertyChanged(nameof(JustInstalled));
                OnPropertyChanged(nameof(InstalledMatch));
                OnPropertyChanged(nameof(ShowOpenButton));
                OnPropertyChanged(nameof(ShowInstallButton));
                OnPropertyChanged(nameof(ShowFirstTimeInstallButton));
                OnPropertyChanged(nameof(ShowReinstallButton));
                OnPropertyChanged(nameof(InstallButtonText));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            SetError(Loc.Get("The AppImage could not be removed. Try again."));
        }
        finally { SetBusy(false); }
    }

    public void Launch(InstalledAppImageViewModel item)
    {
        AppImageService.MakeExecutable(item.AppImagePath);
        var result = _launcher.Launch(item.AppImagePath);
        if (!result.Succeeded)
        {
            var message = !string.IsNullOrWhiteSpace(result.Message)
                ? result.Message
                : Loc.Get("The application could not be opened. Try again.");
            SetError(message);
        }
    }

    private void UpdateSelectedIcon()
    {
        _selectedIcon?.Dispose();
        _selectedIcon = null;
        if (_selected?.IconData is { Length: > 0 } data)
        {
            try
            {
                using var stream = new MemoryStream(data);
                if (string.Equals(_selected.IconExtension, ".svg", StringComparison.OrdinalIgnoreCase))
                {
                    _selectedIcon = DesktopItemLoader.LoadSvgBitmap(stream, 256);
                }
                else
                {
                    _selectedIcon = new Bitmap(stream);
                }
            }
            catch
            {
                _selectedIcon = null;
            }
        }
        OnPropertyChanged(nameof(SelectedIcon));
        OnPropertyChanged(nameof(HasSelectedIcon));
    }

    private void UpdateInstalledStatus()
    {
        _installedMatch = null;
        _isSelectedInstalled = false;
        if (_selected is not null)
        {
            _installedMatch = Installed.FirstOrDefault(item =>
                string.Equals(item.DesktopId, _selected.DesktopId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(item.Name, _selected.Name, StringComparison.OrdinalIgnoreCase));
            _isSelectedInstalled = _installedMatch is not null;
            if (_isSelectedInstalled && _installedMatch is not null && string.IsNullOrWhiteSpace(_customDestinationDirectory))
            {
                _customDestinationDirectory = Path.GetDirectoryName(_installedMatch.AppImagePath);
            }
        }
        OnPropertyChanged(nameof(IsSelectedInstalled));
        OnPropertyChanged(nameof(InstalledMatch));
        OnPropertyChanged(nameof(InstallDestinationPath));
        OnPropertyChanged(nameof(ShowOpenButton));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ShowFirstTimeInstallButton));
        OnPropertyChanged(nameof(ShowReinstallButton));
        OnPropertyChanged(nameof(InstallButtonText));
    }

    private async Task ReloadInstalledAsync(bool showError)
    {
        try
        {
            var installed = await _service.GetInstalledAsync();
            if (_disposed) return;
            Installed.Clear();
            foreach (var installation in installed) Installed.Add(new InstalledAppImageViewModel(installation));
            PublishInstalled();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            if (showError) SetError(Loc.Get("Installed AppImages could not be loaded."));
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1_000_000_000) return (bytes / 1_000_000_000.0).ToString("F1") + " GB";
        if (bytes >= 1_000_000) return (bytes / 1_000_000.0).ToString("F1") + " MB";
        return Math.Max(1, bytes / 1000).ToString() + " KB";
    }

    private void SetBusy(bool value)
    {
        if (_isBusy == value) return;
        _isBusy = value;
        OnPropertyChanged(nameof(IsBusy)); OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(ShowInstalledEmpty));
    }

    private void SetMessage(string message, bool isError)
    {
        _message = message;
        _messageIsError = isError;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(IsMessageError));
    }

    private void SetError(string message) => SetMessage(message, isError: true);

    private void ClearMessage()
    {
        if (_message.Length == 0) return;
        _message = "";
        _messageIsError = false;
        OnPropertyChanged(nameof(Message));
        OnPropertyChanged(nameof(HasMessage));
        OnPropertyChanged(nameof(IsMessageError));
    }

    private void PublishSelection()
    {
        OnPropertyChanged(nameof(CanChooseCompatibilityImage)); OnPropertyChanged(nameof(CompatibilityImages)); OnPropertyChanged(nameof(CompatibilityImage)); OnPropertyChanged(nameof(SelectedPlan)); OnPropertyChanged(nameof(CanChooseDestination)); OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(HasSelection));
        OnPropertyChanged(nameof(SelectedName));
        OnPropertyChanged(nameof(SelectedPath));
        OnPropertyChanged(nameof(SelectedDetails));
        OnPropertyChanged(nameof(SelectedComment));
        OnPropertyChanged(nameof(HasSelectedComment));
        OnPropertyChanged(nameof(InstallDestinationPath));
        OnPropertyChanged(nameof(ShowOpenButton));
        OnPropertyChanged(nameof(ShowInstallButton));
        OnPropertyChanged(nameof(ShowFirstTimeInstallButton));
        OnPropertyChanged(nameof(ShowReinstallButton));
        OnPropertyChanged(nameof(InstallButtonText));
    }

    private void PublishInstalled()
    {
        OnPropertyChanged(nameof(HasInstalled));
        OnPropertyChanged(nameof(ShowInstalledEmpty));
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

    public void Dispose()
    {
        _disposed = true;
        _lifetime.Cancel(); _lifetime.Dispose();
        _selectedIcon?.Dispose();
        _selectedIcon = null;
    }
}

public sealed class InstalledAppImageViewModel
{
    public InstalledAppImageViewModel(AppImageInstallation installation) => Installation = installation;

    public AppImageInstallation Installation { get; }
    public string DesktopId => Installation.DesktopId;
    public string Name => Installation.Name;
    public string AppImagePath => Installation.AppImagePath;
    public string IconPath => Installation.IconPath;
    public IImage? IconImage => !string.IsNullOrWhiteSpace(IconPath) ? DesktopItemLoader.LoadBitmapSafe(IconPath) : null;
    public bool HasIcon => IconImage is not null;
    public string Details => string.IsNullOrWhiteSpace(Installation.Version)
        ? Path.GetFileName(Installation.AppImagePath)
        : Loc.Get("Version") + " " + Installation.Version + " • " + Path.GetFileName(Installation.AppImagePath);
    public string LaunchAccessibilityLabel => Loc.Get("Open") + " " + Name;
    public string RemoveAccessibilityLabel => Loc.Get("Remove") + " " + Name;
}