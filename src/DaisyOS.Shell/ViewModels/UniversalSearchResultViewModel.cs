using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DaisyOS.Core.Helpers;
using DaisyOS.Core.Search;

namespace DaisyOS.Shell.ViewModels;

public sealed class UniversalSearchActionViewModel(string label, Func<Task> execute)
{
    public string Label { get; } = Loc.Get(label);
    public AsyncRelayCommand Command { get; } = new(execute);
}

public sealed class UniversalSearchResultViewModel : ObservableObject
{
    private readonly Func<SearchAction, Task> _dispatch;
    public SearchResult Result { get; }
    public string Title => Result.Type is SearchResultType.Setting or SearchResultType.Action ? Loc.Get(Result.Title) : Result.Title;
    public string Subtitle => Result.Subtitle ?? Result.Type.ToString();
    public string TypeLabel => Loc.Get(Result.Type.ToString());
    public SettingSearchResultViewModel? Setting { get; }
    public bool HasSetting => Setting is not null && !(Setting.IsUnavailable && Result.Actions.Count > 0);
    public bool CanActivate => !HasSetting && Result.IsEnabled && Result.Actions.Count > 0 && !IsPending;
    public LauncherItemViewModel? Application { get; }
    private bool _showDivider;
    private bool _isKeyboardSelected;
    public bool ShowDivider { get => _showDivider; set => SetProperty(ref _showDivider, value); }
    public bool IsKeyboardSelected { get => _isKeyboardSelected; set => SetProperty(ref _isKeyboardSelected, value); }
    public bool ShowSubtitle => Result.Payload is FileSearchPayload or WindowSearchPayload;
    public string PrimaryActionLabel => Loc.Get(Result.Type switch
    {
        SearchResultType.Calculator => "Copy",
        SearchResultType.Window => "Switch",
        SearchResultType.Action => "Run",
        SearchResultType.Setting => "Open Settings",
        _ => "Open"
    });
    public string PrimaryActionIcon => Result.Type == SearchResultType.Calculator ? "copy" : "arrow-up-right";
    public string IconName => Result.Payload switch
    {
        SettingSearchPayload setting when setting.SettingId.Contains("theme", StringComparison.Ordinal) => "moon",
        SettingSearchPayload setting when setting.SettingId.Contains("accent", StringComparison.Ordinal) => "palette",
        SettingSearchPayload setting when setting.SettingId.Contains("wifi", StringComparison.Ordinal) => "wifi-high",
        SettingSearchPayload setting when setting.SettingId.Contains("mute", StringComparison.Ordinal) => "speaker-slash",
        SettingSearchPayload => "sliders-horizontal",
        SettingsPageSearchPayload page when page.Setting == "Theme" => "moon",
        SettingsPageSearchPayload => "gear-six",
        FileSearchPayload { IsDirectory: true } => "folder",
        FileSearchPayload file when global::System.IO.Path.GetExtension(file.Path).ToLowerInvariant() is ".svg" or ".png" or ".jpg" or ".jpeg" or ".webp" or ".gif" or ".avif" => "image",
        FileSearchPayload => "file",
        WindowSearchPayload => "sidebar-simple",
        CalculatorSearchPayload => "plus",
        ApplicationSearchPayload => "squares-four",
        _ => "arrow-square-out"
    };
    public IReadOnlyList<UniversalSearchActionViewModel> Actions { get; }
    private string? _error;
    private bool _pending;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string? Error { get => _error; private set { if (SetProperty(ref _error, value)) OnPropertyChanged(nameof(HasError)); } }
    public bool IsPending { get => _pending; private set { if (SetProperty(ref _pending, value)) OnPropertyChanged(nameof(CanActivate)); } }
    public UniversalSearchResultViewModel(SearchResult result, SettingSearchResultViewModel? setting, Func<SearchAction, Task> dispatch, LauncherItemViewModel? application = null)
    {
        Result = result; Setting = setting; _dispatch = dispatch; Application = application;
        if (setting is not null) setting.PropertyChanged += (_, change) =>
        {
            if (change.PropertyName == nameof(SettingSearchResultViewModel.IsUnavailable))
            { OnPropertyChanged(nameof(HasSetting)); OnPropertyChanged(nameof(CanActivate)); }
        };
        Actions = result.Actions.Select(action => new UniversalSearchActionViewModel(action.Label, () => ExecuteAsync(action))).ToArray();
    }
    public Task ActivateAsync() => CanActivate ? ExecuteAsync(Result.Actions[0]) : Task.CompletedTask;
    public async Task ExecuteAsync(SearchAction action)
    {
        if (IsPending) return;
        IsPending = true; Error = null;
        try { await _dispatch(action); }
        catch (Exception) { Error = Loc.Get("This action could not be completed."); }
        finally { IsPending = false; }
    }
}
