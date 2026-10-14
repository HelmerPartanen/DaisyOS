using DaisyOS.System.Networking.LocalSend;
using global::System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LocalSendHostTests
{
    public sealed record Identity(string Alias, string Version);

    [Fact]
    public async Task EmbeddedHostHasNoFileConfigurationAndStillServesRoutes()
    {
        var builder = LocalSendService.CreateHostBuilder();
        Assert.DoesNotContain(builder.Configuration.Sources, source => source is JsonConfigurationSource);
        Assert.Equal(AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),
            builder.Environment.ContentRootPath.TrimEnd(Path.DirectorySeparatorChar));
        builder.WebHost.ConfigureKestrel(options => options.Listen(global::System.Net.IPAddress.Loopback, 0));
        await using var app = builder.Build();
        app.MapGet("/api/localsend/v2/info", () => new { alias = "DaisyOS", version = "2.1" });
        app.MapPost("/api/localsend/v2/register", (Identity identity) => identity);
        await app.StartAsync();
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var client = new HttpClient();
            var response = await client.GetStringAsync(address + "/api/localsend/v2/info");
            Assert.Contains("DaisyOS", response);
            using var registration = await client.PostAsJsonAsync(address + "/api/localsend/v2/register", new Identity("Test peer", "2.1"));
            registration.EnsureSuccessStatusCode();
            Assert.Equal(new Identity("Test peer", "2.1"), await registration.Content.ReadFromJsonAsync<Identity>());
        }
        finally { await app.StopAsync(); }
    }
}
