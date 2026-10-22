namespace DaisyOS.Shell.Apps.Files;

/// <summary>Semantic user folders resolved from XDG configuration without executing shell text.</summary>
internal static class UserDirectoryPaths
{
    private static readonly Lazy<IReadOnlyDictionary<string, string>> CurrentPaths = new(() => Load());
    public static IReadOnlyDictionary<string, string> Current => CurrentPaths.Value;

    public static IReadOnlyDictionary<string, string> Load(string? homePath = null, string? configPath = null)
        => DaisyOS.Core.Storage.UserDirectoryPaths.Load(homePath, configPath);
}
