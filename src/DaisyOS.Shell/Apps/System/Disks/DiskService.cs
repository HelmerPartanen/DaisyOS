using System.Diagnostics;
using System.Text.Json;

namespace DaisyOS.Shell.Apps.System.Disks;

public sealed record DiskItem(string Path, string Identity, string Name, string Type, long Size,
    string Format, string[] Mounts, long? Used, long? Free, bool ReadOnly, int Depth, string Root)
{
    public string ParentPath { get; init; } = "";
    public string PartitionTable { get; init; } = "";
    public Avalonia.Thickness Indent => new(Math.Min(Depth, 3) * 12, 0, 0, 0);
    public string Icon => Type == "disk" ? "hard-drive" : "folder";
    public string Kind => Type switch { "disk" => "Physical disk", "part" => "Volume", "crypt" => "Encrypted container", "lvm" => "Logical volume", _ => "Container" };
    public string Summary => Kind + " · " + Bytes(Size);
    public string Display => new string(' ', Depth * 4) + Name + " · " + Type;
    public bool Mounted => Mounts.Length > 0;
    public static string Bytes(long? size) => size switch
    {
        null => "Unavailable", < 1024 => $"{size} B", < 1048576 => $"{size / 1024d:N2} KiB",
        < 1073741824 => $"{size / 1048576d:N2} MiB", _ => $"{size / 1073741824d:N2} GiB"
    };
    public override string ToString() => Display;
}

