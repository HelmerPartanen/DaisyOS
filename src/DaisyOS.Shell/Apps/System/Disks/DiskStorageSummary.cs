namespace DaisyOS.Shell.Apps.System.Disks;

public sealed record StorageSegment(string Label, long Bytes, string BrushKey, string Description);

public sealed record DiskStorageSummary(long Capacity, IReadOnlyList<StorageSegment> Segments,
    string Format, string MountPoints, IReadOnlyList<DiskItem> Volumes)
{
    public string PartitionLayout { get; init; } = "Not reported";

    public static DiskStorageSummary Create(DiskItem selected, IReadOnlyList<DiskItem> items)
    {
        var descendants = new List<DiskItem>();
        void Collect(DiskItem parent)
        {
            foreach (var child in items.Where(x => x.ParentPath == parent.Path && x.Root == selected.Root).DistinctBy(x => x.Path))
                if (!descendants.Any(x => x.Path == child.Path)) { descendants.Add(child); Collect(child); }
        }
        Collect(selected);
        var volumes = descendants.Where(x => x.Format.Length > 0 || !descendants.Any(y => y.ParentPath == x.Path)).ToArray();
        var used = 0L; var free = 0L; var reserved = 0L; var unknown = 0L; var other = 0L; var allocated = 0L;
        void Count(DiskItem node, long limit)
        {
            var size = Math.Clamp(node.Size, 0, limit);
            var children = descendants.Where(x => x.ParentPath == node.Path).DistinctBy(x => x.Path).ToArray();
            if (node.Used is { } u && node.Free is { } f && node.Format.Length > 0)
            {
                // A logical filesystem spanning disks cannot be attributed to one smaller extent.
                if (node.Size > limit) { unknown += size; return; }
                u = Math.Clamp(u, 0, size); f = Math.Clamp(f, 0, size - u);
                used += u; free += f; reserved += size - u - f;
            }
            else if (children.Length > 0)
            {
                var remaining = size;
                foreach (var child in children) { var extent = Math.Clamp(child.Size, 0, remaining); Count(child, extent); remaining -= extent; }
                other += remaining;
            }
            else if (node.Format.Length == 0 || node.Format == "swap") other += size;
            else unknown += size;
        }
        var direct = descendants.Where(x => x.ParentPath == selected.Path).ToArray();
        if (direct.Length > 0 && selected.Used is null)
        {
            foreach (var child in direct) { var extent = Math.Clamp(child.Size, 0, Math.Max(0, selected.Size - allocated)); Count(child, extent); allocated += extent; }
        }
        else if (selected.Type != "disk" || selected.Format.Length > 0) { Count(selected, selected.Size); allocated = selected.Size; }
        var unallocated = Math.Max(0, selected.Size - allocated);
        var segments = new List<StorageSegment>();
        void Add(string label, long bytes, string brush, string description) { if (bytes > 0) segments.Add(new(label, bytes, brush, description)); }
        Add("Used", used, "StorageUsedBrush", "Files stored on mounted volumes.");
        Add("Other volumes", other, "StorageOtherVolumesBrush", "Space allocated to swap, unformatted partitions, or container metadata; it is not available for files.");
        Add("Reserved", reserved, "StorageReservedBrush", "Filesystem metadata and reserved space that is not available to normal users.");
        Add("Usage unknown", unknown, "StorageUnknownBrush", "These volumes are not mounted or do not report usage. Mount a volume to see how much space is used and free.");
        Add("Free", free, "StorageFreeBrush", "Space available for files on mounted volumes.");
        Add("Unallocated", unallocated, "StorageUnallocatedBrush", "Space outside the current volumes, including partition alignment and table overhead. It is not free space inside a filesystem.");
        // A partition table describes the disk layout; a filesystem belongs
        // to the selected volume (or directly to a disk without partitions).
        var format = selected.Format.Length > 0 ? FormatName(selected.Format) : selected.Type == "disk" ? "" : "Unformatted";
        var layout = selected.PartitionTable switch
        {
            "gpt" => "GUID Partition Table (GPT)", "dos" => "Master Boot Record (MBR)",
            "" => "Not reported", _ => selected.PartitionTable.ToUpperInvariant()
        };
        var mounts = new[] { selected }.Concat(descendants).SelectMany(x => x.Mounts).Distinct().OrderBy(x => x.Length).ToArray();
        return new(selected.Size, segments, format, mounts.Length > 0 ? string.Join(", ", mounts) : selected.Type == "disk" ? "No volumes mounted" : "Not mounted", volumes) { PartitionLayout = layout };
    }
    public static string FormatName(string format) => format switch
    {
        "vfat" => "FAT", "ntfs" => "NTFS", "crypto_LUKS" => "LUKS encrypted container", "LVM2_member" => "LVM container", "swap" => "Swap", _ => format
    };
}
