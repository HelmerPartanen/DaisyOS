using System.Text.Json;
using DaisyOS.Core.Search;
using DaisyOS.Shell.Services;
using DaisyOS.Shell.Services.Serialization;
using DaisyOS.System.Search;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SearchStatusProtocolTests
{
    [Fact]
    public void DiagnosticsRoundTripThroughTheExistingSourceGeneratedWireContext()
    {
        var report = new SearchStatusReport(new FileIndexDiagnostic("Ready", 42, 1000, DateTimeOffset.UtcNow,
            2, 0, false, 42), 100, 12, new SearchEngineDiagnostics(10, 2, 3.2, 8.1,
            [new ProviderDiagnostic("files", SearchProviderStatus.Ready, 1.2, 12)]));
        var json = SearchStatusProtocol.Serialize(report);
        Assert.DoesNotContain('\n', json);
        var read = SearchStatusProtocol.Deserialize(json);
        Assert.Equal(report.Index, read.Index);
        Assert.Equal(42, read.Index!.ScannedEntries);
        Assert.Equal(100, read.ApplicationCount);
        Assert.Equal("files", Assert.Single(read.Metrics.Providers).ProviderId);
    }
    [Fact]
    public async Task DiagnosticsSurviveTheLineFramedApplicationPipe()
    {
        var report = new SearchStatusReport(null, 12, 3, new SearchEngineDiagnostics(1, 0, 2, 2, []));
        var pipe = "daisy-search-test-" + Guid.NewGuid().ToString("N");
        using var lifetime = new CancellationTokenSource();
        var server = ApplicationPipe.ServeAsync(pipe, request =>
        {
            Assert.Equal("search-status", Assert.Single(request));
            return Task.FromResult(SearchStatusProtocol.Serialize(report));
        }, lifetime.Token);
        try
        {
            var response = await ApplicationPipe.RequestAsync(pipe, ["search-status"]);
            Assert.Equal(12, SearchStatusProtocol.Deserialize(response).ApplicationCount);
        }
        finally { lifetime.Cancel(); await server; }
    }

}
