namespace DaisyOS.Core.Search;

public abstract record SettingValue;
public sealed record BooleanSettingValue(bool Value) : SettingValue;
public sealed record ChoiceSettingValue(string Value) : SettingValue;
public sealed record NumberSettingValue(double Value) : SettingValue;
public abstract record SearchableSetting(string Id, string Name, string Category, IReadOnlyList<string> Keywords)
{
    internal abstract Task<SettingValue> ReadValueAsync(CancellationToken token);
    internal abstract Task WriteValueAsync(SettingValue value, CancellationToken token);
    internal abstract bool IsValid(SettingValue value);
}
public sealed record SearchableBooleanSetting(string Id, string Name, string Category,
    IReadOnlyList<string> Keywords, Func<CancellationToken, Task<bool>> ReadAsync,
    Func<bool, CancellationToken, Task> WriteAsync) : SearchableSetting(Id, Name, Category, Keywords)
{
    internal override async Task<SettingValue> ReadValueAsync(CancellationToken token) => new BooleanSettingValue(await ReadAsync(token));
    internal override Task WriteValueAsync(SettingValue value, CancellationToken token) => WriteAsync(((BooleanSettingValue)value).Value, token);
    internal override bool IsValid(SettingValue value) => value is BooleanSettingValue;
}
public sealed record SearchableChoiceSetting(string Id, string Name, string Category, IReadOnlyList<string> Keywords,
    IReadOnlyList<string> Choices, Func<CancellationToken, Task<string>> ReadAsync,
    Func<string, CancellationToken, Task> WriteAsync) : SearchableSetting(Id, Name, Category, Keywords)
{
    internal override async Task<SettingValue> ReadValueAsync(CancellationToken token) => new ChoiceSettingValue(await ReadAsync(token));
    internal override Task WriteValueAsync(SettingValue value, CancellationToken token) => WriteAsync(((ChoiceSettingValue)value).Value, token);
    internal override bool IsValid(SettingValue value) => value is ChoiceSettingValue choice && Choices.Contains(choice.Value, StringComparer.Ordinal);
}
public sealed record SearchableNumberSetting(string Id, string Name, string Category, IReadOnlyList<string> Keywords,
    double Minimum, double Maximum, double Step, Func<CancellationToken, Task<double>> ReadAsync,
    Func<double, CancellationToken, Task> WriteAsync) : SearchableSetting(Id, Name, Category, Keywords)
{
    internal override async Task<SettingValue> ReadValueAsync(CancellationToken token) => new NumberSettingValue(await ReadAsync(token));
    internal override Task WriteValueAsync(SettingValue value, CancellationToken token) => WriteAsync(((NumberSettingValue)value).Value, token);
    internal override bool IsValid(SettingValue value) => value is NumberSettingValue number && double.IsFinite(number.Value) && number.Value >= Minimum && number.Value <= Maximum;
}
public sealed record SettingUpdateResult(bool Value, string? Error);
public sealed record SettingValueUpdateResult(SettingValue Value, string? Error);

