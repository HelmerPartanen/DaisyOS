namespace DaisyOS.Core;

/// <summary>Commits a complete text file without sharing temporary names between writers.</summary>
public static class AtomicTextFile
{
    public static async Task WriteAsync(string path, string contents, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        path = Path.GetFullPath(path);
        var temporary = Prepare(path);
        try
        {
            await File.WriteAllTextAsync(temporary, contents, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, path, overwrite: true);
        }
        finally { RemoveTemporary(temporary); }
    }

    public static void Write(string path, string contents)
    {
        path = Path.GetFullPath(path);
        var temporary = Prepare(path);
        try
        {
            File.WriteAllText(temporary, contents);
            File.Move(temporary, path, overwrite: true);
        }
        finally { RemoveTemporary(temporary); }
    }

    private static string Prepare(string path)
    {
        path = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        return path + "." + Guid.NewGuid().ToString("N") + ".tmp";
    }

    private static void RemoveTemporary(string path)
    {
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }
}
