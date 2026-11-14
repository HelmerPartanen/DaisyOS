using DaisyOS.Shell.Services;

namespace DaisyOS.Shell;

public partial class App
{
    private SearchStatusReport CaptureSearchStatus()
    {
        var service = SearchService;
        return new(service.Files?.Diagnostics, service.Catalog.Count, service.Settings.Settings.Count,
            service.Coordinator.Diagnostics);
    }
    internal async Task<SearchStatusReport> GetSearchStatusAsync()
    {
        // Standalone applications never construct an indexer just to inspect diagnostics.
        if (!_useDesktopSettings) return CaptureSearchStatus();
        var response = await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), ["search-status"]);
        return SearchStatusProtocol.Deserialize(response);
    }
    internal async Task SetSearchPausedAsync(bool paused)
    {
        if (!_useDesktopSettings)
        {
            SearchService.Files?.SetPaused(paused);
            return;
        }
        var response = await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), ["search-pause", paused.ToString()]);
        if (response != "ok") throw new IOException("Search indexing could not be paused or resumed.");
    }
    internal async Task ReconcileSearchAsync()
    {
        if (!_useDesktopSettings)
        {
            if (SearchService.Files is not null) await SearchService.Files.ReindexAsync();
            return;
        }
        var response = await ApplicationPipe.RequestAsync(ApplicationPipe.Name("desktop-settings"), ["search-reconcile"]);
        if (response != "ok") throw new IOException("Search reconciliation could not be requested.");
    }
}
