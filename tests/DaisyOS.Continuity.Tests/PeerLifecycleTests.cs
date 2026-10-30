using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class PeerLifecycleTests
{
    [Fact]
    public async Task OfflineForgetReconcilesOnNextAuthenticatedConnection()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-forget-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SecureState(Path.Combine(root, "a"));
            using var second = new SecureState(Path.Combine(root, "b"));
            first.Settings.Enabled = second.Settings.Enabled = true;
            first.Settings.Peers[second.Id] = new(Convert.ToBase64String(second.Identity.RawData), "Second", "127.0.0.1:1", true, true);
            second.Settings.Peers[first.Id] = new(Convert.ToBase64String(first.Identity.RawData), "First", "127.0.0.1:1", true, true);
            await using var a = new ContinuityDaemon(first, 0, () => true, root, "unix:path=/nonexistent-forget-a");
            await using var b = new ContinuityDaemon(second, 0, () => true, root, "unix:path=/nonexistent-forget-b");
            await b.ExecuteAsync(new("forget", first.Id), "local-ui");
            Assert.Empty(second.Settings.Peers);
            Assert.Single(first.Settings.Peers);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var client = new TcpClient(); await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            var connect = PeerWire.OpenAsync(client, first, false, false, second.Id, (_, _) => true, timeout.Token);
            var accept = PeerWire.OpenAsync(accepted, second, true, false, first.Id, (_, _) => true, timeout.Token);
            await using var outgoing = await connect; await using var incoming = await accept;
            Assert.False(outgoing.RemoteTrusted);
            Assert.False(a.CheckMutualTrust(outgoing));
            Assert.Empty(first.Settings.Peers);
            Assert.Empty(a.Snapshot().Prompts);
            Assert.DoesNotContain("error", a.Snapshot().Diagnostic, StringComparison.OrdinalIgnoreCase);
        }
        finally { Directory.Delete(root, true); }
    }
}
