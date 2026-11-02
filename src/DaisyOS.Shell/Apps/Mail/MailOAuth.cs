using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace DaisyOS.Shell.Apps.Mail;

internal static class MailOAuth
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private static string TokenEndpoint(MailAccount a) => a.Provider == "Google" ? "https://oauth2.googleapis.com/token" : "https://login.microsoftonline.com/common/oauth2/v2.0/token";
    public static async Task<string> SignInAsync(MailAccount a, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(a.ClientId)) throw new InvalidOperationException("Enter a registered OAuth desktop application client ID. See docs/Mail.md for setup.");
        var probe = new TcpListener(IPAddress.Loopback, 0); probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port; probe.Stop();
        var redirect = $"http://127.0.0.1:{port}/";
        using var listener = new HttpListener(); listener.Prefixes.Add(redirect); listener.Start();
        var verifier = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var challenge = Convert.ToBase64String(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        var state = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var scope = a.Provider == "Google" ? "https://mail.google.com/" : "offline_access https://outlook.office.com/IMAP.AccessAsUser.All https://outlook.office.com/SMTP.Send";
        var endpoint = a.Provider == "Google" ? "https://accounts.google.com/o/oauth2/v2/auth" : "https://login.microsoftonline.com/common/oauth2/v2.0/authorize";
        var query = new Dictionary<string, string> { ["client_id"] = a.ClientId, ["redirect_uri"] = redirect, ["response_type"] = "code", ["scope"] = scope, ["state"] = state, ["code_challenge"] = challenge, ["code_challenge_method"] = "S256", ["login_hint"] = a.Email, ["prompt"] = "consent" };
        if (a.Provider == "Google") query["access_type"] = "offline";
        Process.Start(new ProcessStartInfo(endpoint + "?" + string.Join("&", query.Select(p => Uri.EscapeDataString(p.Key) + "=" + Uri.EscapeDataString(p.Value)))) { UseShellExecute = true });
        var context = await listener.GetContextAsync().WaitAsync(ct);
        var valid = context.Request.QueryString["state"] == state;
        var code = context.Request.QueryString["code"];
        var bytes = Encoding.UTF8.GetBytes("You can close this window and return to DaisyOS Mail.");
        context.Response.ContentType = "text/plain; charset=utf-8";
        await context.Response.OutputStream.WriteAsync(bytes, ct); context.Response.Close();
        if (!valid || string.IsNullOrEmpty(code)) throw new IOException("Sign-in was declined or the sign-in response was invalid.");
        return await ExchangeAsync(a, new() { ["grant_type"] = "authorization_code", ["code"] = code, ["redirect_uri"] = redirect, ["code_verifier"] = verifier }, null, ct);
    }
    public static async Task<string> RefreshAsync(MailAccount a, string refresh, CancellationToken ct) =>
        await ExchangeAsync(a, new() { ["grant_type"] = "refresh_token", ["refresh_token"] = refresh }, refresh, ct);
    private static async Task<string> ExchangeAsync(MailAccount a, Dictionary<string, string> fields, string? previous, CancellationToken ct)
    {
        fields["client_id"] = a.ClientId;
        if (a.Provider == "Google" && Environment.GetEnvironmentVariable("DAISYOS_MAIL_GOOGLE_CLIENT_SECRET") is { Length: > 0 } clientSecret) fields["client_secret"] = clientSecret;
        using var response = await Http.PostAsync(TokenEndpoint(a), new FormUrlEncodedContent(fields), ct);
        if (!response.IsSuccessStatusCode) throw new IOException("The provider could not authorize Mail. Check the client ID, allowed accounts and administrator consent, then sign in again.");
        using var data = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        var refresh = data.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : previous;
        if (string.IsNullOrEmpty(refresh)) throw new IOException("The provider did not grant offline access. Sign in again and allow offline access.");
        await MailSecrets.RunAsync("store", a.Id, refresh, ct);
        return data.RootElement.GetProperty("access_token").GetString()!;
    }
}
