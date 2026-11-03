using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MimeKit;

namespace DaisyOS.Shell.Apps.Mail;

/// <summary>Validates the provider's signed identity before selecting a mailbox.</summary>
public static class MailIdentity
{
    public static void Apply(MailAccount account, string token, string nonce, string jwks)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw new IOException("The sign-in response was invalid.");
        using var header = JsonDocument.Parse(Decode(parts[0]));
        if (header.RootElement.GetProperty("alg").GetString() != "RS256") throw new IOException("Unsupported sign-in signature.");
        var kid = header.RootElement.GetProperty("kid").GetString();
        using var keys = JsonDocument.Parse(jwks);
        var key = keys.RootElement.GetProperty("keys").EnumerateArray().FirstOrDefault(k =>
            k.GetProperty("kid").GetString() == kid && k.GetProperty("kty").GetString() == "RSA");
        if (key.ValueKind == JsonValueKind.Undefined) throw new IOException("The sign-in signature could not be verified.");
        using var rsa = RSA.Create();
        rsa.ImportParameters(new RSAParameters { Modulus = Decode(key.GetProperty("n").GetString()!), Exponent = Decode(key.GetProperty("e").GetString()!) });
        if (!rsa.VerifyData(Encoding.ASCII.GetBytes(parts[0] + "." + parts[1]), Decode(parts[2]), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
            throw new IOException("The sign-in signature was invalid.");
        using var payload = JsonDocument.Parse(Decode(parts[1]));
        var claims = payload.RootElement;
        string? Claim(string name) => claims.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
        var issuer = Claim("iss");
        var validIssuer = account.Provider == "Google" ? issuer is "https://accounts.google.com" or "accounts.google.com" :
            issuer is not null && Uri.TryCreate(issuer, UriKind.Absolute, out var uri) && uri.Scheme == "https" && uri.Host == "login.microsoftonline.com" &&
            uri.AbsolutePath.EndsWith("/v2.0", StringComparison.Ordinal) && Guid.TryParse(uri.Segments.ElementAtOrDefault(1)?.Trim('/'), out _);
        var audience = claims.GetProperty("aud");
        var validAudience = audience.ValueKind == JsonValueKind.String ? audience.GetString() == account.ClientId :
            audience.ValueKind == JsonValueKind.Array && audience.EnumerateArray().Any(a => a.GetString() == account.ClientId) && Claim("azp") == account.ClientId;
        if (!validIssuer || !validAudience || Claim("nonce") != nonce || string.IsNullOrEmpty(Claim("sub")) ||
            claims.GetProperty("exp").GetInt64() <= DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            claims.GetProperty("iat").GetInt64() > DateTimeOffset.UtcNow.AddMinutes(5).ToUnixTimeSeconds() ||
            (Claim("azp") is { } azp && azp != account.ClientId)) throw new IOException("The sign-in response could not be verified.");
        if (account.Provider == "Google" && (!claims.TryGetProperty("email_verified", out var verified) || verified.ValueKind != JsonValueKind.True))
            throw new IOException("Your email address has not been verified by Google.");
        var address = Claim("email") ?? Claim("preferred_username");
        if (!MailboxAddress.TryParse(address, out var mailbox) || mailbox.Address != address || !mailbox.Address.Contains('@'))
            throw new IOException("Your provider did not return a mailbox address.");
        account.Email = mailbox.Address; account.Name = Claim("name") ?? mailbox.Address;
    }
    private static byte[] Decode(string value)
    {
        value = value.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(value.PadRight((value.Length + 3) / 4 * 4, '='));
    }
}

public sealed class MailSignInUnavailableException(string message) : IOException(message);
