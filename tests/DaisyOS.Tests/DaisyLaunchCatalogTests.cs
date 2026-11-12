using System.Globalization;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DaisyLaunchCatalogTests
{
    private const string Catalog = """
        [{"id":"fixture.desktop","name":"Fixture","description":"Test","icon":"test","categories":[],
          "labels":{"Name":"Fixture","Name[fi]":"Testi"},"argv":["/bin/true"]}]
        """;

    [Fact]
    public async Task ConcurrentReadersShareOneFetchAndParsedSnapshot()
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var client = new DaisyLaunchClient(_ => { Interlocked.Increment(ref calls); return response.Task; });
        var readers = Enumerable.Range(0, 20).Select(_ => client.CatalogAsync()).ToArray();
        Assert.Equal(1, calls);
        response.SetResult(Catalog);
        var snapshots = await Task.WhenAll(readers);
        Assert.Equal(1, calls);
        Assert.All(snapshots, snapshot => Assert.Same(snapshots[0], snapshot));
        Assert.Equal("fixture.desktop", Assert.Single(snapshots[0]).Id);
    }

    [Fact]
    public async Task RegistryChangeDuringFetchRequiresAnotherFetch()
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var client = new DaisyLaunchClient(_ => Interlocked.Increment(ref calls) == 1 ? response.Task : Task.FromResult(Catalog));
        var first = client.CatalogAsync();
        client.InvalidateCatalog();
        response.SetResult(Catalog);
        var oldSnapshot = await first;
        var freshSnapshot = await client.CatalogAsync();
        Assert.Equal(2, calls);
        Assert.NotSame(oldSnapshot, freshSnapshot);
        Assert.Same(freshSnapshot, await client.CatalogAsync());
    }

    [Fact]
    public async Task LanguageChangeRefreshesLocalizedLabels()
    {
        var previous = CultureInfo.CurrentUICulture;
        try
        {
            var calls = 0;
            var client = new DaisyLaunchClient(_ => { calls++; return Task.FromResult(Catalog); });
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en-US");
            Assert.Equal("Fixture", Assert.Single(await client.CatalogAsync()).Name);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("fi-FI");
            Assert.Equal("Testi", Assert.Single(await client.CatalogAsync()).Name);
            Assert.Equal(2, calls);
        }
        finally { CultureInfo.CurrentUICulture = previous; }
    }

    [Fact]
    public async Task FailedFetchAndCancelledWaiterDoNotPoisonCache()
    {
        var response = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var client = new DaisyLaunchClient(_ => ++calls == 1 ? Task.FromException<string>(new IOException("offline")) : response.Task);
        await Assert.ThrowsAsync<IOException>(() => client.CatalogAsync());
        var pending = client.CatalogAsync();
        using var cancelled = new CancellationTokenSource();
        var waiter = client.CatalogAsync(cancelled.Token);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        response.SetResult(Catalog);
        Assert.Same(await pending, await client.CatalogAsync());
        Assert.Equal(2, calls);
    }
}
