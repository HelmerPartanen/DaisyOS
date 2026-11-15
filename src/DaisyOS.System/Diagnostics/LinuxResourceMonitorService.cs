using System.Globalization;

namespace DaisyOS.System.Diagnostics;

/// <summary>Independent, read-only kernel sources. One failed source never hides another.</summary>
public sealed class LinuxResourceMonitorService(string procRoot = "/proc", string sysRoot = "/sys", Func<DateTimeOffset>? clock = null)
{
    private readonly Dictionary<string, (long Idle, long Total)> _cpu = new();
    private readonly Dictionary<string, (long Read, long Write, long Busy, DateTimeOffset Time)> _disks = new();
    private readonly Dictionary<string, (long Receive, long Send, DateTimeOffset Time)> _networks = new();
    private string? _model;

    public MonitorResourceSnapshot Sample()
    {
        var now = clock?.Invoke() ?? DateTimeOffset.UtcNow;
        var errors = new List<string>();
        T? Read<T>(string name, Func<T> read) where T : class
        {
            try { return read(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or OverflowException or InvalidDataException)
            { errors.Add($"{name} unavailable"); return null; }
        }
        var cpu = Read("CPU", ReadCpu);
        var memory = Read("Memory", ReadMemory);
        var disks = Read("Disk activity", () => ReadDisks(now));
        var networks = Read("Network activity", () => ReadNetworks(now));
        TimeSpan? uptime = null;
        try { uptime = TimeSpan.FromSeconds(Number(File.ReadAllText(Path.Combine(procRoot, "uptime")).Split(' ')[0])); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException or OverflowException) { errors.Add("Uptime unavailable"); }
        return new(now, cpu, memory, disks, networks, uptime, errors);
    }

    private CpuResource ReadCpu()
    {
        var usage = new List<double?>();
        var names = new List<string>();
        foreach (var line in File.ReadLines(Path.Combine(procRoot, "stat")).Where(l => l.StartsWith("cpu", StringComparison.Ordinal)))
        {
            var p = Fields(line);
            if (p.Length < 5) continue;
            var counters = p.Skip(1).Take(8).Select(Long).ToArray();
            var idle = counters[3] + (counters.Length > 4 ? counters[4] : 0);
            var total = counters.Sum();
            double? percent = null;
            if (_cpu.TryGetValue(p[0], out var previous) && total > previous.Total && idle >= previous.Idle)
                percent = Math.Clamp((1 - (idle - previous.Idle) / (double)(total - previous.Total)) * 100, 0, 100);
            _cpu[p[0]] = (idle, total);
            usage.Add(percent); names.Add(p[0]);
        }
        foreach (var name in _cpu.Keys.Except(names).ToArray()) _cpu.Remove(name);
        if (usage.Count == 0) throw new InvalidDataException();
        _model ??= Optional(() => File.ReadLines(Path.Combine(procRoot, "cpuinfo"))
            .FirstOrDefault(l => l.StartsWith("model name", StringComparison.Ordinal) || l.StartsWith("Hardware", StringComparison.Ordinal))?.Split(':', 2).Last().Trim()) ?? "Processor";
        var load = Optional(() => string.Join(" · ", Fields(File.ReadAllText(Path.Combine(procRoot, "loadavg"))).Take(3))) ?? "Unavailable";
        return new(_model, usage[0], usage.Skip(1).ToArray(), load) { LogicalProcessorNames = names.Skip(1).ToArray() };
    }

    private MemoryResource ReadMemory()
    {
        var values = File.ReadLines(Path.Combine(procRoot, "meminfo")).Select(Fields).Where(p => p.Length >= 2)
            .ToDictionary(p => p[0].TrimEnd(':'), p => Long(p[1]) * 1024);
        if (!values.TryGetValue("MemTotal", out var total) || total <= 0 || !values.TryGetValue("MemAvailable", out var available))
            throw new InvalidDataException();
        double? pressure = null;
        var psi = Optional(() => File.ReadLines(Path.Combine(procRoot, "pressure", "memory")).FirstOrDefault(l => l.StartsWith("some ", StringComparison.Ordinal)));
        var average = psi is null ? null : Fields(psi).FirstOrDefault(p => p.StartsWith("avg10=", StringComparison.Ordinal));
        if (average is not null && double.TryParse(average.AsSpan(6), CultureInfo.InvariantCulture, out var parsed)) pressure = parsed;
        return new(total, Math.Clamp(available, 0, total), Math.Max(0, values.GetValueOrDefault("Cached") + values.GetValueOrDefault("SReclaimable") + values.GetValueOrDefault("Buffers") - values.GetValueOrDefault("Shmem")),
            values.GetValueOrDefault("SwapTotal"), Math.Max(0, values.GetValueOrDefault("SwapTotal") - values.GetValueOrDefault("SwapFree")), pressure);
    }

    private IReadOnlyList<DiskResource> ReadDisks(DateTimeOffset now)
    {
        var result = new List<DiskResource>();
        foreach (var line in File.ReadLines(Path.Combine(procRoot, "diskstats")))
        {
            var p = Fields(line);
            if (p.Length < 14 || !Directory.Exists(Path.Combine(sysRoot, "block", p[2], "device"))) continue;
            var read = Long(p[5]) * 512; var write = Long(p[9]) * 512; var busy = Long(p[12]);
            var known = _disks.TryGetValue(p[2], out var old);
            var elapsed = known ? (now - old.Time).TotalSeconds : 0;
            result.Add(new(p[2], Rate(read, old.Read, elapsed), Rate(write, old.Write, elapsed),
                Rate(busy, old.Busy, elapsed) is { } activity ? Math.Clamp(activity / 10, 0, 100) : null));
            _disks[p[2]] = (read, write, busy, now);
        }
        foreach (var name in _disks.Keys.Except(result.Select(d => d.Name)).ToArray()) _disks.Remove(name);
        return result;
    }

    private IReadOnlyList<NetworkResource> ReadNetworks(DateTimeOffset now)
    {
        var result = new List<NetworkResource>();
        foreach (var line in File.ReadLines(Path.Combine(procRoot, "net", "dev")).Skip(2))
        {
            var p = line.Split(':', 2);
            if (p.Length != 2 || p[0].Trim() == "lo") continue;
            var name = p[0].Trim(); var values = Fields(p[1]);
            if (values.Length < 9) continue;
            var received = Long(values[0]); var sent = Long(values[8]);
            var known = _networks.TryGetValue(name, out var old);
            var elapsed = known ? (now - old.Time).TotalSeconds : 0;
            var state = Optional(() => File.ReadAllText(Path.Combine(sysRoot, "class", "net", name, "operstate")).Trim()) ?? "unknown";
            result.Add(new(name, state, received, sent, Rate(received, old.Receive, elapsed), Rate(sent, old.Send, elapsed)));
            _networks[name] = (received, sent, now);
        }
        foreach (var name in _networks.Keys.Except(result.Select(n => n.Name)).ToArray()) _networks.Remove(name);
        return result;
    }

    public static double? Rate(long current, long previous, double seconds) => seconds > 0 && current >= previous ? (current - previous) / seconds : null;
    private static string[] Fields(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
    private static long Long(string value) => long.Parse(value, CultureInfo.InvariantCulture);
    private static double Number(string value) => double.Parse(value, CultureInfo.InvariantCulture);
    private static string? Optional(Func<string?> read)
    {
        try { return read(); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }
}
