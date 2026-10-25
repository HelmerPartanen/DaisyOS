using System.Diagnostics;
using System.Globalization;

namespace DaisyOS.System.Diagnostics;

public sealed record SystemActivity(double? ReadBytesPerSecond, double? WriteBytesPerSecond,
    double? ReceiveBytesPerSecond, double? SendBytesPerSecond, long SwapUsed, long SwapTotal, TimeSpan Uptime);

/// <summary>Samples cumulative kernel counters. First samples and counter resets have no rate.</summary>
public sealed class LinuxActivitySampler
{
    private (double Time, long Read, long Write, long Receive, long Send)? _previous;

    public SystemActivity Sample()
    {
        long read = 0, write = 0, receive = 0, send = 0;
        foreach (var line in File.ReadLines("/proc/diskstats"))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            // Count whole physical devices, excluding partitions and stacked virtual devices.
            if (p.Length < 14 || !Directory.Exists($"/sys/block/{p[2]}/device")) continue;
            read += long.Parse(p[5], CultureInfo.InvariantCulture) * 512;
            write += long.Parse(p[9], CultureInfo.InvariantCulture) * 512;
        }
        foreach (var line in File.ReadLines("/proc/net/dev").Skip(2))
        {
            var p = line.Split(':', 2);
            if (p.Length != 2 || p[0].Trim() == "lo") continue;
            var v = p[1].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            receive += long.Parse(v[0], CultureInfo.InvariantCulture);
            send += long.Parse(v[8], CultureInfo.InvariantCulture);
        }
        var memory = File.ReadLines("/proc/meminfo").Select(l => l.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .Where(p => p.Length >= 2).ToDictionary(p => p[0], p => long.Parse(p[1], CultureInfo.InvariantCulture) * 1024);
        var uptime = double.Parse(File.ReadAllText("/proc/uptime").Split(' ')[0], CultureInfo.InvariantCulture);
        var now = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        var previous = _previous;
        _previous = (now, read, write, receive, send);
        double? Rate(long value, long old) => previous is { } prev && now > prev.Time && value >= old ? (value - old) / (now - prev.Time) : null;
        return new(Rate(read, previous?.Read ?? read), Rate(write, previous?.Write ?? write),
            Rate(receive, previous?.Receive ?? receive), Rate(send, previous?.Send ?? send),
            memory.GetValueOrDefault("SwapTotal:") - memory.GetValueOrDefault("SwapFree:"),
            memory.GetValueOrDefault("SwapTotal:"), TimeSpan.FromSeconds(uptime));
    }
}
