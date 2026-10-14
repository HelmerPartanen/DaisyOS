using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using DaisyOS.Core.Helpers;
using DaisyOS.Core.Models;

namespace DaisyOS.Installer;

public sealed partial class InstallerViewModel : ObservableObject, IDisposable
{
    public const string RequiredConfirmation = "INSTALL DAISYOS";
    private readonly IInstallerBackend _backend;
    private readonly CancellationTokenSource _lifetime = new();
    private InstallerStep _step;
    private string _language = "English (United States)";
    private string _keyboard = "English (US)";
    private string _timeZone = "Europe/Helsinki";
    private string _fullName = string.Empty;
    private string _userName = string.Empty;
    private string _hostName = BrandInfo.DefaultHostName;
    private string _password = string.Empty;
    private string _passwordConfirmation = string.Empty;
    private InstallerDisk? _selectedDisk;
    private bool _encrypt;
    private bool _createRecoveryPartition = true;
    private bool _vmWarningAccepted;
    private string _confirmationText = string.Empty;
    private string _validationMessage = string.Empty;
    private int _progressPercentage;
    private string _progressStage = "Waiting";
    private string _progressDetail = string.Empty;
    private bool _hasInstallError;

    public InstallerViewModel(IInstallerBackend backend)
    {
        _backend = backend;
        Languages = ["English (United States)", "English (United Kingdom)", "Finnish", "German"];
        Keyboards = ["English (US)", "English (UK)", "Finnish", "German"];
        TimeZones = ["Europe/Helsinki", "Europe/London", "Europe/Berlin", "America/New_York", "Asia/Tokyo"];
        Disks =
        [
            new("mock-vda", "Virtual disk (example)", "64 GB", "Example device only — no real disk is connected"),
            new("mock-nvme", "NVMe drive (example)", "512 GB", "Example device only — no real disk is connected")
        ];
        _selectedDisk = Disks[0];
        NextCommand = new RelayCommand(_ => Next(), _ => CanGoNext);
        BackCommand = new RelayCommand(_ => Back(), _ => CanGoBack);
        InstallCommand = new AsyncRelayCommand((_, cancellationToken) => InstallAsync(cancellationToken), _ => CanInstall);
    }

    public ObservableCollection<string> Languages { get; }
    public ObservableCollection<string> Keyboards { get; }
    public ObservableCollection<string> TimeZones { get; }
    public ObservableCollection<InstallerDisk> Disks { get; }
    public RelayCommand NextCommand { get; }
    public RelayCommand BackCommand { get; }
    public AsyncRelayCommand InstallCommand { get; }

    public InstallerStep Step { get => _step; private set { if (SetProperty(ref _step, value)) RefreshStepState(); } }
    public string Language { get => _language; set => SetAndRefresh(ref _language, value); }
    public string Keyboard { get => _keyboard; set => SetAndRefresh(ref _keyboard, value); }
    public string TimeZone { get => _timeZone; set => SetAndRefresh(ref _timeZone, value); }
    public string FullName { get => _fullName; set { if (SetAndRefresh(ref _fullName, value) && string.IsNullOrWhiteSpace(UserName)) UserName = SuggestUserName(value); } }
    public string UserName { get => _userName; set => SetAndRefresh(ref _userName, value.Trim().ToLowerInvariant()); }
    public string HostName { get => _hostName; set => SetAndRefresh(ref _hostName, value.Trim().ToLowerInvariant()); }
    public string Password { get => _password; set => SetAndRefresh(ref _password, value); }
    public string PasswordConfirmation { get => _passwordConfirmation; set => SetAndRefresh(ref _passwordConfirmation, value); }
    public InstallerDisk? SelectedDisk { get => _selectedDisk; set => SetAndRefresh(ref _selectedDisk, value); }
    public bool Encrypt { get => _encrypt; set => SetAndRefresh(ref _encrypt, value); }
    public bool CreateRecoveryPartition { get => _createRecoveryPartition; set => SetAndRefresh(ref _createRecoveryPartition, value); }
    public bool VmWarningAccepted { get => _vmWarningAccepted; set => SetAndRefresh(ref _vmWarningAccepted, value); }
    public string ConfirmationText { get => _confirmationText; set => SetAndRefresh(ref _confirmationText, value); }
    public string ValidationMessage { get => _validationMessage; private set => SetProperty(ref _validationMessage, value); }
    public int ProgressPercentage { get => _progressPercentage; private set => SetProperty(ref _progressPercentage, value); }
    public string ProgressStage { get => _progressStage; private set => SetProperty(ref _progressStage, value); }
    public string ProgressDetail { get => _progressDetail; private set => SetProperty(ref _progressDetail, value); }
    public bool HasInstallError { get => _hasInstallError; private set => SetProperty(ref _hasInstallError, value); }

