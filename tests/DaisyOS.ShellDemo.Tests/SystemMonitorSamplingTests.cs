using DaisyOS.System.Diagnostics;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SystemMonitorSamplingTests
{
    [Fact]
    public void KernelSourcesAreIndependentAndRatesRequireValidCounterDeltas()
    {
        var root = Path.Combine(Path.GetTempPath(), "daisy-monitor-" + Guid.NewGuid());
        var now = DateTimeOffset.UtcNow;
        try
        {
            Directory.CreateDirectory(Path.Combine(root, "proc", "net"));
            Directory.CreateDirectory(Path.Combine(root, "sys", "block", "sda", "device"));
            Write("stat", "cpu 100 0 100 800 0 0 0 0\ncpu0 100 0 100 800 0 0 0 0\n");
            Write("meminfo", "MemTotal: 8192 kB\nMemAvailable: 4096 kB\nCached: 1024 kB\nSReclaimable: 256 kB\nSwapTotal: 2048 kB\nSwapFree: 1024 kB\n");
            Write("uptime", "3600 0");
            Write("diskstats", "8 0 sda 0 0 100 0 0 0 200 0 0 100 0");
            Write("net/dev", "header\nheader\neth0: 100 0 0 0 0 0 0 0 200 0 0 0 0 0 0 0\n");
            var sampler = new LinuxResourceMonitorService(Path.Combine(root, "proc"), Path.Combine(root, "sys"), () => now);
            var first = sampler.Sample();
            Assert.Null(first.Cpu!.Usage);
            Assert.Null(Assert.Single(first.Disks!).ReadRate);
            Assert.Equal(4096 * 1024, first.Memory!.Used);
            Assert.Equal(1280 * 1024, first.Memory.Cache);
            now = now.AddSeconds(2);
            Write("stat", "cpu 110 0 110 880 0 0 0 0\ncpu0 110 0 110 880 0 0 0 0\n");
            Write("diskstats", "8 0 sda 0 0 104 0 0 0 208 0 0 1100 0");
            Write("net/dev", "header\nheader\neth0: 300 0 0 0 0 0 0 0 600 0 0 0 0 0 0 0\n");
            var second = sampler.Sample();
            Assert.Equal(20, second.Cpu!.Usage!.Value, 5);
            Assert.Equal(1024, Assert.Single(second.Disks!).ReadRate);
            Assert.Equal(50, Assert.Single(second.Disks!).Activity);
            Assert.Equal(100, Assert.Single(second.Networks!).ReceiveRate);
            now = now.AddSeconds(2);
            File.Delete(Path.Combine(root, "proc", "meminfo"));
            Write("net/dev", "header\nheader\neth0: 10 0 0 0 0 0 0 0 20 0 0 0 0 0 0 0\n");
            var third = sampler.Sample();
            Assert.Null(third.Memory);
            Assert.NotNull(third.Cpu);
            Assert.NotNull(third.Disks);
            Assert.Null(Assert.Single(third.Networks!).ReceiveRate);
            Assert.Contains("Memory unavailable", third.Errors);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
        void Write(string path, string text) => File.WriteAllText(Path.Combine(root, "proc", path), text);
    }
}
