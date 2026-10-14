namespace DaisyOS.Shell.Apps.Files;

/// <summary>Shared location for the user's Trash and its metadata.</summary>
internal static class UserTrashPaths
{
    public static string Root => ResolveRoot(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        Environment.GetEnvironmentVariable("XDG_DATA_HOME"));

    public static string Files => Path.Combine(Root, "files");

    internal static string ResolveRoot(string homePath, string? dataHome) => Path.Combine(
        !string.IsNullOrWhiteSpace(dataHome) && Path.IsPathFullyQualified(dataHome)
            ? Path.GetFullPath(dataHome)
            : Path.Combine(Path.GetFullPath(homePath), ".local", "share"),
        "Trash");
}