/// <summary>Metadata and service delegates only; the underlying service owns every value.</summary>
public sealed class SearchSettingsRegistry
{
    private sealed record Entry(SearchableSetting Setting, SemaphoreSlim Updates);
    private (SearchableSetting Setting, SearchDocument Document)[] _catalog = [];
    internal IReadOnlyList<(SearchableSetting Setting, SearchDocument Document)> Catalog => Volatile.Read(ref _catalog);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new(StringComparer.Ordinal);
    public event EventHandler<string>? ValueChanged;
    public IReadOnlyList<SearchableSetting> Settings
    {
        get { lock (_gate) return _entries.Values.Select(e => e.Setting).OrderBy(s => s.Id, StringComparer.Ordinal).ToArray(); }
    }
    public void Register(SearchableBooleanSetting setting) => RegisterDefinition(setting);
    public void RegisterChoice(SearchableChoiceSetting setting) => RegisterDefinition(setting);
    public void RegisterNumber(SearchableNumberSetting setting)
    {
        if (!double.IsFinite(setting.Minimum) || !double.IsFinite(setting.Maximum) || setting.Maximum < setting.Minimum ||
            !double.IsFinite(setting.Step) || setting.Step <= 0) throw new ArgumentException("Numeric setting bounds are invalid.", nameof(setting));
        RegisterDefinition(setting);
    }
    public SearchableSetting GetDefinition(string id) => Resolve(id).Setting;
    private void RegisterDefinition(SearchableSetting setting)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(setting.Id);
        lock (_gate)
        {
            if (!_entries.TryAdd(setting.Id, new(setting, new(1, 1))))
                throw new ArgumentException("Setting IDs must be unique.", nameof(setting));
            Volatile.Write(ref _catalog, _catalog.Append((setting, new SearchDocument(setting.Name, setting.Category, setting.Keywords))).ToArray());
        }
    }
    public bool Unregister(string id)
    {
        lock (_gate)
        {
            if (!_entries.Remove(id)) return false;
            Volatile.Write(ref _catalog, _catalog.Where(e => e.Setting.Id != id).ToArray());
            return true;
        }
    }
    private Entry Resolve(string id)
    {
        lock (_gate) return _entries.TryGetValue(id, out var entry) ? entry :
            throw new KeyNotFoundException("This setting is unavailable.");
    }
    public async Task<bool> ReadAsync(string id, CancellationToken cancellationToken = default) =>
        (await ReadValueAsync(id, cancellationToken).ConfigureAwait(false) as BooleanSettingValue)?.Value
        ?? throw new ArgumentException("This setting is not Boolean.", nameof(id));
    public Task<SettingValue> ReadValueAsync(string id, CancellationToken cancellationToken = default) => Resolve(id).Setting.ReadValueAsync(cancellationToken);
    public void NotifyExternalChange(string id)
    {
        lock (_gate) { if (!_entries.ContainsKey(id)) return; }
        ValueChanged?.Invoke(this, id);
    }
    public async Task<SettingUpdateResult> SetAsync(string id, bool value, CancellationToken cancellationToken = default)
    {
        var result = await SetValueAsync(id, new BooleanSettingValue(value), cancellationToken).ConfigureAwait(false);
        return new(((BooleanSettingValue)result.Value).Value, result.Error);
    }
    public async Task<SettingValueUpdateResult> SetValueAsync(string id, SettingValue value, CancellationToken cancellationToken = default)
    {
        var entry = Resolve(id);
        if (!entry.Setting.IsValid(value)) throw new ArgumentException("The setting value is invalid.", nameof(value));
        if (!await entry.Updates.WaitAsync(0, cancellationToken).ConfigureAwait(false))
            return new(await entry.Setting.ReadValueAsync(cancellationToken).ConfigureAwait(false), "An update is already in progress.");
        try
        {
            string? error = null;
            try { await entry.Setting.WriteValueAsync(value, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { throw; }
            catch (Exception) { error = "The setting could not be updated."; }
            var actual = await entry.Setting.ReadValueAsync(cancellationToken).ConfigureAwait(false);
            if (error is null && actual != value) error = "The service did not confirm the requested value.";
            ValueChanged?.Invoke(this, id);
            return new(actual, error);
        }
        finally { entry.Updates.Release(); }
    }

}

public sealed class SettingsSearchProvider(SearchSettingsRegistry registry, IReadOnlyList<SearchableSettingsPage>? pages = null) : ISearchProvider
{
    public string Id => "settings";
    public SearchProviderCapabilities Capabilities => new("DaisyOS Settings", [SearchResultType.Setting], Priority: 90);
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        foreach (var (setting, document) in registry.Catalog)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (setting is not (SearchableBooleanSetting or SearchableChoiceSetting)) continue;
            var score = SearchRanker.Score(document, query);
            if (score > 0)
            {
                var page = pages?.FirstOrDefault(page => page.InlineSettingId == setting.Id);
                results.Add(new(setting.Id, Id, setting.Name, SearchResultType.Setting, score,
                    new SettingSearchPayload(setting.Id)) { Subtitle = setting.Category, MatchedTerms = query.Tokens,
                    Actions = page is null ? [] : [new("open", "settings-pages", page.Id, "Open in Settings")] });
            }
        }
        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
    public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken) =>
        throw new InvalidOperationException("Settings require a validated, explicitly requested value.");
}
