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
    private sealed class EraseHost
    {
        public IReadOnlyList<DiskItem> Items = DiskService.Parse(Fixture.Replace("\"/\"", "null"));
        public readonly List<(string Tool, string[] Args)> Calls = [];
        public bool ReplaceAfterUnmount;
        public bool FailUnmount;
        public DiskService Service => new(Execute);
        private Task<string> Execute(string tool, string[] args, Action<string>? progress, int[]? success)
        {
            if (tool == "lsblk") return Task.FromResult(Json());
            Calls.Add((tool, args));
            if (tool == "udisksctl")
            {
                if (FailUnmount) throw new IOException("Device busy");
                Items = Items.Select(x => x with { Mounts = [] }).ToArray();
                if (ReplaceAfterUnmount) Items = Items.Select(x => x with { Identity = "replaced" }).ToArray();
            }
            if (tool == "pkexec" && args[0] == "/usr/bin/parted")
                Items = [Items[0], Items[1] with { Path = "/dev/test7", Identity = "8:7", Format = "", Mounts = [] }];
            return Task.FromResult("");
        }
        private string Json() => global::System.Text.Json.JsonSerializer.Serialize(new
        {
            blockdevices = Items.Where(x => x.Depth == 0).Select(x => Entry(x))
        });
        private object Entry(DiskItem x) => new Dictionary<string, object?>
        {
            ["path"] = x.Path, ["name"] = x.Name, ["maj:min"] = x.Identity,
            ["type"] = x.Type, ["size"] = x.Size, ["ro"] = x.ReadOnly,
            ["mountpoints"] = x.Mounts, ["fstype"] = x.Format,
            ["children"] = Items.Where(y => y.ParentPath == x.Path).Select(Entry).ToArray()
        };
    }

    [Theory]
    [InlineData("vfat", "msdos", "/usr/bin/mkfs.vfat")]
    [InlineData("ext4", "gpt", "/usr/bin/mkfs.ext4")]
    public async Task WholeDiskEraseCreatesAndFormatsTheDiscoveredPartition(string format, string table, string formatter)
    {
        var host = new EraseHost();
        host.Items = host.Items.Select(x => x with { Mounts = x.Type == "part" ? ["/media/usb"] : [] }).ToArray();
        await host.Service.Operate(host.Items[0], "Erase", format, null, _ => { });
        Assert.Equal("udisksctl", host.Calls[0].Tool);
        Assert.Equal("/usr/bin/wipefs", host.Calls[1].Args[0]);
        var partition = Assert.Single(host.Calls, x => x.Tool == "pkexec" && x.Args[0] == "/usr/bin/parted");
        Assert.Contains(table, partition.Args);
        var mkfs = Assert.Single(host.Calls, x => x.Tool == "pkexec" && x.Args[0] == formatter);
        Assert.Equal("/dev/test7", mkfs.Args.Last());
        Assert.DoesNotContain("/dev/test", mkfs.Args);
        if (format == "vfat") Assert.Contains("32", mkfs.Args);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ReadOnlyOrReplacedDiskAbortsBeforeAnyOperation(bool readOnly)
    {
        var host = new EraseHost();
        var original = host.Items[0];
        host.Items = host.Items.Select(x => x with { ReadOnly = readOnly,
            Identity = readOnly ? x.Identity : "replacement" }).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Service.Operate(original, "Erase", "vfat", null, _ => { }));
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task MountPhysicalDiskUsesItsSingleFormattedPartition()
    {
        var host = new EraseHost();
        await host.Service.Operate(host.Items[0], "Mount", "", null, _ => { });
        Assert.Equal(new[] { "mount", "--block-device", "/dev/test1" }, Assert.Single(host.Calls).Args);
    }

    [Fact]
    public async Task PartitionErasePreservesThePartitionTable()
    {
        var host = new EraseHost();
        await host.Service.Operate(host.Items[1], "Erase", "vfat", null, _ => { });
        var call = Assert.Single(host.Calls);
        Assert.Equal(new[] { "/usr/bin/mkfs.vfat", "-F", "32", "/dev/test1" }, call.Args);
    }

    [Theory]
    [InlineData("/")]
    [InlineData("/efi")]
    [InlineData("[SWAP]")]
    [InlineData("/run/archiso/bootmnt")]
    public async Task WholeDiskEraseNeverUnmountsOrWritesSystemMedia(string mount)
    {
        var host = new EraseHost();
        host.Items = host.Items.Select(x => x with { Mounts = x.Type == "part" ? [mount] : [] }).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Service.Operate(host.Items[0], "Erase", "vfat", null, _ => { }));
        Assert.Empty(host.Calls);
    }

    [Fact]
    public async Task ReplacementDuringUnmountAbortsBeforeAnyWrite()
    {
        var host = new EraseHost { ReplaceAfterUnmount = true };
        host.Items = host.Items.Select(x => x with { Mounts = x.Type == "part" ? ["/media/usb"] : [] }).ToArray();
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Service.Operate(host.Items[0], "Erase", "vfat", null, _ => { }));
        Assert.DoesNotContain(host.Calls, x => x.Tool == "pkexec");
    }

    [Fact]
    public async Task UnmountFailureAbortsBeforeAnyWrite()
    {
        var host = new EraseHost { FailUnmount = true };
        host.Items = host.Items.Select(x => x with { Mounts = x.Type == "part" ? ["/media/usb"] : [] }).ToArray();
        await Assert.ThrowsAsync<IOException>(() => host.Service.Operate(host.Items[0], "Erase", "vfat", null, _ => { }));
        Assert.DoesNotContain(host.Calls, x => x.Tool == "pkexec");
    }

}
