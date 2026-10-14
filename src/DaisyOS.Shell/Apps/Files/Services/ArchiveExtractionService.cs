using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.Files.Services;

internal static class ArchiveExtractionService
{
    public static async Task<bool> ExtractAsync(string archivePath, string destination, ICommandRunner runner)
    {
        Directory.CreateDirectory(destination);
        if (Path.GetExtension(archivePath).Equals(".zip", StringComparison.OrdinalIgnoreCase))
        {
            await Task.Run(() => global::System.IO.Compression.ZipFile.ExtractToDirectory(archivePath, destination, overwriteFiles: true));
            return true;
        }
        var sevenZip = Path.GetExtension(archivePath).Equals(".7z", StringComparison.OrdinalIgnoreCase);
        var result = await runner.RunAsync(sevenZip ? "7z" : "tar", sevenZip
            ? ["x", "-y", archivePath, $"-o{destination}"]
            : ["-xf", archivePath, "-C", destination], TimeSpan.FromMinutes(5));
        if (!result.Succeeded)
            Console.Error.WriteLine($"[files] Archive extraction failed (exit {result.ExitCode}, timed out: {result.TimedOut}): {result.StandardError}");
        return result.Succeeded;
    }
}
