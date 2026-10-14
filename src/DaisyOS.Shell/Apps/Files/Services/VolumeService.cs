using System;
using System.Collections.Generic;
using System.IO;

namespace DaisyOS.Shell.Apps.Files.Services;

public record VolumeInfo(string Name, string Path, string Type, bool IsRemovable);

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
