using DaisyOS.Shell.Apps.System.Disks;
using Xunit;

namespace DaisyOS.Tests;

public class DiskStorageSummaryTests
{
    private const string MixedDisk = """
    {"blockdevices":[{"path":"/dev/test","name":"disk","type":"disk","size":10000,"pttype":"gpt","children":[
      {"path":"/dev/test1","name":"mounted","type":"part","size":6000,"fstype":"ext4","fsused":2000,"fsavail":3500,"mountpoints":["/media/data",null]},
      {"path":"/dev/test2","name":"offline","type":"part","size":2000,"fstype":"ntfs","mountpoints":[null]},
      {"path":"/dev/test3","name":"blank","type":"part","size":1000,"mountpoints":[]}
    ]}]}
    """;
    [Fact] public void PhysicalDiskSeparatesPartitionLayoutFromVolumeFormats()
    {
        var items = DiskService.Parse(MixedDisk); var summary = DiskStorageSummary.Create(items[0], items);
        Assert.Equal("GUID Partition Table (GPT)", summary.PartitionLayout); Assert.Empty(summary.Format);
        Assert.Equal("ext4", DiskStorageSummary.Create(items[1], items).Format);
        Assert.Equal("NTFS", DiskStorageSummary.Create(items[2], items).Format); Assert.Equal("/media/data", summary.MountPoints);
        Assert.Equal(3, summary.Volumes.Count);
        Assert.Equal(summary.Capacity, summary.Segments.Sum(x => x.Bytes));
        Assert.Equal(2000, summary.Segments.Single(x => x.Label == "Used").Bytes);
        Assert.Equal(3500, summary.Segments.Single(x => x.Label == "Free").Bytes);
        Assert.Equal(500, summary.Segments.Single(x => x.Label == "Reserved").Bytes);
        Assert.Equal(2000, summary.Segments.Single(x => x.Label == "Usage unknown").Bytes);
        Assert.Equal(1000, summary.Segments.Single(x => x.Label == "Other volumes").Bytes);
        Assert.Equal(1000, summary.Segments.Single(x => x.Label == "Unallocated").Bytes);
    }
    [Fact] public void OfflineVolumeDoesNotClaimItsCapacityIsFree()
    {
        var items = DiskService.Parse(MixedDisk); var summary = DiskStorageSummary.Create(items[2], items);
        Assert.Equal("NTFS", summary.Format); Assert.Equal("Not mounted", summary.MountPoints);
        Assert.Equal("Usage unknown", Assert.Single(summary.Segments).Label);
        Assert.Equal(2000, summary.Segments[0].Bytes);
    }
    [Fact] public void MountedVolumeUsesItsOwnUsageAndCapacity()
    {
        var items = DiskService.Parse(MixedDisk); var summary = DiskStorageSummary.Create(items[1], items);
        Assert.Equal(6000, summary.Capacity); Assert.Equal(6000, summary.Segments.Sum(x => x.Bytes));
        Assert.DoesNotContain(summary.Segments, x => x.Label == "Unallocated" || x.Label == "Usage unknown");
    }
    [Fact] public void EmptyDiskIsUnallocatedRatherThanFreeFilesystemSpace()
    {
        var items = DiskService.Parse("""{"blockdevices":[{"path":"/dev/blank","name":"blank","type":"disk","size":5000}]}""");
        var summary = DiskStorageSummary.Create(items[0], items);
        Assert.Equal("Unallocated", Assert.Single(summary.Segments).Label);
        Assert.Equal(5000, summary.Segments[0].Bytes);
    }
    [Fact] public void ContainersDoNotDoubleCountTheirChildFilesystem()
    {
        var items = DiskService.Parse("""
        {"blockdevices":[{"path":"/dev/test","name":"disk","type":"disk","size":10000,"children":[
        {"path":"/dev/test1","name":"crypt","type":"part","size":9000,"fstype":"crypto_LUKS","children":[
        {"path":"/dev/mapper/data","name":"data","type":"crypt","size":8500,"fstype":"ext4","fsused":3000,"fsavail":5000,"mountpoints":["/media/data"]}]}]}]}
        """);
        var summary = DiskStorageSummary.Create(items[0], items);
        Assert.Equal(10000, summary.Segments.Sum(x => x.Bytes));
        Assert.Equal(3000, summary.Segments.Single(x => x.Label == "Used").Bytes);
        Assert.Equal(5000, summary.Segments.Single(x => x.Label == "Free").Bytes);
        Assert.Equal("/media/data", summary.MountPoints);
    }
}
