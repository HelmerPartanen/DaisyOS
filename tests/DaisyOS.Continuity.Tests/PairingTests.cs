using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class PairingTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BothLocalApprovalsAreRequiredAndPermissionsRemainOff(bool approveSecond)
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-continuity-pair-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SecureState(Path.Combine(root, "a"));
            using var second = new SecureState(Path.Combine(root, "b"));
            first.Settings.Enabled = second.Settings.Enabled = true;
            first.Settings.Visible = second.Settings.Visible = true;
            await using var firstHost = new ContinuityDaemon(first, 0, () => true, Path.Combine(root, "downloads-a"), "unix:path=/nonexistent-pair-test-a");
            await using var secondHost = new ContinuityDaemon(second, 0, () => true, Path.Combine(root, "downloads-b"), "unix:path=/nonexistent-pair-test-b");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            var connect = PeerWire.OpenAsync(client, first, false, true, second.Id, (_, _) => true, timeout.Token);
            var accept = PeerWire.OpenAsync(accepted, second, true, false, first.Id, (_, _) => true, timeout.Token);
            await using var firstWire = await connect;
            await using var secondWire = await accept;
            var firstPair = firstHost.PairAsync(firstWire, "127.0.0.1:47621", timeout.Token);
            var secondPair = secondHost.PairAsync(secondWire, "127.0.0.1:47621", timeout.Token);
            while (firstHost.Snapshot().Prompts.Length == 0 || secondHost.Snapshot().Prompts.Length == 0)
                await Task.Delay(5, timeout.Token);
            var promptA = Assert.Single(firstHost.Snapshot().Prompts);
            var promptB = Assert.Single(secondHost.Snapshot().Prompts);
            Assert.Contains(first.Id, promptA.Detail); Assert.Contains(second.Id, promptA.Detail);
            Assert.Contains(first.Id, promptB.Detail); Assert.Contains(second.Id, promptB.Detail);
            Assert.Empty(first.Settings.Peers); Assert.Empty(second.Settings.Peers);
            await firstHost.ExecuteAsync(new("decide", promptA.Id, Allowed: true), "fixture-local-ui");
            while (firstHost.Snapshot().Prompts.Length != 0) await Task.Delay(5, timeout.Token);
            Assert.Empty(first.Settings.Peers); Assert.Empty(second.Settings.Peers);
            await secondHost.ExecuteAsync(new("decide", promptB.Id, Allowed: approveSecond), "fixture-local-ui");
            await Task.WhenAll(firstPair, secondPair).WaitAsync(timeout.Token);
            if (!approveSecond)
            {
                Assert.Empty(first.Settings.Peers); Assert.Empty(second.Settings.Peers);
            }
            else
            {
                var peerA = Assert.Single(first.Settings.Peers).Value;
                var peerB = Assert.Single(second.Settings.Peers).Value;
                Assert.Equal(Convert.ToBase64String(second.Identity.RawData), peerA.Certificate);
                Assert.Equal(Convert.ToBase64String(first.Identity.RawData), peerB.Certificate);
                Assert.False(peerA.Clipboard); Assert.False(peerA.Files);
                Assert.False(peerB.Clipboard); Assert.False(peerB.Files);
                Assert.Equal(32, peerA.GrantId.Length); Assert.Equal(32, peerB.GrantId.Length);
                Assert.True(File.Exists(Path.Combine(first.DirectoryPath, "settings.json")));
                Assert.True(File.Exists(Path.Combine(second.DirectoryPath, "settings.json")));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
}
