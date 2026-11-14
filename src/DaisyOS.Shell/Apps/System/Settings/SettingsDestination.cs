namespace DaisyOS.Shell.Apps.System.Settings;

/// <summary>Navigation carried through both a new Settings process and single-instance activation.</summary>
public sealed record SettingsDestination(string Category, string? Section = null, string? Setting = null)
{
    public string[] ToArguments() => new[] { "--category=" + Category,
        Section is null ? null : "--section=" + Section,
        Setting is null ? null : "--setting=" + Setting }.OfType<string>().ToArray();

    public static SettingsDestination? FromArguments(string[] arguments)
    {
        string? Read(string prefix) => arguments.FirstOrDefault(arg => arg.StartsWith(prefix, StringComparison.Ordinal))?[prefix.Length..];
        var category = Read("--category=");
        return string.IsNullOrWhiteSpace(category) ? null : new(category, Read("--section="), Read("--setting="));
    }
}
