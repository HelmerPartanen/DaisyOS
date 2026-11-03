using System.Text.Json;

namespace DaisyOS.Shell.Apps.Mail;

/// <summary>Distributor-owned registration, independent of how the desktop launches Mail.</summary>
public sealed record MailProviderRegistration
{
    public string GoogleClientId { get; init; } = "";
    public string GoogleClientSecret { get; init; } = "";
    public string MicrosoftClientId { get; init; } = "";

    public bool GoogleConfigured => GoogleClientId is { Length: > 0 } id && id.EndsWith(".apps.googleusercontent.com", StringComparison.Ordinal) && id.Length > ".apps.googleusercontent.com".Length;
    public bool MicrosoftConfigured => Guid.TryParse(MicrosoftClientId, out _);

    public static MailProviderRegistration Load(string? file = null, bool useEnvironment = true)
    {
        var config = new MailProviderRegistration();
        var explicitFile = file ?? (useEnvironment ? Environment.GetEnvironmentVariable("DAISYOS_MAIL_PROVIDER_CONFIG") : null);
        if (explicitFile is not null) config = Read(explicitFile);
        else
        {
            var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            foreach (var path in new[] { Path.Combine(AppContext.BaseDirectory, "mail-providers.json"), "/etc/daisyos/mail-providers.json", Path.Combine(configHome, "daisyos", "mail", "providers.json") })
                if (File.Exists(path)) config = Read(path);
        }
        if (!useEnvironment) return config;
        return config with
        {
            GoogleClientId = Environment.GetEnvironmentVariable("DAISYOS_MAIL_GOOGLE_CLIENT_ID")?.Trim() ?? config.GoogleClientId,
            GoogleClientSecret = Environment.GetEnvironmentVariable("DAISYOS_MAIL_GOOGLE_CLIENT_SECRET") ?? config.GoogleClientSecret,
            MicrosoftClientId = Environment.GetEnvironmentVariable("DAISYOS_MAIL_MICROSOFT_CLIENT_ID")?.Trim() ?? config.MicrosoftClientId
        };
    }
    private static MailProviderRegistration Read(string path)
    {
        using var stream = File.OpenRead(path);
        if (stream.Length > 64 * 1024) throw new IOException("Mail provider registration is too large.");
        var config = JsonSerializer.Deserialize(stream, MailJsonContext.Default.MailProviderRegistration) ?? throw new IOException("Mail provider registration is empty.");
        return config with { GoogleClientId = config.GoogleClientId?.Trim() ?? "", MicrosoftClientId = config.MicrosoftClientId?.Trim() ?? "", GoogleClientSecret = config.GoogleClientSecret ?? "" };
    }
}
