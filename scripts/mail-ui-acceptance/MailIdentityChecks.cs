using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DaisyOS.Shell.Apps.Mail;

internal static class MailIdentityChecks
{
    public static void Run()
    {
        using var rsa = RSA.Create(2048);
        var key = rsa.ExportParameters(false);
        var jwks = JsonSerializer.Serialize(new { keys = new[] { new { kty = "RSA", kid = "test-key", n = Encode(key.Modulus!), e = Encode(key.Exponent!) } } });
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new Dictionary<string, object> { ["iss"] = "https://accounts.google.com", ["aud"] = "test-client", ["nonce"] = "test-nonce", ["sub"] = "test-subject", ["exp"] = now + 600, ["iat"] = now, ["email"] = "test@example.org", ["email_verified"] = true, ["name"] = "Test User" };
        string Token()
        {
            var header = Encode(Encoding.UTF8.GetBytes("{\"alg\":\"RS256\",\"kid\":\"test-key\"}"));
            var body = Encode(JsonSerializer.SerializeToUtf8Bytes(claims));
            var data = header + "." + body;
            return data + "." + Encode(rsa.SignData(Encoding.ASCII.GetBytes(data), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        }
        MailAccount Account() => new() { Provider = "Google", ClientId = "test-client" };
        var account = Account(); MailIdentity.Apply(account, Token(), "test-nonce", jwks);
        if (account.Email != "test@example.org" || account.Name != "Test User") throw new Exception("Signed identity was not applied.");
        void Reject(string label, string token, string nonce = "test-nonce")
        {
            var rejected = false;
            try { MailIdentity.Apply(Account(), token, nonce, jwks); } catch (IOException) { rejected = true; }
            if (!rejected) throw new Exception("Accepted invalid identity: " + label);
        }
        Reject("nonce", Token(), "wrong-nonce");
        claims["aud"] = "another-client"; Reject("audience", Token()); claims["aud"] = "test-client";
        claims["iss"] = "https://attacker.invalid"; Reject("issuer", Token()); claims["iss"] = "https://accounts.google.com";
        claims["exp"] = now - 1; Reject("expired", Token()); claims["exp"] = now + 600;
        claims["email_verified"] = false; Reject("unverified mailbox", Token()); claims["email_verified"] = true;
        var valid = Token().Split('.'); valid[1] = Encode(Encoding.UTF8.GetBytes("{}")); Reject("tampered signature", string.Join('.', valid));
        claims["iss"] = "https://login.microsoftonline.com/9188040d-6c67-4c5b-b112-36a304b66dad/v2.0";
        claims.Remove("email"); claims.Remove("email_verified"); claims["preferred_username"] = "student@example.edu";
        var microsoft = new MailAccount { Provider = "Microsoft", ClientId = "test-client" };
        MailIdentity.Apply(microsoft, Token(), "test-nonce", jwks);
        if (microsoft.Email != "student@example.edu") throw new Exception("Microsoft school mailbox identity was not applied.");
    }
    private static string Encode(byte[] value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
