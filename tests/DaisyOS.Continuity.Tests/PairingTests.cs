using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class PairingTests
{
    [Theory]
    [InlineData("correct")]
    [InlineData("incorrect")]
    [InlineData("cancel")]
    public async Task OnlyInitiatorEntersCodeAndPairingEnablesEveryFeature(string action)
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
            Assert.Equal("pair-code", promptA.Kind); Assert.Equal("pair-display", promptB.Kind);
            Assert.Matches("^[0-9]{6}$", promptB.Detail);
            Assert.Empty(first.Settings.Peers); Assert.Empty(second.Settings.Peers);
            if (action == "cancel")
            {
                await firstHost.ExecuteAsync(new("pair-cancel", promptA.Id), "fixture-local-ui");
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => firstPair);
                await firstWire.DisposeAsync();
                await Assert.ThrowsAnyAsync<IOException>(() => secondPair);
                Assert.Empty(first.Settings.Peers); Assert.Empty(second.Settings.Peers);
                Assert.Empty(firstHost.Snapshot().Prompts);
                return;
            }
            var approveSecond = action == "correct";
            var typedCode = approveSecond ? promptB.Detail : ((int.Parse(promptB.Detail) + 1) % 1_000_000).ToString("D6");
            await firstHost.ExecuteAsync(new("pair-code", promptA.Id, typedCode), "fixture-local-ui");
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
                Assert.True(peerA.Clipboard && peerA.Files && peerA.Input && peerA.Handoff && peerA.Screen && peerA.Audio);
                Assert.True(peerB.Clipboard && peerB.Files && peerB.Input && peerB.Handoff && peerB.Screen && peerB.Audio);
                Assert.Equal(32, peerA.GrantId.Length); Assert.Equal(32, peerB.GrantId.Length);
                Assert.True(File.Exists(Path.Combine(first.DirectoryPath, "settings.json")));
                Assert.True(File.Exists(Path.Combine(second.DirectoryPath, "settings.json")));
            }
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task KnowledgeOfCodeCannotBeRelayedToDifferentTlsIdentities()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-pair-binding-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var first = new SecureState(Path.Combine(root, "a"));
            using var second = new SecureState(Path.Combine(root, "b"));
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            using var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
            using var accepted = await listener.AcceptTcpClientAsync(timeout.Token);
            var connect = PeerWire.OpenAsync(client, first, false, true, second.Id, (_, _) => true, timeout.Token);
            var accept = PeerWire.OpenAsync(accepted, second, true, false, first.Id, (_, _) => true, timeout.Token);
            await using var a = await connect; await using var b = await accept;
            var server = PairAuthentication.VerifyAsync(b, "real TLS identities"u8.ToArray(), "012345", timeout.Token);
            var proof = PairAuthentication.ProveAsync(a, "intermediary identities"u8.ToArray(), Task.FromResult("012345"), timeout.Token);
            Assert.False(await proof); Assert.False(await server);
        }
        finally { Directory.Delete(root, true); }
    }

}
