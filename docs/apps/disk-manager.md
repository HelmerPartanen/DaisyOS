# Disk Manager

Open **Disk Manager** from the application launcher, or run `daisyos-diskmanager`. It uses the same standalone application host, single-instance activation, appearance synchronization, and compositor taskbar tracking as Notes and Settings. The shell executable also accepts `--disk-manager` for compatibility.

The sidebar lists the block-device hierarchy reported by `lsblk`, including physical disks, partitions, encrypted mappings, and logical volumes. Select an item to see its capacity, filesystem, mount points, and available usage figures. Physical disks show their partition layout (GPT or MBR) separately from filesystem formats. Each volume shows its own filesystem format; physical disk mount points summarize the contained volumes. Click a volume row to inspect or mount it. The proportional storage bar distinguishes used, free, reserved, other allocated volumes, unknown usage, and unallocated space; unknown usage is never counted as free. Usage is available for mounted filesystems. **Health** requests SMART status for the parent disk; **First Aid** checks the filesystem.

Toolbar actions are also available from the selected item's context menu:

- **Erase** formats a partition as ext4 or FAT. The dialog names the device and states that all its files will be lost.
- **Partition** replaces a physical disk's layout with GPT and one full-size partition. Format the resulting partition with Erase.
- **Mount / Unmount** uses UDisks and its normal authorization policy.
- **First Aid** offers read-only verification or automatic repair of ext2/3/4 and FAT filesystems.
- **Restore / Clone** copies an unmounted source device to another unmounted device of sufficient capacity. Its single dialog identifies the destination and warns that its contents will be lost.

The window uses the same SidebarAppWindow chrome, AppWindowTitleBar, and SystemAppWindowChrome as Notes and Settings, with an opaque SurfaceBaseSolidBrush content surface and a full-height inset sidebar. Device rows use the same shared SidebarNavigationStyles buttons as Files, with identical hover and pressed colors, MotionDurationHover transitions, button height, padding, and SidebarItemGap spacing. Capacity is available in each device tooltip. Storage and device details use the Settings surface, corner radius, and typography tokens. The toolbar uses shared icon buttons and a single Mount/Unmount action; it wraps at compact widths. Dialogs use the shared ConfirmationDialog and themed ComboBox controls. `DiskManagerWindow` composes these controls; `DiskService` owns discovery, safety validation, and argument-based command execution without a shell. Long-running actions show an indeterminate progress bar; cloning also shows transferred-byte output. Every action reports completion or an error with a next step. Closing is blocked while an operation runs.

## Limitations

Requires Linux with `lsblk`, UDisks (`udisksctl`), polkit (`pkexec`), `parted`, `e2fsck`, `fsck.fat`, `mkfs.ext4`, `mkfs.vfat`, `dd`, and `smartctl`. Administrative actions use the system's authentication prompt. SMART may be unavailable for some USB bridges and virtual disks.

Disks containing the running root, home, boot/EFI, or active swap cannot undergo offline operations. All volumes on both participating disks must be unmounted before formatting, partitioning, repair, or cloning. Device identity and capacity are checked again immediately before execution. Physical device removal or replacement during an operation is outside the app's control.

Partitioning currently provides a single whole-disk GPT layout; resizing existing partitions and multi-partition layouts are not implemented. Restore currently copies devices, rather than image files. Encryption/LVM containers are listed, but creation and unlocking are not implemented. Destructive operations must be acceptance-tested on expendable media before production use; development verification does not modify the host's disks.
