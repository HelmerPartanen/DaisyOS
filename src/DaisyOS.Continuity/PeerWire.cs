using global::System.Net.Security;
using global::System.Net.Sockets;
using global::System.Security.Authentication;
using global::System.Security.Cryptography.X509Certificates;
using DaisyOS.Core.Continuity;
using DaisyOS.Continuity.Protocol.V1;
using Google.Protobuf;

namespace DaisyOS.Continuity;

public sealed class PeerWire : IAsyncDisposable
{
    private readonly TcpClient _client;
    public SslStream Stream { get; }
    public Bootstrap Remote { get; }
    public string PeerId { get; }
    public bool IsServer { get; private init; }
    public bool RemoteTrusted { get; private set; }
    private readonly HashSet<Capability> _capabilities = [];
    public bool Supports(Capability capability) => _capabilities.Contains(capability);
    private PeerWire(TcpClient client, SslStream stream, Bootstrap remote)
    { _client = client; Stream = stream; Remote = remote; PeerId = SecureState.Fingerprint(remote.Certificate.Span); }

    public static async Task<PeerWire> OpenAsync(TcpClient client, SecureState state, bool server, bool pairing,
        string? expectedId, Func<string, bool, bool> authorizeCandidate, CancellationToken token, int port = 47621)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        var ct = deadline.Token;
        var network = client.GetStream();
        var local = new Bootstrap { Major = 1, Certificate = ByteString.CopyFrom(state.Identity.RawData),
            Name = SecureState.SafeName(state.Settings.Name), Pairing = pairing, Port = (uint)port };
        // Public certificate preface only. No feature or secret data precedes TLS.
        await ContinuityFraming.WriteAsync(network, local.ToByteArray(), ct);
        var raw = await ReadBoundedAsync(network, 8192, ct);
        var remote = Bootstrap.Parser.ParseFrom(raw);
        if (remote.Major != 1 || remote.Certificate.Length is < 128 or > 4096 || remote.Name.Length > 256 || remote.Port is 0 or > 65535)
            throw new InvalidDataException("Invalid bootstrap.");
        var id = SecureState.Fingerprint(remote.Certificate.Span);
        if (id == state.Id || (expectedId is not null && id != expectedId) || !authorizeCandidate(id, server ? remote.Pairing : pairing))
            throw new AuthenticationException("Peer is not authorized.");
        using var certificate = X509CertificateLoader.LoadCertificate(remote.Certificate.Span);
        var policy = new X509ChainPolicy { TrustMode = X509ChainTrustMode.CustomRootTrust,
            RevocationMode = X509RevocationMode.NoCheck, DisableCertificateDownloads = true };
        policy.CustomTrustStore.Add(certificate);
        var ssl = new SslStream(network, false); // Platform verifier; no accept-all callback.
        try
        {
            if (server)
                await ssl.AuthenticateAsServerAsync(new SslServerAuthenticationOptions { ServerCertificate = state.Identity,
                    ClientCertificateRequired = true, CertificateChainPolicy = policy, EnabledSslProtocols = SslProtocols.Tls13 }, ct);
            else
                await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = "daisycont.local",
                    ClientCertificates = new X509CertificateCollection { state.Identity }, CertificateChainPolicy = policy,
                    EnabledSslProtocols = SslProtocols.Tls13 }, ct);
            if (ssl.RemoteCertificate is null || !ssl.RemoteCertificate.GetRawCertData().AsSpan().SequenceEqual(remote.Certificate.Span))
                throw new AuthenticationException("Certificate pin mismatch.");
            var wire = new PeerWire(client, ssl, remote) { IsServer = server };
            await wire.SendAsync(new Envelope { Hello = new Hello { DeviceId = ByteString.CopyFrom(Convert.FromHexString(state.Id)),
                Trusted = state.Settings.Peers.ContainsKey(id),
                Nonce = ByteString.CopyFrom(global::System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
                Capabilities = { Capability.TextClipboard, Capability.Files, Capability.Input, Capability.Handoff, Capability.Screen, Capability.Audio, Capability.EdgeInput } } }, ct);
            var hello = await wire.ReceiveAsync(ct);
            if (hello.Hello is null || hello.Hello.DeviceId != ByteString.CopyFrom(Convert.FromHexString(id)) || hello.Hello.Nonce.Length != 32)
                throw new InvalidDataException("Invalid Hello identity.");
            wire.RemoteTrusted = hello.Hello.Trusted;
            wire._capabilities.UnionWith(hello.Hello.Capabilities);
            return wire;
        }
        catch { await ssl.DisposeAsync(); throw; }
    }
    public async Task SendAsync(Envelope envelope, CancellationToken token)
    {
        envelope.ProtocolMajor = 1; envelope.ProtocolMinor = 1;
        envelope.RequestId = ByteString.CopyFrom(Guid.NewGuid().ToByteArray());
        var encoded = envelope.ToByteArray();
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try { await ContinuityFraming.WriteAsync(Stream, encoded, deadline.Token); await Stream.FlushAsync(deadline.Token); }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { throw new TimeoutException("Continuity write deadline."); }
        finally { global::System.Security.Cryptography.CryptographicOperations.ZeroMemory(encoded); }
    }
    public async Task<Envelope> ReceiveAsync(CancellationToken token)
    {
        var data = await ReadBoundedAsync(Stream, ContinuityFraming.MaxEnvelopeBytes, token);
        Envelope envelope;
        try { envelope = Envelope.Parser.ParseFrom(data); }
        finally { global::System.Security.Cryptography.CryptographicOperations.ZeroMemory(data); }
        if (envelope.ProtocolMajor != 1 || envelope.RequestId.Length != 16 || envelope.MessageCase == Envelope.MessageOneofCase.None)
            throw new InvalidDataException("Invalid envelope or protocol version.");
        return envelope;
    }
    public static async Task<byte[]> ReadBoundedAsync(Stream stream, int maximum, CancellationToken token)
    {
        var header = new byte[4];
        await stream.ReadExactlyAsync(header, token);
        var length = global::System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header);
        if (length == 0 || length > maximum) throw new InvalidDataException("Message limit exceeded.");
        var bytes = new byte[(int)length];
        await stream.ReadExactlyAsync(bytes, token);
        return bytes;
    }
    public async ValueTask DisposeAsync() { await Stream.DisposeAsync(); _client.Dispose(); }
}