public sealed class DiskService
{
    public static IReadOnlyList<DiskItem> Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var result = new List<DiskItem>();
        void Visit(JsonElement e, int depth, string root, string parent)
        {
            string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind != JsonValueKind.Null ? v.ToString() : "";
            long? N(string k) => long.TryParse(S(k), out var n) ? n : null;
            var path = S("path");
            if (depth == 0) root = path;
            var mounts = e.TryGetProperty("mountpoints", out var m) ? m.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => x.Length > 0).ToArray() : [];
            result.Add(new(path, S("maj:min"), S("label") is { Length: > 0 } label ? label : S("model") is { Length: > 0 } model ? model.Trim() : S("name"), S("type"), N("size") ?? 0, S("fstype"), mounts, N("fsused"), N("fsavail"), S("ro") is "True" or "true" or "1", depth, root) { ParentPath = parent, PartitionTable = S("pttype") });
            if (e.TryGetProperty("children", out var children)) foreach (var child in children.EnumerateArray()) Visit(child, depth + 1, root, path);
        }
        foreach (var e in doc.RootElement.GetProperty("blockdevices").EnumerateArray()) Visit(e, 0, "", "");
        return result;
    }
    public async Task<IReadOnlyList<DiskItem>> Discover() => Parse(await Run("lsblk", ["--json", "--bytes", "--output", "PATH,NAME,TYPE,SIZE,FSTYPE,LABEL,MOUNTPOINTS,FSAVAIL,FSUSED,MODEL,RO,MAJ:MIN,PTTYPE"]));

    private static bool SystemMount(string mount)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return mount is "/" or "[SWAP]" or "/efi" or "/boot" or "/usr" or "/var" or "/home"
            || mount.StartsWith("/boot/", StringComparison.Ordinal)
            || (home.Length > 0 && (home == mount || home.StartsWith(mount.TrimEnd('/') + "/", StringComparison.Ordinal)));
    }
    public static void Validate(DiskItem original, IReadOnlyList<DiskItem> current, bool write)
    {
        var item = current.FirstOrDefault(x => x.Path == original.Path && x.Identity == original.Identity && x.Size == original.Size)
            ?? throw new InvalidOperationException("The device changed or was disconnected. Refresh and select it again.");
        var related = current.Where(x => x.Root == item.Root).ToArray();
        if (related.Any(x => x.Mounts.Any(SystemMount)))
            throw new InvalidOperationException("This disk contains the running system or active swap. Boot from another disk to work on it.");
        if (write && item.ReadOnly) throw new InvalidOperationException("This device is read-only. Remove its write protection and try again.");
        if (related.Any(x => x.Mounted)) throw new InvalidOperationException("Unmount all volumes on this disk first, then try again.");
    }
    public async Task Operate(DiskItem item, string action, string option, DiskItem? source, Action<string> progress)
    {
        var current = await Discover();
        if (action is "Mount" or "Unmount")
        {
            var live = current.FirstOrDefault(x => x.Path == item.Path && x.Identity == item.Identity) ?? throw new IOException("Device disconnected. Refresh the list.");
            if (action == "Unmount" && current.Where(x => x.Root == live.Root).Any(x => x.Mounts.Any(SystemMount))) throw new IOException("The running system uses this disk. Boot from another disk to unmount it.");
            await Run("udisksctl", [action.ToLowerInvariant(), "--block-device", item.Path], progress: progress); return;
        }
        if (action == "Health")
        {
            if (!current.Any(x => x.Path == item.Path && x.Identity == item.Identity)) throw new IOException("Device disconnected. Refresh the list.");
            var report = await Run("pkexec", ["/usr/bin/smartctl", "-H", "-j", item.Root], success: Enumerable.Range(0, 256).Where(x => (x & 7) == 0).ToArray());
            using var health = JsonDocument.Parse(report);
            if (!health.RootElement.TryGetProperty("smart_status", out var status)) throw new IOException("This device does not report SMART health. Use First Aid to check its filesystem.");
            progress(status.GetProperty("passed").GetBoolean() ? "SMART health check passed" : "SMART reports a failing disk. Back up your files and replace the disk.");
            return;
        }
        Validate(item, current, action != "Verify");
        string tool; string[] args;
        switch (action)
        {
            case "Erase":
                if (item.Type != "part" || option is not ("ext4" or "vfat")) throw new IOException("Select a partition and a supported format.");
                tool = "mkfs." + option; args = option == "ext4" ? ["-F", item.Path] : [item.Path]; break;
            case "Partition":
                if (item.Type != "disk") throw new IOException("Select a physical disk to partition it.");
                tool = "parted"; args = ["--script", item.Path, "mklabel", "gpt", "mkpart", "primary", "1MiB", "100%"]; break;
            case "Verify": case "Repair":
                if (item.Format is not ("ext2" or "ext3" or "ext4" or "vfat")) throw new IOException("First Aid supports ext2, ext3, ext4 and FAT volumes. Use the filesystem's own repair tool for this format.");
                tool = item.Format == "vfat" ? "fsck.fat" : "e2fsck"; args = [action == "Verify" ? "-n" : "-p", item.Path]; break;
            case "Clone":
                if (source is null || source.Root == item.Root || source.Size > item.Size || source.Size == 0) throw new IOException("Choose a source on another disk that fits the destination.");
                Validate(source, current, false);
                tool = "dd"; args = [$"if={source.Path}", $"of={item.Path}", "bs=4M", "conv=fsync", "status=progress"]; break;
            default: throw new IOException("Unknown disk operation.");
        }
        await Run("pkexec", ["/usr/bin/" + tool, ..args], progress, action == "Repair" ? [0, 1] : [0]);
    }
    public static async Task<string> Run(string tool, string[] args, Action<string>? progress = null, int[]? success = null)
    {
        var info = new ProcessStartInfo(tool) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var p = Process.Start(info) ?? throw new IOException($"Could not start {tool}. Install it and try again.");
        var output = p.StandardOutput.ReadToEndAsync();
        var errors = new global::System.Text.StringBuilder();
        var readErrors = Task.Run(async () => { var buffer = new char[512]; int count; while ((count = await p.StandardError.ReadAsync(buffer)) > 0) { var message = new string(buffer, 0, count); errors.Append(message); progress?.Invoke(message.Trim()); } });
        await p.WaitForExitAsync(); await readErrors;
        var text = await output;
        if (!(success ?? [0]).Contains(p.ExitCode)) throw new IOException($"{tool} could not complete (code {p.ExitCode}). {errors.ToString().Trim()} {text.Trim()} Check that the required tools are installed and administrator authentication was accepted, then try again.");
        return text;
    }
}
