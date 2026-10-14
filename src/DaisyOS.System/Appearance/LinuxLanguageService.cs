using DaisyOS.Core.Models;
using DaisyOS.System.Processes;

namespace DaisyOS.System.Appearance;

/// <summary>Per-user desktop locale. Never changes the machine-wide locale or keyboard.</summary>
public sealed class LinuxLanguageService(ICommandRunner runner, string? configHome = null)
{
    private readonly string _configHome = configHome ?? GetConfigHome();

    private static string GetConfigHome() =>
        Environment.GetEnvironmentVariable("XDG_CONFIG_HOME") is { Length: > 0 } path ? path :
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");

    public async Task<PersonalizationApplyResult> ApplyAsync(string tag, CancellationToken cancellationToken = default)
    {
        if (SystemLanguages.Find(tag) is not { } language)
            return PersonalizationApplyResult.Failure("Choose a supported language.");
        var locales = await runner.RunAsync("locale", ["-a"], TimeSpan.FromSeconds(5), cancellationToken);
        var locale = locales.Succeeded && locales.StandardOutput.Split('\n').Any(value =>
            value.Trim().Replace("utf8", "UTF-8", StringComparison.OrdinalIgnoreCase)
                .Equals(language.LinuxLocale, StringComparison.OrdinalIgnoreCase)) ? language.LinuxLocale : "C.UTF-8";
        var directory = Path.Combine(_configHome, "environment.d");
        var path = Path.Combine(directory, "90-daisyos-language.conf");
        try
        {
            Directory.CreateDirectory(directory);
            await global::DaisyOS.Core.AtomicTextFile.WriteAsync(path,
                $"# DaisyOS user language\nLANG={locale}\nLANGUAGE={language.GettextLanguages}\nLC_MESSAGES={locale}\nLC_ALL=\n", cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return PersonalizationApplyResult.Failure("The language preference could not be saved.");
        }
        // These are compatibility mirrors. Own-session app launches inherit the
        // environment below even if a minimal installation has no D-Bus helper.
        await runner.RunAsync("kwriteconfig6", ["--file", "plasma-localerc", "--group", "Formats", "--key", "LANG", locale], TimeSpan.FromSeconds(5), cancellationToken);
        await runner.RunAsync("kwriteconfig6", ["--file", "plasma-localerc", "--group", "Translations", "--key", "LANGUAGE", language.GettextLanguages], TimeSpan.FromSeconds(5), cancellationToken);
        await runner.RunAsync("dbus-update-activation-environment", ["--systemd", "LANG=" + locale,
            "LANGUAGE=" + language.GettextLanguages, "LC_MESSAGES=" + locale, "LC_ALL="], TimeSpan.FromSeconds(5), cancellationToken);
        ApplyProcessEnvironment(language, locale);
        return PersonalizationApplyResult.Success;
    }

    public static void ApplyProcessEnvironment(LanguageOption language, string? locale = null)
    {
        if (locale is null)
        {
            var inherited = Environment.GetEnvironmentVariable("LANG")?.Replace("utf8", "UTF-8", StringComparison.OrdinalIgnoreCase);
            locale = string.Equals(inherited, language.LinuxLocale, StringComparison.OrdinalIgnoreCase)
                ? language.LinuxLocale : "C.UTF-8";
            try
            {
                var lines = File.ReadAllLines(Path.Combine(GetConfigHome(), "environment.d", "90-daisyos-language.conf"));
                var saved = lines.FirstOrDefault(line => line.StartsWith("LANG=", StringComparison.Ordinal))?[5..];
                if (lines.Contains("LANGUAGE=" + language.GettextLanguages) &&
                    (saved == language.LinuxLocale || saved == "C.UTF-8")) locale = saved;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        Environment.SetEnvironmentVariable("LANG", locale);
        Environment.SetEnvironmentVariable("LANGUAGE", language.GettextLanguages);
        Environment.SetEnvironmentVariable("LC_MESSAGES", locale);
        Environment.SetEnvironmentVariable("LC_ALL", null);
    }
}
