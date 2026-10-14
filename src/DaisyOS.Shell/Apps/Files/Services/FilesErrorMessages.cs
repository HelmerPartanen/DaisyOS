using DaisyOS.Shell.Services.Localization;

namespace DaisyOS.Shell.Apps.Files.Services;

internal static class FilesErrorMessages
{
    public static string Describe(string action, Exception error)
    {
        Console.Error.WriteLine($"[files] {action}: {error.Message}");
        var recovery = error switch
        {
            UnauthorizedAccessException => "Check that you have permission to access these files.",
            FileNotFoundException or DirectoryNotFoundException => "The file or folder is no longer available.",
            IOException => "Check that the drive is connected and has enough free space, then try again.",
            _ => "Try again in a moment."
        };
        return $"{Loc.Get(action)}. {Loc.Get(recovery)}";
    }
}
