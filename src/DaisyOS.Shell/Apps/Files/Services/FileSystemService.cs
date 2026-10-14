using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DaisyOS.Shell.Apps.Files.Services;

public enum ConflictResolution
{
    Replace,
    Skip,
    Cancel
}

public sealed record TrashOperation(string OriginalPath, string TrashPath, string InfoPath);

public static class FileSystemService
{
    public static async Task<bool> CreateFolderAsync(string parentPath, string folderName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var newPath = Path.Combine(parentPath, folderName);
                if (!Directory.Exists(newPath))
                {
                    Directory.CreateDirectory(newPath);
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        });
    }

    public static async Task<bool> RenameAsync(string currentPath, string newName)
    {
        return await Task.Run(() =>
        {
            try
            {
                var parent = Path.GetDirectoryName(currentPath);
                if (parent == null) return false;
                
                var newPath = Path.Combine(parent, newName);
                if (File.Exists(newPath) || Directory.Exists(newPath)) return false;

                if (Directory.Exists(currentPath))
                {
                    Directory.Move(currentPath, newPath);
                }
                else if (File.Exists(currentPath))
                {
                    File.Move(currentPath, newPath);
                }
                else return false;

                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public static async Task<bool> MoveToTrashAsync(string path) =>
        (await MoveToTrashWithUndoAsync(path)) is not null;

    public static async Task<TrashOperation?> MoveToTrashWithUndoAsync(string path)
    {
        return await Task.Run<TrashOperation?>(() =>
        {
            string? reservedInfo = null;
            try
            {
                var trashDir = UserTrashPaths.Root;
                var filesDir = Path.Combine(trashDir, "files");
                var infoDir = Path.Combine(trashDir, "info");

                Directory.CreateDirectory(filesDir);
                Directory.CreateDirectory(infoDir);

                var fileName = Path.GetFileName(path);
                var destPath = Path.Combine(filesDir, fileName);
                var infoPath = Path.Combine(infoDir, $"{fileName}.trashinfo");

                int i = 1;
                while (File.Exists(destPath) || Directory.Exists(destPath) || File.Exists(infoPath))
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var newName = $"{nameWithoutExt} {i}{ext}";
                    destPath = Path.Combine(filesDir, newName);
                    infoPath = Path.Combine(infoDir, $"{newName}.trashinfo");
                    i++;
                }

                if (!Directory.Exists(path) && !File.Exists(path)) return null;
                var trashInfo = $"[Trash Info]\nPath={Uri.EscapeDataString(path)}\nDeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", global::System.Globalization.CultureInfo.InvariantCulture)}";
                using (var stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write))
                {
                    reservedInfo = infoPath;
                    using var writer = new StreamWriter(stream);
                    writer.Write(trashInfo);
                }

                if (Directory.Exists(path))
                {
                    Directory.Move(path, destPath);
                }
                else if (File.Exists(path))
                {
                    File.Move(path, destPath);
                }
                else throw new IOException("Trash source disappeared during the move.");


                return new TrashOperation(path, destPath, infoPath);
            }
            catch
            {
                if (reservedInfo != null)
                {
                    try { File.Delete(reservedInfo); } catch { }
                }
                return null;
            }
        });
    }

    public static async Task<bool> RestoreFromTrashAsync(TrashOperation operation)
    {
        return await Task.Run(() =>
        {
            try
            {
                if (File.Exists(operation.OriginalPath) || Directory.Exists(operation.OriginalPath))
                {
                    return false;
                }

                var parent = Path.GetDirectoryName(operation.OriginalPath);
                if (string.IsNullOrWhiteSpace(parent)) return false;
                Directory.CreateDirectory(parent);
                if (Directory.Exists(operation.TrashPath)) Directory.Move(operation.TrashPath, operation.OriginalPath);
                else if (File.Exists(operation.TrashPath)) File.Move(operation.TrashPath, operation.OriginalPath);
                else return false;

                if (File.Exists(operation.InfoPath)) File.Delete(operation.InfoPath);
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    public static Task<IReadOnlyList<TrashOperation>> GetTrashOperationsAsync()
    {
        return Task.Run<IReadOnlyList<TrashOperation>>(() =>
        {
            var trashDir = UserTrashPaths.Root;
            var filesDir = Path.Combine(trashDir, "files");
            var infoDir = Path.Combine(trashDir, "info");
            if (!Directory.Exists(infoDir)) return Array.Empty<TrashOperation>();

            var operations = new List<TrashOperation>();
            foreach (var infoPath in Directory.EnumerateFiles(infoDir, "*.trashinfo"))
            {
                var trashName = Path.GetFileNameWithoutExtension(infoPath);
                var trashPath = Path.Combine(filesDir, trashName);
                if ((!File.Exists(trashPath) && !Directory.Exists(trashPath))
                    || !TryReadOriginalPath(infoPath, out var originalPath))
                {
                    continue;
                }

                operations.Add(new TrashOperation(originalPath, trashPath, infoPath));
            }

            return operations;
        });
    }

    public static async Task<bool> RestoreFromTrashAsync(string trashPath)
    {
        var operations = await GetTrashOperationsAsync();
        var operation = operations.FirstOrDefault(candidate =>
            string.Equals(candidate.TrashPath, trashPath, StringComparison.Ordinal));
        return operation is not null && await RestoreFromTrashAsync(operation);
    }

    private static bool TryReadOriginalPath(string infoPath, out string originalPath)
    {
        originalPath = string.Empty;
        try
        {
            foreach (var line in File.ReadLines(infoPath))
            {
                if (!line.StartsWith("Path=", StringComparison.Ordinal)) continue;
                originalPath = Uri.UnescapeDataString(line[5..]);
                return Path.IsPathFullyQualified(originalPath);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or UriFormatException)
        {
            return false;
        }

        return false;
    }

    public static async Task DeletePermanentlyAsync(string path)
    {
        await Task.Run(() =>
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, true);
                }
                else if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch { }
        });
    }

    internal static Task<bool> EmptyTrashAsync(string? trashRoot = null) => Task.Run(() =>
    {
        trashRoot ??= UserTrashPaths.Root;
        var files = Path.Combine(trashRoot, "files");
        var info = Path.Combine(trashRoot, "info");
        var succeeded = true;
        try
        {
            if (Directory.Exists(files))
                foreach (var entry in Directory.EnumerateFileSystemEntries(files))
                {
                    try
                    {
                        if (Directory.Exists(entry)) Directory.Delete(entry, recursive: true);
                        else File.Delete(entry);
                        // Keep restore metadata whenever deleting the payload fails.
                        var metadata = Path.Combine(info, Path.GetFileName(entry) + ".trashinfo");
                        if (File.Exists(metadata)) File.Delete(metadata);
                    }
                    catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                    {
                        succeeded = false;
                        Console.Error.WriteLine($"[trash] Could not delete an item: {exception.Message}");
                    }
                }
            if (Directory.Exists(info))
                foreach (var metadata in Directory.EnumerateFiles(info, "*.trashinfo"))
                {
                    var payload = Path.Combine(files, Path.GetFileName(metadata)[..^10]);
                    if (!File.Exists(payload) && !Directory.Exists(payload)) File.Delete(metadata);
                }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"[trash] Could not empty trash: {exception.Message}");
            succeeded = false;
        }
        return succeeded;
    });

    public static async Task<bool> CopyAsync(string sourcePath, string destPath, Func<string, Task<ConflictResolution>> onConflict)
    {
        using var busyCursor = DaisyOS.System.Processes.BusyCursor.Begin();
        return await Task.Run(async () => await CopyRecursiveCoreAsync(sourcePath, destPath, onConflict));
    }

    private static async Task<bool> CopyRecursiveCoreAsync(string sourcePath, string destPath, Func<string, Task<ConflictResolution>> onConflict)
    {
        try
        {
            if (File.Exists(destPath) || Directory.Exists(destPath))
            {
                var resolution = await onConflict(destPath);
                if (resolution == ConflictResolution.Cancel) return false;
                if (resolution == ConflictResolution.Skip) return true;
                if (resolution == ConflictResolution.Replace)
                {
                    if (File.Exists(destPath)) File.Delete(destPath);
                    else if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                }
            }

            if (Directory.Exists(sourcePath))
            {
                Directory.CreateDirectory(destPath);
                foreach (var file in Directory.GetFiles(sourcePath))
                {
                    var destFile = Path.Combine(destPath, Path.GetFileName(file));
                    if (!await CopyRecursiveCoreAsync(file, destFile, onConflict)) return false;
                }
                foreach (var dir in Directory.GetDirectories(sourcePath))
                {
                    var destDir = Path.Combine(destPath, Path.GetFileName(dir));
                    if (!await CopyRecursiveCoreAsync(dir, destDir, onConflict)) return false;
                }
            }
            else if (File.Exists(sourcePath))
            {
                File.Copy(sourcePath, destPath);
            }
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static async Task<bool> MoveAsync(string sourcePath, string destPath, Func<string, Task<ConflictResolution>> onConflict)
    {
        using var busyCursor = DaisyOS.System.Processes.BusyCursor.Begin();
        return await Task.Run(async () =>
        {
            try
            {
                if (File.Exists(destPath) || Directory.Exists(destPath))
                {
                    var resolution = await onConflict(destPath);
                    if (resolution == ConflictResolution.Cancel) return false;
                    if (resolution == ConflictResolution.Skip) return true;
                    if (resolution == ConflictResolution.Replace)
                    {
                        if (File.Exists(destPath)) File.Delete(destPath);
                        else if (Directory.Exists(destPath)) Directory.Delete(destPath, true);
                    }
                }

                if (Directory.Exists(sourcePath))
                {
                    try
                    {
                        Directory.Move(sourcePath, destPath);
                    }
                    catch (IOException)
                    {
                        // Moving between different mounted drives is a copy followed by
                        // deletion because the filesystem cannot rename across devices.
                        if (!await CopyAsync(sourcePath, destPath, onConflict))
                        {
                            TryDeletePath(destPath);
                            return false;
                        }
                        Directory.Delete(sourcePath, true);
                    }
                }
                else if (File.Exists(sourcePath))
                {
                    try
                    {
                        File.Move(sourcePath, destPath);
                    }
                    catch (IOException)
                    {
                        if (!await CopyAsync(sourcePath, destPath, onConflict))
                        {
                            TryDeletePath(destPath);
                            return false;
                        }
                        File.Delete(sourcePath);
                    }
                }
                else return false;
                return true;
            }
            catch
            {
                return false;
            }
        });
    }

    private static void TryDeletePath(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, true);
            else if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // The original remains untouched; a later cleanup can remove any
            // inaccessible partial destination without risking source data.
        }
    }
}
