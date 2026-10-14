namespace DaisyOS.Core.Models;

public sealed record LanguageOption(string Tag, string NativeName, string LinuxLocale, string GettextLanguages);

public static class SystemLanguages
{
    public const string DefaultTag = "en-US";
    public static IReadOnlyList<LanguageOption> Supported { get; } =
    [
        new("en-US", "English", "en_US.UTF-8", "en"),
        new("fi-FI", "Suomi", "fi_FI.UTF-8", "fi:en")
    ];

    public static LanguageOption? Find(string? tag) => Supported.FirstOrDefault(language =>
        string.Equals(language.Tag, tag, StringComparison.OrdinalIgnoreCase));

    public static LanguageOption Resolve(string? tag) => Find(tag) ?? Supported[0];
}
