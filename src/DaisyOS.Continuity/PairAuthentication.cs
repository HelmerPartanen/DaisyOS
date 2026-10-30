using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using DaisyOS.Continuity.Protocol.V1;
using Google.Protobuf;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Agreement.Srp;
using Org.BouncyCastle.Crypto.Digests;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;

namespace DaisyOS.Continuity;

internal static class PairAuthentication
{
    private static async Task<PairConsent> ReceiveAsync(PeerWire wire, CancellationToken token) =>
        (await wire.ReceiveAsync(token).WaitAsync(TimeSpan.FromMinutes(2), token)).PairConsent
        ?? throw new InvalidDataException("Expected pairing proof.");

    private static BigInteger Number(ByteString bytes, int maximum)
    {
        if (bytes.Length is 0 || bytes.Length > maximum) throw new InvalidDataException("Invalid pairing proof.");
        return new BigInteger(1, bytes.ToByteArray());
    }

    internal static async Task<bool> VerifyAsync(PeerWire wire, byte[] identity, string code, CancellationToken token)
    {
        var salt = RandomNumberGenerator.GetBytes(32);
        var group = Srp6StandardGroups.rfc5054_3072;
        var verifier = new Srp6VerifierGenerator();
        verifier.Init(group, new Sha256Digest());
        var server = new Srp6Server();
        server.Init(group, verifier.GenerateVerifier(salt, identity, Encoding.ASCII.GetBytes(code)), new Sha256Digest(), new SecureRandom());
        await wire.SendAsync(new() { PairConsent = new() { Salt = ByteString.CopyFrom(salt),
            PublicValue = ByteString.CopyFrom(server.GenerateServerCredentials().ToByteArrayUnsigned()) } }, token);
        var response = await ReceiveAsync(wire, token);
        bool approved;
        try
        {
            server.CalculateSecret(Number(response.PublicValue, 384));
            approved = server.VerifyClientEvidenceMessage(Number(response.Proof, 32));
        }
        catch (CryptoException ex) { throw new AuthenticationException("Invalid pairing proof.", ex); }
        await wire.SendAsync(new() { PairConsent = new() { Approved = approved,
            Proof = approved ? ByteString.CopyFrom(server.CalculateServerEvidenceMessage().ToByteArrayUnsigned()) : ByteString.Empty } }, token);
        return approved;
    }

    internal static async Task<bool> ProveAsync(PeerWire wire, byte[] identity, Task<string> input, CancellationToken token)
    {
        var challenge = await ReceiveAsync(wire, token);
        if (challenge.Salt.Length != 32) throw new InvalidDataException("Pairing requires a compatible device.");
        var code = await input.WaitAsync(TimeSpan.FromMinutes(2), token);
        var client = new Srp6Client();
        client.Init(Srp6StandardGroups.rfc5054_3072, new Sha256Digest(), new SecureRandom());
        var publicValue = client.GenerateClientCredentials(challenge.Salt.ToByteArray(), identity, Encoding.ASCII.GetBytes(code));
        try { client.CalculateSecret(Number(challenge.PublicValue, 384)); }
        catch (CryptoException ex) { throw new AuthenticationException("Invalid pairing challenge.", ex); }
        await wire.SendAsync(new() { PairConsent = new() { PublicValue = ByteString.CopyFrom(publicValue.ToByteArrayUnsigned()),
            Proof = ByteString.CopyFrom(client.CalculateClientEvidenceMessage().ToByteArrayUnsigned()) } }, token);
        var result = await ReceiveAsync(wire, token);
        return result.Approved && client.VerifyServerEvidenceMessage(Number(result.Proof, 32));
    }
}
