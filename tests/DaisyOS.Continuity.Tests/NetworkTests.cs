using System.Net;
using System.Net.Sockets;
using System.Security.Authentication;
using DaisyOS.Continuity;
using DaisyOS.Continuity.Protocol.V1;

namespace DaisyOS.Continuity.Tests;

public sealed class NetworkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "daisy-continuity-" + Guid.NewGuid().ToString("N"));
    [Fact]
    public async Task RealTlsPeersAuthenticateAndExchangeHello()
    {
        using var first = new SecureState(Path.Combine(_root, "first"));
        using var second = new SecureState(Path.Combine(_root, "second"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using var server = await listener.AcceptTcpClientAsync(timeout.Token);
        var left = PeerWire.OpenAsync(client, first, false, true, second.Id, (_, _) => true, timeout.Token);
        var right = PeerWire.OpenAsync(server, second, true, false, first.Id, (_, _) => true, timeout.Token);
        await using var a = await left;
        await using var b = await right;
        Assert.True(client.NoDelay);
        Assert.True(server.NoDelay);
        Assert.Equal(second.Id, a.PeerId);
        Assert.Equal(first.Id, b.PeerId);
        Assert.Equal(SslProtocols.Tls13, a.Stream.SslProtocol);
        Assert.True(a.Stream.IsMutuallyAuthenticated);
        await a.SendAsync(new() { Ping = new Ping { Sequence = 9 } }, timeout.Token);
        Assert.Equal(9UL, (await b.ReceiveAsync(timeout.Token)).Ping.Sequence);
    }
    [Fact]
    public async Task ChangedIdentityPinIsRejectedBeforeFeatureExchange()
    {
        using var first = new SecureState(Path.Combine(_root, "pin-first"));
        using var second = new SecureState(Path.Combine(_root, "pin-second"));
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var listener = new TcpListener(IPAddress.Loopback, 0); listener.Start();
        using var client = new TcpClient();
        await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, timeout.Token);
        using var server = await listener.AcceptTcpClientAsync(timeout.Token);
        var serverTask = PeerWire.OpenAsync(server, second, true, false, first.Id, (_, _) => true, timeout.Token);
        await Assert.ThrowsAsync<AuthenticationException>(() => PeerWire.OpenAsync(client, first, false, false,
            new string('a', 64), (_, _) => true, timeout.Token));
        client.Dispose();
        try { await serverTask; } catch (Exception) { }
    }
    [Fact]
    public void UnknownPersistentSchemaFailsClosed()
    {
        var directory = Path.Combine(_root, "version");
        using (var state = new SecureState(directory)) state.Save();
        SecureState.WritePrivate(Path.Combine(directory, "settings.json"), global::System.Text.Encoding.UTF8.GetBytes("{\"Version\":2}"));
        Assert.Throws<IOException>(() => new SecureState(directory));
    }
    [Fact]
    public void IdentityPersistsWithPrivatePermissionsAndLegacyPauseIsCleared()
    {
        var directory = Path.Combine(_root, "identity");
        string id;
        using (var first = new SecureState(directory))
        {
            id = first.Id;
            Assert.False(first.Settings.Enabled); Assert.False(first.Settings.Visible);
            first.Settings.Paused = true;
            first.Save();
        }
        using var second = new SecureState(directory);
        Assert.Equal(id, second.Id);
        Assert.False(second.Settings.Paused);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(Path.Combine(directory, "identity.pfx")));
    }
    [Fact]
    public async Task StatusReportsCurrentPauseAndSessionStateAfterRestart()
    {
        using var state = new SecureState(Path.Combine(_root, "status"));
        state.Settings.Enabled = true; state.Settings.Paused = true;
        var active = true;
        await using var daemon = new ContinuityDaemon(state, 0, () => active, Path.Combine(_root, "downloads"), "unix:path=/nonexistent-status-test");
        Assert.True(daemon.Snapshot().Paused);
        Assert.Contains("paused", daemon.Snapshot().Diagnostic);
        state.Settings.Paused = false; active = false;
        Assert.False(daemon.Snapshot().SessionReady);
        Assert.Contains("unavailable", daemon.Snapshot().Diagnostic);
        active = true;
        Assert.True(daemon.Snapshot().SessionReady);
        Assert.Contains("enabled", daemon.Snapshot().Diagnostic);
        Assert.DoesNotContain("off", daemon.Snapshot().Diagnostic);
    }
    [Fact]
    public void UnsafeStateAndSymlinksAreRejected()
    {
        var directory = Path.Combine(_root, "unsafe"); Directory.CreateDirectory(directory);
        File.SetUnixFileMode(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        Assert.Throws<IOException>(() => new SecureState(directory));
        var link = Path.Combine(_root, "link"); Directory.CreateSymbolicLink(link, directory);
        Assert.Throws<IOException>(() => new SecureState(link));
    }
    [Theory]
    [InlineData("../evil", false)] [InlineData("/absolute", false)] [InlineData("a/b", false)]
    [InlineData("a\\b", false)] [InlineData("..", false)] [InlineData("file.txt", true)] [InlineData("emoji-🌼.txt", true)]
    public void NetworkFileNamesAreBasenames(string name, bool expected) => Assert.Equal(expected, ContinuityDaemon.IsSafeFileName(name));
    [Theory]
    [InlineData("192.168.1.2", true)] [InlineData("172.16.0.2", true)] [InlineData("10.1.2.3", true)]
    [InlineData("8.8.8.8", false)] [InlineData("172.32.0.2", false)] [InlineData("fe80::1", true)] [InlineData("2001:4860:4860::8888", false)]
    public void PublicNetworkAddressesAreRejected(string address, bool expected) => Assert.Equal(expected, AvahiDiscovery.IsLan(IPAddress.Parse(address)));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
