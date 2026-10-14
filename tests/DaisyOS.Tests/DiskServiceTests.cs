using DaisyOS.Shell.Apps.System.Disks;
using Xunit;

namespace DaisyOS.Tests;

public class DiskServiceTests
{
    private const string Fixture = """
    {"blockdevices":[{"path":"/dev/test","name":"test","type":"disk","size":10000,"ro":false,"maj:min":"8:0","mountpoints":[],"children":[{"path":"/dev/test1","name":"test1","type":"part","size":9000,"fstype":"ext4","ro":false,"maj:min":"8:1","mountpoints":["/"],"fsused":100,"fsavail":8000}]}]}
    """;
    [Fact] public void ReadsHierarchyAndUsage()
    {
        var items = DiskService.Parse(Fixture);
        Assert.Equal(2, items.Count); Assert.Equal(1, items[1].Depth);
        Assert.Equal(items[0].Path, items[1].Root); Assert.Equal(8000, items[1].Free);
    }
    [Fact] public void RejectsSystemDiskAndItsVolumes()
    {
        var items = DiskService.Parse(Fixture);
        foreach (var item in items) Assert.Throws<InvalidOperationException>(() => DiskService.Validate(item, items, true));
    }
    [Fact] public void RejectsDisconnectedOrReplacedDevice()
    {
        var items = DiskService.Parse(Fixture.Replace("\"/\"", "null"));
        Assert.Throws<InvalidOperationException>(() => DiskService.Validate(items[0] with { Identity = "different" }, items, true));
        DiskService.Validate(items[0], items, true);
    }
    [Fact] public void RejectsMountedSiblingAndReadOnlyTarget()
    {
        var items = DiskService.Parse(Fixture.Replace("\"/\"", "\"/media/data\""));
        Assert.Throws<InvalidOperationException>(() => DiskService.Validate(items[0], items, true));
        var unmounted = DiskService.Parse(Fixture.Replace("\"/\"", "null"));
        var readOnly = unmounted.Select(x => x with { ReadOnly = true }).ToArray();
        Assert.Throws<InvalidOperationException>(() => DiskService.Validate(readOnly[0], readOnly, true));
    }
    [Fact] public async Task DiscoveryReadsHostWithoutChangingIt()
    {
        var items = await new DiskService().Discover();
        Assert.NotEmpty(items);
        Assert.All(items, item => Assert.StartsWith("/dev/", item.Path));
    }
    [Fact] public async Task CommandErrorsIncludeNextStep()
    {
        var error = await Assert.ThrowsAsync<IOException>(() => DiskService.Run("lsblk", ["--invalid-daisy-option"]));
        Assert.Contains("try again", error.Message);
    }
}
