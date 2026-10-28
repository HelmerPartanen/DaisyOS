using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;
using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity.Tests;

public sealed class ManualConnectionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TrustedAddressFallbackVerifiesPinAndPreservesExistingPermissions(bool mapped)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-continuity-connect-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SecureState(Path.Combine(root, "a"));
            using var second = new SecureState(Path.Combine(root, "b"));
            first.Settings.Enabled = true;
            first.Settings.Peers[second.Id] = new(Convert.ToBase64String(second.Identity.RawData), "Second", "127.0.0.1:1", true, true, "existing-grant");
            await using var host = new ContinuityDaemon(first, 47621, () => true, Path.Combine(root, "downloads"), "unix:path=/nonexistent-connect-test");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            var address = mapped ? $"[::ffff:127.0.0.1]:{port}" : $"127.0.0.1:{port}";
            var receive = Task.Run(async () =>
            {
                using var incoming = await listener.AcceptTcpClientAsync(timeout.Token);
                await using var wire = await PeerWire.OpenAsync(incoming, second, true, false, first.Id, (_, _) => true, timeout.Token);
                var ping = await wire.ReceiveAsync(timeout.Token);
                await wire.SendAsync(new() { Pong = new Pong { Sequence = ping.Ping.Sequence } }, timeout.Token);
            });
            await host.ExecuteAsync(new("connect", second.Id, address), "fixture-local-ui");
            await receive;
            while (host.Snapshot().Devices.Single().Address != address) await Task.Delay(5, timeout.Token);
            Assert.True(first.Settings.Peers[second.Id].Clipboard);
            Assert.True(first.Settings.Peers[second.Id].Files);
            Assert.Equal("existing-grant", first.Settings.Peers[second.Id].GrantId);
            Assert.Empty(host.Snapshot().Prompts);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
