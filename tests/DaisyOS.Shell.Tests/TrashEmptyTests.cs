using DaisyOS.Shell.Apps.Files.Services;
using Xunit;

namespace DaisyOS.Shell.Tests;

public sealed class TrashEmptyTests
{
    [Fact]
    public async Task EmptyRemovesOwnedPayloadsAndMetadataButPreservesSymlinkTargets()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-empty-" + Guid.NewGuid());
        var trash = Path.Combine(root, "Trash");
        var files = Path.Combine(trash, "files");
        var info = Path.Combine(trash, "info");
        Directory.CreateDirectory(files);
        Directory.CreateDirectory(info);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(files, "file"), "owned");
            Directory.CreateDirectory(Path.Combine(files, "folder"));
            await File.WriteAllTextAsync(Path.Combine(files, "folder", "nested"), "owned");
            foreach (var name in new[] { "file", "folder", "orphan" })
                await File.WriteAllTextAsync(Path.Combine(info, name + ".trashinfo"), "owned metadata");
            var target = Path.Combine(root, "preserved");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "safe"), "preserve");
            if (OperatingSystem.IsLinux()) Directory.CreateSymbolicLink(Path.Combine(files, "link"), target);
            Assert.True(await FileSystemService.EmptyTrashAsync(trash));
            Assert.Empty(Directory.EnumerateFileSystemEntries(files));
            Assert.Empty(Directory.EnumerateFileSystemEntries(info));
            Assert.Equal("preserve", await File.ReadAllTextAsync(Path.Combine(target, "safe")));
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public async Task FailedDirectoryDeletionKeepsItsRestoreMetadata()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Path.Combine(Path.GetTempPath(), "daisy-empty-denied-" + Guid.NewGuid());
        var files = Path.Combine(root, "files");
        var info = Path.Combine(root, "info");
        var denied = Path.Combine(files, "denied");
        Directory.CreateDirectory(denied);
        Directory.CreateDirectory(info);
        await File.WriteAllTextAsync(Path.Combine(denied, "keep"), "owned");
        var metadata = Path.Combine(info, "denied.trashinfo");
        await File.WriteAllTextAsync(metadata, "restore metadata");
        File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        try
        {
            Assert.False(await FileSystemService.EmptyTrashAsync(root));
            Assert.True(File.Exists(metadata));
            Assert.True(File.Exists(Path.Combine(denied, "keep")));
        }
        finally
        {
            File.SetUnixFileMode(denied, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            Directory.Delete(root, recursive: true);
        }
    }
}