    public bool IsWelcome => Step == InstallerStep.Welcome;
    public bool IsRegion => Step == InstallerStep.Region;
    public bool IsUser => Step == InstallerStep.User;
    public bool IsDisk => Step == InstallerStep.Disk;
    public bool IsSummary => Step == InstallerStep.Summary;
    public bool IsConfirm => Step == InstallerStep.Confirm;
    public bool IsInstalling => Step == InstallerStep.Installing;
    public bool IsComplete => Step == InstallerStep.Complete;
    public bool ShowsNext => Step is >= InstallerStep.Welcome and <= InstallerStep.Summary;
    public bool CanGoBack => Step is > InstallerStep.Welcome and < InstallerStep.Installing;
    public bool CanGoNext => Step switch
    {
        InstallerStep.Welcome => VmWarningAccepted,
        InstallerStep.Region => HasRegionDetails,
        InstallerStep.User => HasValidUserDetails,
        InstallerStep.Disk => SelectedDisk is not null,
        InstallerStep.Summary => true,
        _ => false
    };
    public bool CanInstall => IsConfirm && ConfirmationText == RequiredConfirmation && !_backend.CanModifyDisks;
    public string StepLabel => Step switch
    {
        InstallerStep.Welcome => "Welcome",
        InstallerStep.Region => "Region",
        InstallerStep.User => "Your account",
        InstallerStep.Disk => "Storage",
        InstallerStep.Summary => "Review",
        InstallerStep.Confirm => "Confirm",
        InstallerStep.Installing => "Installing",
        _ => "Finished"
    };
    public string DiskSummary => SelectedDisk is null ? "No disk selected" : $"{SelectedDisk.DisplayName} (mock only)";
    public string EncryptionSummary => Encrypt ? "Encryption requested" : "No encryption";
    public string RecoverySummary => CreateRecoveryPartition ? "Recovery partition requested" : "No recovery partition";
    public bool HasRegionDetails => !string.IsNullOrWhiteSpace(Language) && !string.IsNullOrWhiteSpace(Keyboard) && !string.IsNullOrWhiteSpace(TimeZone);
    public bool HasValidUserDetails => ValidateUserDetails() is null;

    public void Next()
    {
        ValidationMessage = ValidateCurrentStep() ?? string.Empty;
        if (!CanGoNext || ValidationMessage.Length > 0) return;
        Step++;
    }

    public void Back()
    {
        if (!CanGoBack) return;
        ValidationMessage = string.Empty;
        Step--;
    }

    public async Task InstallAsync(CancellationToken cancellationToken = default)
    {
        if (!CanInstall || SelectedDisk is null) return;
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        Step = InstallerStep.Installing;
        HasInstallError = false;
        try
        {
            var plan = new InstallerPlan(Language, Keyboard, TimeZone, FullName.Trim(), UserName, HostName, SelectedDisk, Encrypt, CreateRecoveryPartition);
            var progress = new Progress<InstallerProgress>(item =>
            {
                ProgressPercentage = item.Percentage;
                ProgressStage = item.Stage;
                ProgressDetail = item.Detail;
            });
            await _backend.InstallAsync(plan, progress, linked.Token);
            Step = InstallerStep.Complete;
        }
        catch (OperationCanceledException) when (linked.IsCancellationRequested) { }
        catch
        {
            HasInstallError = true;
            ProgressStage = "Installation couldn’t continue";
            ProgressDetail = "Nothing was changed. Close the installer and try again.";
        }
    }

    private string? ValidateCurrentStep() => Step switch
    {
        InstallerStep.Welcome when !VmWarningAccepted => "Confirm that this prototype is for virtual-machine testing.",
        InstallerStep.Region when !HasRegionDetails => "Choose a language, keyboard, and time zone.",
        InstallerStep.User => ValidateUserDetails(),
        InstallerStep.Disk when SelectedDisk is null => "Choose an example disk.",
        _ => null
    };

    private string? ValidateUserDetails()
    {
        if (string.IsNullOrWhiteSpace(FullName)) return "Enter your name.";
        if (!UserNamePattern().IsMatch(UserName)) return "Use lowercase letters, numbers, hyphens, or underscores for the user name.";
        if (!HostNamePattern().IsMatch(HostName)) return "Use letters, numbers, or hyphens for the device name.";
        if (Password.Length < 8) return "Use at least 8 characters for the password.";
        if (Password != PasswordConfirmation) return "The passwords don’t match.";
        return null;
    }

    private bool SetAndRefresh<T>(ref T field, T value)
    {
        if (!SetProperty(ref field, value)) return false;
        ValidationMessage = string.Empty;
        OnPropertyChanged(nameof(CanGoNext));
        OnPropertyChanged(nameof(CanInstall));
        OnPropertyChanged(nameof(DiskSummary));
        OnPropertyChanged(nameof(EncryptionSummary));
        OnPropertyChanged(nameof(RecoverySummary));
        NextCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
        return true;
    }

    private void RefreshStepState()
    {
        foreach (var property in new[] { nameof(IsWelcome), nameof(IsRegion), nameof(IsUser), nameof(IsDisk), nameof(IsSummary), nameof(IsConfirm), nameof(IsInstalling), nameof(IsComplete), nameof(ShowsNext), nameof(CanGoBack), nameof(CanGoNext), nameof(CanInstall), nameof(StepLabel) })
            OnPropertyChanged(property);
        NextCommand.RaiseCanExecuteChanged();
        BackCommand.RaiseCanExecuteChanged();
        InstallCommand.RaiseCanExecuteChanged();
    }

    private static string SuggestUserName(string name) =>
        Regex.Replace(name.Trim().ToLowerInvariant().Replace(' ', '-'), "[^a-z0-9_-]", string.Empty);

    public void Dispose()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        InstallCommand.Dispose();
    }

    [GeneratedRegex("^[a-z_][a-z0-9_-]{0,31}$")]
    private static partial Regex UserNamePattern();

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?$")]
    private static partial Regex HostNamePattern();
}
