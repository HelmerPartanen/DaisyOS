using Avalonia.Threading;
using DaisyOS.Core.Search;
using DaisyOS.Shell.Services.Compositor;

namespace DaisyOS.Shell.Services;

public sealed class SearchKWinProvider(Func<ICompositorWindowService?> service) : ISearchProvider
{
    public string Id => "kwin";
    public SearchProviderCapabilities Capabilities => new("Open windows", [SearchResultType.Window], SearchStage.Extended, SupportsActions: true);
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task<IReadOnlyList<SearchResult>> SearchAsync(SearchQuery query, CancellationToken cancellationToken)
    {
        var bridge = service();
        if (bridge?.IsAvailable != true) return Task.FromResult<IReadOnlyList<SearchResult>>([]);
        var results = new List<SearchResult>();
        foreach (var window in bridge.Windows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var score = SearchRanker.Score(new(window.DisplayName, window.AppId, ["window", "open window"]), query);
            if (score <= 0) continue;
            results.Add(new(window.Id, Id, window.DisplayName, SearchResultType.Window, score,
                new WindowSearchPayload(window.Id))
            {
                Subtitle = window.AppId, Icon = window.IconName,
                Actions = window.IsMaximized
                    ? [new("activate", Id, window.Id, "Switch to window"), new("restore", Id, window.Id, "Restore")]
                    : [new("activate", Id, window.Id, "Switch to window")]
            });
        }
        return Task.FromResult<IReadOnlyList<SearchResult>>(results);
    }
    public async Task ExecuteAsync(SearchAction action, CancellationToken cancellationToken)
    {
        if (action.ProviderId != Id || service() is not { IsAvailable: true } bridge ||
            !bridge.Windows.Any(w => w.Id == action.EntityId)) throw new InvalidOperationException("This window is no longer available.");
        await Dispatcher.UIThread.InvokeAsync(async () =>
        {
            switch (action.Id)
            {
                case "activate": await bridge.ActivateOrMinimizeAsync(action.EntityId, false, cancellationToken); break;
                case "restore": await bridge.SetMaximizedAsync(action.EntityId, false, cancellationToken); break;
                default: throw new InvalidOperationException("This window action is unavailable.");
            }
        });
    }
}
