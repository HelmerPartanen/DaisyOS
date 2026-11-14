namespace DaisyOS.Core.Search;

public sealed record SearchableSettingsPage(string Id, string Title, string Category, string Section,
    string Description, IReadOnlyList<string> Keywords)
{
    public string? InlineSettingId { get; init; }
    public string? SearchTitle { get; init; }
    public bool HasAdditionalTasks { get; init; }
}
public sealed record SettingsPageSearchPayload(string Category, string Section, string Setting) : SearchPayload;

/// <summary>Reuses the Settings application's catalog and navigation, with no duplicated values.</summary>
public sealed class SettingsPageSearchProvider : ISearchProvider
{
    private readonly IReadOnlyDictionary<string, (SearchableSettingsPage Page, SearchDocument Document)> _pages;
    private readonly Func<SearchableSettingsPage, CancellationToken, Task> _open;
    private readonly SearchSettingsRegistry? _settings;
    public string Id => "settings-pages";
    public SearchProviderCapabilities Capabilities => new("Settings pages", [SearchResultType.Setting], Priority: 70, SupportsActions: true);
    public SettingsPageSearchProvider(IEnumerable<SearchableSettingsPage> pages, Func<SearchableSettingsPage, CancellationToken, Task> open, SearchSettingsRegistry? settings = null)
    {
        _open = open;
        _settings = settings;
        _pages = pages.ToDictionary(p => p.Id, p => (p, new SearchDocument(p.Title, p.Description, p.Keywords.Append(p.Category))), StringComparer.Ordinal);
    }
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var results = new List<SearchResult>();
        foreach (var (page, document) in _pages.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            // A page fallback is useful only when its equivalent inline editor is unavailable.
            if (!page.HasAdditionalTasks && page.InlineSettingId is { } inline && _settings?.Settings.Any(setting => setting.Id == inline &&
                setting is SearchableBooleanSetting or SearchableChoiceSetting) == true) continue;
            var score = SearchRanker.Score(document, query);
            if (score > 0) results.Add(new(page.Id, Id, page.SearchTitle ?? page.Title, SearchResultType.Setting, score,
                new SettingsPageSearchPayload(page.Category, page.Section, page.Title))
            { Subtitle = page.Category, Actions = [new("open", Id, page.Id, "Open in Settings")] });
        }
        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
    public Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken)
    {
        if (action.ProviderId != Id || action.Id != "open" || !_pages.TryGetValue(action.EntityId, out var entry))
            throw new InvalidOperationException("This settings page is unavailable.");
        return _open(entry.Page, cancellationToken);
    }
}
