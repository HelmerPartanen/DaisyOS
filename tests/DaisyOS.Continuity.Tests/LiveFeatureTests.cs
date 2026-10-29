using System.Net;
using System.Net.Sockets;
using DaisyOS.Core.Continuity;
using DaisyOS.Continuity;
using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity.Tests;

public sealed class LiveFeatureTests
{
    [Theory]
    [InlineData("1 30 1 0 0")]
    [InlineData("1 30 0 0 0")]
    [InlineData("2 272 1 0 0")]
    [InlineData("3 0 0 -23.5 12")]
    [InlineData("4 0 0 0 -15")]
    [InlineData("5 0 0 0.5 1")]
    public void ValidBoundedInput(string input) => ContinuityDaemon.ValidateInput(input);

    [Theory]
    [InlineData("1 99999 1 0 0")]
    [InlineData("2 1 1 0 0")]
    [InlineData("5 0 0 1.1 0")]
    [InlineData("3 0 0 NaN 0")]
    [InlineData("3 0 0 Infinity 0")]
    [InlineData("3 0 0 100000 0")]
    [InlineData("1 30 2 0 0")]
    [InlineData("1 30 1 0 0\n1 1 1 0 0")]
    [InlineData("0 0 0 0 0")]
    [InlineData("1 30 1 0 0 extra")]
    public void MalformedInputFailsClosed(string input) => Assert.Throws<InvalidDataException>(() => ContinuityDaemon.ValidateInput(input));

    [Fact]
    public void DecoderRejectsOversizedDimensionsBeforeBitmapAllocation()
    {
        byte[] image = [0xff, 0xd8, 0xff, 0xc0, 0, 8, 8, 2, 208, 5, 0, 3];
        Assert.True(ContinuityDaemon.IsVideoFrame(image));
        image[7] = 255;
        Assert.False(ContinuityDaemon.IsVideoFrame(image));
        Assert.False(ContinuityDaemon.IsVideoFrame([0xff, 0xd8, 0xff, 0xc0, 0, 0]));
        Assert.False(ContinuityDaemon.IsVideoFrame([0xff, 0xd8]));
    }
    [Fact]
    public void ActivityOnlyAllowsVersionedNotesText()
    {
        new ContinuityActivity("org.daisyos.notes", "note.text", 1, "Meeting", "hello").Validate();
        Assert.Throws<InvalidDataException>(() => new ContinuityActivity("/bin/sh", "note.text", 1, "", "").Validate());
        Assert.Throws<InvalidDataException>(() => new ContinuityActivity("org.daisyos.notes", "note.text", 2, "", "").Validate());
        Assert.Throws<InvalidDataException>(() => new ContinuityActivity("org.daisyos.notes", "note.text", 1, "", new string('x', 65537)).Validate());
    }
    [Theory]
    [InlineData("screen", false)]
    [InlineData("screen", true)]
    [InlineData("audio", false)]
    public async Task OutgoingRequestsNeedPairingAndAnUnlockedSession(string kind, bool control)
    {
        using var fixture = new StateFixture();
        await using var daemon = fixture.Host();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => daemon.ExecuteAsync(new("live", "unknown-peer", kind, control), "local-ui"));
        Assert.Empty(daemon.Snapshot().Sessions!);
        Assert.Empty(daemon.Snapshot().Prompts);
    }
    [Theory]
    [InlineData("audio")]
    public async Task InitialAudioRequestsRequireTargetConsent(string kind)
    {
        using var fixture = new StateFixture();
        fixture.HostState.Settings.Peers[fixture.Peer.Id] = fixture.HostState.Settings.Peers[fixture.Peer.Id] with { Screen = true, Audio = false };
        await using var daemon = fixture.Host();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        var server = Task.Run(async () =>
        {
            using var socket = await listener.AcceptTcpClientAsync(timeout.Token);
            await using var wire = await PeerWire.OpenAsync(socket, fixture.HostState, true, false, fixture.Peer.Id, (_, _) => true, timeout.Token);
            await daemon.ReceiveLiveAsync(wire, new() { SessionId = Guid.NewGuid().ToString("N"), Kind = kind }, timeout.Token);
        });
        using var client = new TcpClient(); await client.ConnectAsync(IPAddress.Loopback, ((IPEndPoint)listener.LocalEndpoint).Port);
        await using var remote = await PeerWire.OpenAsync(client, fixture.Peer, false, false, fixture.HostState.Id, (_, _) => true, timeout.Token);
        while (daemon.Snapshot().Prompts.Length == 0) await Task.Delay(5, timeout.Token);
        var prompt = Assert.Single(daemon.Snapshot().Prompts);
        Assert.Equal(kind, prompt.Kind);
        await daemon.ExecuteAsync(new("decide", prompt.Id, Allowed: false), "local-ui");
        Assert.False((await remote.ReceiveAsync(timeout.Token)).LiveDecision.Accepted);
        await server;
        Assert.Empty(daemon.Snapshot().Sessions!);
    }
    [Fact]
    public async Task PermissionsPersistAndDoNotAffectExistingPairing()
    {
        using var fixture = new StateFixture();
        await using (var daemon = fixture.Host())
        {
            foreach (var feature in new[] { "input", "screen", "audio", "handoff" })
                await daemon.ExecuteAsync(new("permission", fixture.Peer.Id, feature, true), "local-ui");
            var device = Assert.Single(daemon.Snapshot().Devices);
            Assert.True(device.Input && device.Screen && device.Audio && device.Handoff);
            Assert.False(device.Clipboard);
        }
        Assert.True(fixture.HostState.Settings.Peers[fixture.Peer.Id].Audio);
        await using var inactive = new ContinuityDaemon(fixture.HostState, 47621, () => false, fixture.Root, "unix:path=/nonexistent-live-test");
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => inactive.ExecuteAsync(new("live", fixture.Peer.Id, "audio"), "local-ui"));
    }
    private sealed class StateFixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "daisy-live-" + Guid.NewGuid().ToString("N"));
        public SecureState HostState { get; }
        public SecureState Peer { get; }
        public StateFixture()
        {
            HostState = new(Path.Combine(Root, "host")); Peer = new(Path.Combine(Root, "peer"));
            HostState.Settings.Enabled = true;
            HostState.Settings.Peers[Peer.Id] = new(Convert.ToBase64String(Peer.Identity.RawData), "Peer", "127.0.0.1:1");
        }
        public ContinuityDaemon Host() => new(HostState, 47621, () => true, Root, "unix:path=/nonexistent-live-test");
        public void Dispose() { HostState.Dispose(); Peer.Dispose(); Directory.Delete(Root, true); }
    }
}
