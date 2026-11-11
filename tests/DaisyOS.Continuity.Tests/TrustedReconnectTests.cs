using System.Net;
using System.Net.Sockets;
using DaisyOS.Continuity;

namespace DaisyOS.Continuity.Tests;

public sealed class TrustedReconnectTests
{
    [Fact]
    public void OfflineTrustedPeerRetriesUseBoundedExponentialBackoff()
    {
        Assert.Equal(TimeSpan.FromSeconds(10), ContinuityDaemon.TrustedPeerRetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(20), ContinuityDaemon.TrustedPeerRetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(40), ContinuityDaemon.TrustedPeerRetryDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(60), ContinuityDaemon.TrustedPeerRetryDelay(4));
        Assert.Equal(TimeSpan.FromSeconds(60), ContinuityDaemon.TrustedPeerRetryDelay(20));
    }

    [Fact]
    public async Task TrustedPeerAutomaticallyStartsAndRestoresInputWithoutPairingOrLiveCommands()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-trusted-reconnect-" + Guid.NewGuid().ToString("N"));
        try
        {
            var sourceTools = EdgeInputTests.Tools(Path.Combine(root, "source-tools"));
            var targetTools = EdgeInputTests.Tools(Path.Combine(root, "target-tools"));
            using var source = new SecureState(Path.Combine(root, "source"));
            using var target = new SecureState(Path.Combine(root, "target"));
            using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
            source.Settings.Enabled = target.Settings.Enabled = true;
            source.Settings.Peers[target.Id] = new(Convert.ToBase64String(target.Identity.RawData), "Target", "127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port, Input: true, InputEdge: "left");
            target.Settings.Peers[source.Id] = new(Convert.ToBase64String(source.Identity.RawData), "Source", "127.0.0.1:1", Input: true);
            await using var sender = new ContinuityDaemon(source, 47621, () => true, root, "unix:path=/nonexistent-reconnect-source", sourceTools);
            await using var receiver = new ContinuityDaemon(target, 47621, () => true, root, "unix:path=/nonexistent-reconnect-target", targetTools);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var server = Task.Run(async () =>
            {
                for (var round = 0; round < 2; round++)
                {
                    using (var socket = await listener.AcceptTcpClientAsync(timeout.Token))
                    {
                        await using var wire = await PeerWire.OpenAsync(socket, target, true, false, source.Id, (_, _) => true, timeout.Token);
                        var ping = await wire.ReceiveAsync(timeout.Token);
                        await wire.SendAsync(new() { Pong = new() { Sequence = ping.Ping.Sequence } }, timeout.Token);
                    }
                    using var inputSocket = await listener.AcceptTcpClientAsync(timeout.Token);
                    await using var input = await PeerWire.OpenAsync(inputSocket, target, true, false, source.Id, (_, _) => true, timeout.Token);
                    var start = await input.ReceiveAsync(timeout.Token);
                    Assert.Equal("input", start.LiveStart.Kind);
                    try { await receiver.ReceiveLiveAsync(input, start.LiveStart, timeout.Token); }
                    catch (Exception ex) when (ex is IOException or OperationCanceledException) { }
                }
            });
            string? previous = null;
            for (var round = 0; round < 2; round++)
            {
                if (round == 1)
                {
                    // Simulate the saved address becoming stale after a network change.
                    // Discovery still has the verified reachable address from round one.
                    source.Settings.Peers[target.Id] = source.Settings.Peers[target.Id] with { Address = "127.0.0.1:1" };
                }
                await sender.ReconcilePeersAsync();
                while (!sender.Snapshot().Sessions!.Any(s => s.State == "Ready")) await Task.Delay(10, timeout.Token);
                var link = Assert.Single(sender.Snapshot().Sessions!);
                Assert.NotEqual(previous, link.Id);
                previous = link.Id;
                while (!File.Exists(Path.Combine(sourceTools, "returned"))) await Task.Delay(10, timeout.Token);
                Assert.Empty(sender.Snapshot().Prompts);
                Assert.Empty(receiver.Snapshot().Prompts);
                await sender.ExecuteAsync(new("live-stop", link.Id), "fixture");
                while (sender.Snapshot().Sessions!.Length != 0 || receiver.Snapshot().Sessions!.Length != 0) await Task.Delay(10, timeout.Token);
            }
            await server;
            Assert.Equal("left", source.Settings.Peers[target.Id].InputEdge);
            Assert.Equal("127.0.0.1:" + ((IPEndPoint)listener.LocalEndpoint).Port, source.Settings.Peers[target.Id].Address);
            Assert.Single(source.Settings.Peers);
            Assert.Single(target.Settings.Peers);
        }
        finally { Directory.Delete(root, true); }
    }
}
