using DaisyOS.System.Storage;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxStorageOverviewTests
{
    [Theory]
    [InlineData("/", true)]
    [InlineData("/home", true)]
    [InlineData("/run/media/user/Backup", true)]
    [InlineData("/sys/firmware/efi/efivars", false)]
    [InlineData("/sys/kernel/tracing", false)]
    [InlineData("/efi", false)]
    public void ShowsUserDisksWithoutTechnicalMounts(string path, bool visible) =>
        Assert.Equal(visible, LinuxStorageOverview.IsUserVolume(path));

    [Fact]
    public async Task BreakdownMeasuresChildrenAndKeepsHiddenUnicodeAndNewlineNames()
    {
        if (!OperatingSystem.IsLinux()) return;
        var root = Directory.CreateTempSubdirectory("daisy-storage-details-").FullName;
        try
        {
            var large = Path.Combine(root, "large folder");
            Directory.CreateDirectory(large);
            await File.WriteAllBytesAsync(Path.Combine(large, "data"), new byte[262144]);
            var hidden = Path.Combine(root, ".piilotettu 日本語\nfile");
            await File.WriteAllBytesAsync(hidden, new byte[8192]);
            var excluded = Path.Combine(root, "Known [files]");
            Directory.CreateDirectory(excluded);
            await File.WriteAllBytesAsync(Path.Combine(excluded, "data"), new byte[524288]);
            var entries = await LinuxStorageOverview.ReadEntriesAsync(new("Other files", 0, [root], [excluded]), CancellationToken.None);
            Assert.Equal(2, entries.Count);
            Assert.Equal(large, entries[0].Path);
            Assert.True(entries[0].IsDirectory);
            Assert.Equal(hidden, entries[1].Path);
            Assert.False(entries[1].IsDirectory);
            Assert.True(entries[0].Bytes > entries[1].Bytes);
            Assert.True(entries[1].Bytes > 0);
            Assert.DoesNotContain(entries, entry => entry.Path == root || entry.Path.StartsWith(excluded));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task MeasuredCategoriesDoNotDoubleCountSharedDirectories()
    {
        var temporaryPath = Directory.CreateTempSubdirectory("daisy-storage-test-").FullName;
        var directories = new[] { "Documents", "Downloads", "Pictures", "Videos", "Music" }
            .ToDictionary(name => name, _ => temporaryPath);
        var runner = new ScriptedCommandRunner(new CommandResult(0, $"300\t{temporaryPath}\n", "", false));
        var drive = new StorageDriveOverview(temporaryPath, "Test disk", 2000, 1000, []);
        var result = Assert.Single(await LinuxStorageOverview.MeasureAsync([drive], directories, CancellationToken.None, runner));
        Assert.Equal(1000, result.Categories.Sum(category => category.Bytes));
        Assert.Equal(300, Assert.Single(result.Categories.Where(category => category.Name == "Documents")).Bytes);
        Assert.Equal(700, Assert.Single(result.Categories.Where(category => category.Name == "Other files")).Bytes);
        Assert.DoesNotContain(result.Categories, category => category.Name == "Downloads");
        Assert.Equal(1, runner.Calls.Single().Arguments.Count(argument => argument == temporaryPath));
        Directory.Delete(temporaryPath);
    }
}
