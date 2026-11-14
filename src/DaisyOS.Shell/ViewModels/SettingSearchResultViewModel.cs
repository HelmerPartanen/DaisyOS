using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DaisyOS.Core.Helpers;
using DaisyOS.Core.Search;

namespace DaisyOS.Shell.ViewModels;

public sealed class SettingSearchResultViewModel : ObservableObject
{
    private readonly SearchSettingsRegistry _registry;
    public string Id { get; }
    public string Title { get; }
    public string Category { get; }
    public bool IsBoolean { get; }
    public bool IsChoice { get; }
    public IReadOnlyList<string> Choices { get; } = [];
    private bool _value, _pending, _refreshing, _hasValue;
    private string? _choice, _error;
    private int _readGeneration;
    public bool Value { get => _value; private set => SetProperty(ref _value, value); }
    public string? Choice
    {
        get => _choice;
        set { if (SetProperty(ref _choice, value) && !_refreshing && value is not null) _ = UpdateAsync(new ChoiceSettingValue(value)); }
    }
    public bool IsPending { get => _pending; private set { if (SetProperty(ref _pending, value)) OnPropertyChanged(nameof(CanUpdate)); } }
    public bool CanUpdate => !IsPending && _hasValue;
    public bool IsUnavailable => !_hasValue && HasError;
    public bool HasError => !string.IsNullOrEmpty(Error);
    public string? Error { get => _error; private set { if (SetProperty(ref _error, value)) { OnPropertyChanged(nameof(HasError)); OnPropertyChanged(nameof(IsUnavailable)); } } }
    public SettingSearchResultViewModel(SearchResult result, SearchSettingsRegistry registry)
    {
        Id = ((SettingSearchPayload)result.Payload).SettingId; Title = Loc.Get(result.Title);
        Category = Loc.Get(result.Subtitle ?? "Settings"); _registry = registry;
        var definition = registry.Settings.FirstOrDefault(setting => setting.Id == Id);
        IsBoolean = definition is SearchableBooleanSetting;
        if (definition is SearchableChoiceSetting choice) { IsChoice = true; Choices = choice.Choices; }
    }
    private void Apply(SettingValue value)
    {
        _refreshing = true;
        try
        {
            switch (value)
            {
                case BooleanSettingValue boolean: Value = boolean.Value; break;
                case ChoiceSettingValue choice: Choice = choice.Value; break;
            }
        }
        finally { _refreshing = false; }
    }
    public async Task RefreshAsync()
    {
        var generation = ++_readGeneration;
        try
        {
            using var deadline = new global::System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var actual = await _registry.ReadValueAsync(Id, deadline.Token).WaitAsync(deadline.Token);
            if (generation == _readGeneration && !IsPending)
            {
                var recovering = !_hasValue;
                _hasValue = true;
                if (recovering) Error = null;
                OnPropertyChanged(nameof(IsUnavailable));
                OnPropertyChanged(nameof(CanUpdate));
                Apply(actual);
            }
        }
        catch (Exception)
        {
            if (generation == _readGeneration)
            { _hasValue = false; OnPropertyChanged(nameof(CanUpdate)); Error = Loc.Get("This setting is unavailable."); }
        }
    }
    public Task ToggleAsync() => UpdateAsync(new BooleanSettingValue(!Value));
    private async Task UpdateAsync(SettingValue requested)
    {
        if (!CanUpdate) return;
        ++_readGeneration; IsPending = true; Error = null;
        try
        {
            using var deadline = new global::System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
            var result = await _registry.SetValueAsync(Id, requested, deadline.Token).WaitAsync(deadline.Token);
            Apply(result.Value); Error = result.Error is null ? null : Loc.Get(result.Error);
        }
        catch (Exception) { Error = Loc.Get("The setting could not be updated."); }
        finally
        {
            IsPending = false;
            await RefreshAsync();
            OnPropertyChanged(nameof(Value)); OnPropertyChanged(nameof(Choice));
        }
    }
}
