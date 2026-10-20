using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DaisyOS.Shell.Apps.System.Disks;

namespace DaisyOS.Shell.Apps.Files.Services;

public record VolumeInfo(string Name, string Path, string Type, bool IsRemovable)
{
    public string DevicePath { get; init; } = "";
    public bool IsMounted => !Path.StartsWith("/dev/", StringComparison.Ordinal);
    public bool CanEject => IsRemovable && IsMounted;
}

public static class VolumeService
{
    private static readonly HashSet<string> VirtualFileSystems = new(StringComparer.OrdinalIgnoreCase)
    {
        "autofs", "bpf", "cgroup", "cgroup2", "configfs", "debugfs", "devpts", "devtmpfs",
        "efivarfs", "fusectl", "hugetlbfs", "mqueue", "overlay", "proc", "pstore", "securityfs",
        "sysfs", "tmpfs", "tracefs"
    };

    public static List<VolumeInfo> GetVolumes()
    {
        var volumes = new List<VolumeInfo>();
        var usedNames = new HashSet<string>(StringComparer.CurrentCultureIgnoreCase);
        try
        {
            var drives = DriveInfo.GetDrives();
            foreach (var drive in drives)
            {
                if (IsUserVisibleStorage(drive))
                {
                    bool isRemovable = drive.DriveType == DriveType.Removable || drive.DriveType == DriveType.CDRom || drive.Name.StartsWith("/run/media") || drive.Name.StartsWith("/media");
                    var baseName = string.IsNullOrWhiteSpace(drive.VolumeLabel)
                        ? (isRemovable ? "Connected drive" : "Hard drive")
                        : drive.VolumeLabel.Trim();
                    var name = baseName;
                    for (var index = 2; !usedNames.Add(name); index++)
                    {
                        name = $"{baseName} {index}";
                    }
                    
                    volumes.Add(new VolumeInfo(name, drive.Name, drive.DriveFormat, isRemovable));
                }
            }
        }
        catch { }

        return volumes;
    }

    public static async Task<List<VolumeInfo>> GetVolumesAsync()
    {
        var mounted = await Task.Run(GetVolumes);
        try { return MergeVolumes(await new DiskService().Discover(), mounted); }
        catch { return mounted; }
    }

    internal static List<VolumeInfo> MergeVolumes(IReadOnlyList<DiskItem> devices, IEnumerable<VolumeInfo> mounted)
    {
        var result = new List<VolumeInfo>();
        var represented = new HashSet<string>(StringComparer.Ordinal);
        foreach (var device in devices.Where(x => x.Format.Length > 0 && x.Type is "disk" or "part" or "crypt" or "lvm"))
        {
            var root = devices.FirstOrDefault(x => x.Path == device.Root) ?? device;
            var removable = root.Removable || root.Transport == "usb";
            var mount = device.Mounts.Where(IsVisibleMount).OrderBy(x => x.Length).FirstOrDefault();
            if (mount is null && !removable) continue;
            if (VirtualFileSystems.Contains(device.Format)) continue;
            // A filesystem label names a volume; an unlabeled USB uses its hardware model.
            var name = device.VolumeLabel.Length > 0 ? device.VolumeLabel :
                root.Model.Length > 0 ? root.Model : removable ? "Connected drive" : device.Name;
            result.Add(new VolumeInfo(name, mount ?? device.Path, device.Format, removable) { DevicePath = device.Path });
            foreach (var path in device.Mounts) represented.Add(path);
        }
        result.AddRange(mounted.Where(x => !represented.Contains(x.Path)));
        return result;
    }

    private static bool IsVisibleMount(string path) => path.Length > 0 &&
        path is not ("/efi" or "/boot" or "/boot/efi") &&
        (!path.StartsWith("/run/", StringComparison.Ordinal) || path.StartsWith("/run/media/", StringComparison.Ordinal));

    public static async Task<string> MountAsync(VolumeInfo volume)
    {
        if (volume.IsMounted) return volume.Path;
        if (volume.DevicePath.Length == 0) throw new IOException("This drive is no longer available.");
        await DiskService.Run("udisksctl", ["mount", "--block-device", volume.DevicePath]);
        var live = (await new DiskService().Discover()).FirstOrDefault(x => x.Path == volume.DevicePath);
        return live?.Mounts.FirstOrDefault(IsVisibleMount) ?? throw new IOException("The drive did not provide a mount point.");
    }

    private static bool IsUserVisibleStorage(DriveInfo drive)
    {
        if (!drive.IsReady || drive.DriveType is DriveType.Ram or DriveType.NoRootDirectory)
        {
            return false;
        }

        var mountPath = drive.Name.TrimEnd(Path.DirectorySeparatorChar);
        if (string.IsNullOrEmpty(mountPath)) mountPath = Path.DirectorySeparatorChar.ToString();
        if (VirtualFileSystems.Contains(drive.DriveFormat)
            || mountPath.StartsWith("/sys", StringComparison.Ordinal)
            || mountPath.StartsWith("/proc", StringComparison.Ordinal)
            || mountPath.StartsWith("/dev", StringComparison.Ordinal)
            || mountPath is "/boot" or "/boot/efi" or "/efi")
        {
            return false;
        }

        // Runtime mounts are session plumbing unless they are a user-mounted
        // drive or a GVFS location exposed as a real file-system destination.
        return !mountPath.StartsWith("/run", StringComparison.Ordinal)
            || mountPath.StartsWith("/run/media", StringComparison.Ordinal)
            || mountPath.EndsWith("/gvfs", StringComparison.Ordinal);
    }
}
