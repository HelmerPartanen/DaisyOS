using DaisyOS.Shell.Apps.Files.Services;
using DaisyOS.Shell.Apps.System.Disks;
using Xunit;

namespace DaisyOS.Tests;

public class VolumeServiceTests
{
    internal const string Usb = """
        {"blockdevices":[{"path":"/dev/testusb","name":"testusb","model":"DataTraveler 3.0","type":"disk","size":32000000000,"rm":true,"tran":"usb","children":[{"path":"/dev/testusb1","name":"testusb1","type":"part","fstype":"vfat","label":"","mountpoints":[],"size":31900000000}]}]}
        """;
    [Fact]
    public void UnmountedUsbKeepsItsHardwareNameAndCanBeMountedFromSidebar()
    {
        var item = Assert.Single(VolumeService.MergeVolumes(DiskService.Parse(Usb), []));
        Assert.Equal("DataTraveler 3.0", item.Name);
        Assert.Equal("/dev/testusb1", item.DevicePath);
        Assert.Equal("/dev/testusb1", item.Path);
        Assert.False(item.IsMounted);
        Assert.False(item.CanEject);
    }
    [Fact]
    public void MountedUsbUsesItsMountPointAndDoesNotDuplicateFallbackEntry()
    {
        var mount = "/run/media/test/USB";
        var devices = DiskService.Parse(Usb.Replace("\"mountpoints\":[]", "\"mountpoints\":[\"/run/media/test/USB\"]"));
        var item = Assert.Single(VolumeService.MergeVolumes(devices, [new("Connected drive", mount, "vfat", true)]));
        Assert.Equal("DataTraveler 3.0", item.Name);
        Assert.Equal(mount, item.Path);
        Assert.True(item.CanEject);
    }
    [Fact]
    public void ExplicitVolumeLabelsRemainLiteral()
    {
        var devices = DiskService.Parse(Usb.Replace("\"label\":\"\"", "\"label\":\"BIOS USB\""));
        Assert.Equal("BIOS USB", Assert.Single(VolumeService.MergeVolumes(devices, [])).Name);
    }
}
