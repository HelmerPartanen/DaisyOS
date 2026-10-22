using System.Net.Sockets;
using System.Text;
using DaisyOS.Core.Models;
using DaisyOS.System.Bluetooth;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LibrePodsServiceTests
{
    private const string Address = "AA:BB:CC:DD:EE:FF";
    private const string Status = """{"version":1,"connected":true,"address":"AA:BB:CC:DD:EE:FF","name":"Alex’s AirPods","mode":2,"modes":[0,1,2,3],"battery":"Left 80%, Right 75%"}""";

    [Fact]
    public async Task MissingCompanionDoesNotInventAConnection()
    {
        var service = new LibrePodsService("/tmp/nonexistent-" + Guid.NewGuid().ToString("N"));
        Assert.False((await service.GetStatusAsync()).IsConnected);
        Assert.False(await service.SetNoiseModeAsync(Address, AirPodsNoiseMode.Anc));
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("{\"version\":2}")]
    [InlineData("{}")]
    public async Task InvalidOrIncompatibleStatusIsUnavailable(string response)
    {
        await WithServer([response], async service => Assert.False((await service.GetStatusAsync()).IsAvailable));
    }

    [Fact]
    public async Task ReadsFragmentedUtf8StatusAndRealCapabilities()
    {
        await WithServer([Status], async service =>
        {
            var status = await service.GetStatusAsync();
            Assert.Equal("Alex’s AirPods", status.Name);
            Assert.Equal(AirPodsNoiseMode.Transparency, status.NoiseMode);
            Assert.Equal(4, status.SupportedModes.Count);
            Assert.Equal("Left 80%, Right 75%", status.Battery);
        });
    }

    [Fact]
    public async Task ReportsAuthenticatedProximityWithoutClaimingAConnection()
    {
        var status = Status.Replace("\"connected\":true", "\"connected\":false")
            .Replace("\"version\":1", "\"nearbyAddress\":\"AA:BB:CC:DD:EE:FF\",\"version\":1");
        await WithServer([status], async service =>
        {
            var result = await service.GetStatusAsync();
            Assert.False(result.IsConnected);
            Assert.Equal(Address, result.NearbyAddress);
        });
    }

    [Fact]
    public async Task ModeChangeRequiresDeviceConfirmation()
    {
        await WithServer(["ok", Status, Status.Replace("\"mode\":2", "\"mode\":1")], async service =>
            Assert.True(await service.SetNoiseModeAsync(Address, AirPodsNoiseMode.Anc)));
    }

    [Fact]
    public async Task DisconnectAfterAcknowledgementFailsModeChange()
    {
        await WithServer(["ok", Status.Replace("\"connected\":true", "\"connected\":false")], async service =>
            Assert.False(await service.SetNoiseModeAsync(Address, AirPodsNoiseMode.Anc)));
    }

    [Fact]
    public async Task RejectsInvalidCommandsBeforeConnecting()
    {
        var service = new LibrePodsService("/tmp/no-server");
        Assert.False(await service.SetNoiseModeAsync("AA\nstatus", AirPodsNoiseMode.Anc));
        Assert.False(await service.SetNoiseModeAsync(Address, (AirPodsNoiseMode)90));
    }

    private static async Task WithServer(string[] responses, Func<LibrePodsService, Task> action)
    {
        var path = "/tmp/pods-test-" + Guid.NewGuid().ToString("N");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        using var server = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        server.Bind(new UnixDomainSocketEndPoint(path));
        server.Listen();
        var serve = Task.Run(async () =>
        {
            foreach (var response in responses)
            {
                using var client = await server.AcceptAsync(timeout.Token);
                using var stream = new NetworkStream(client);
                using var reader = new StreamReader(stream, leaveOpen: true);
                var command = await reader.ReadLineAsync(timeout.Token);
                Assert.True(command == "status" || command == "noise:" + Address + ":1");
                var bytes = Encoding.UTF8.GetBytes(response + "\n");
                var split = bytes.Length / 2;
                await stream.WriteAsync(bytes.AsMemory(0, split), timeout.Token);
                await Task.Delay(5, timeout.Token);
                await stream.WriteAsync(bytes.AsMemory(split), timeout.Token);
            }
        }, timeout.Token);
        try { await action(new LibrePodsService(path)); await serve; }
        finally { timeout.Cancel(); File.Delete(path); }
    }
}
