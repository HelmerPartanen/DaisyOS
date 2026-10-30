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
    [Fact]
    public async Task UnknownCaptureGrantsAreRejected()
    {
        using var fixture = new StateFixture();
        await using var daemon = fixture.Host();
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => daemon.ExecuteAsync(new("capture-grant", Value: new string('a', 64)), "local-ui"));
    }
    [Fact]
    public void ExistingPairsEnableAllFeaturesAfterRestart()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-migrate-" + Guid.NewGuid().ToString("N"));
        try
        {
            using var peer = new SecureState(Path.Combine(root, "peer"));
            var path = Path.Combine(root, "host");
            using (var state = new SecureState(path))
            {
                state.Settings.Peers[peer.Id] = new(Convert.ToBase64String(peer.Identity.RawData), "Peer", "127.0.0.1:1", BlockedFeatures: 15, InputEdge: "left");
                state.Save();
            }
            using var reopened = new SecureState(path);
            var saved = Assert.Single(reopened.Settings.Peers).Value;
            Assert.True(saved.Clipboard && saved.Files && saved.Input && saved.Screen && saved.Audio && saved.Handoff);
            Assert.Equal(0, saved.BlockedFeatures);
            Assert.Equal("left", saved.InputEdge);
        }
        finally { Directory.Delete(root, true); }
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
