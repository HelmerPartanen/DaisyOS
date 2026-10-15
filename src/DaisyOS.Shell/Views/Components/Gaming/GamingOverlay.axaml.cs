using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DaisyOS.System.Diagnostics;
using System.Globalization;
using System.Diagnostics;
using System.ComponentModel;
using System.IO;

namespace DaisyOS.Shell.Views.Components.Gaming;

public partial class GamingOverlay : UserControl
{
    private readonly LinuxSystemMetricsService _metrics = new();
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
    private bool _sampling;
    private int _generation;

    public GamingOverlay()
    {
        AvaloniaXamlLoader.Load(this);
        _timer.Tick += async (_, _) => await SampleAsync();
        DetachedFromVisualTree += (_, _) => _timer.Stop();
    }

    public void Toggle()
    {
        IsVisible = !IsVisible;
        _generation++;
        if (IsVisible)
        {
            foreach (var name in new[] { "FPS", "CPU", "GPU", "MEM" }) Set(name, "—");
            _timer.Start();
            _ = SampleAsync();
        }
        else _timer.Stop();
    }

    private void Set(string name, string value) => this.FindControl<TextBlock>(name + "Value")!.Text = value;

    private async Task SampleAsync()
    {
        if (_sampling || !IsVisible) return;
        _sampling = true;
        var generation = _generation;
        try
        {
            var status = await _metrics.GetStatusAsync();
            var telemetry = await Task.Run(async () =>
            {
                (double? Fps, double? Gpu) sample;
                try { sample = ReadTelemetry(); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { sample = (null, null); }
                return (sample.Fps, sample.Gpu ?? await ReadNvidiaUsageAsync());
            });
            if (!IsVisible || generation != _generation) return;
            Set("CPU", status.CpuUsagePercent is { } cpu ? $"{cpu:0}%" : "—");
            Set("MEM", status.MemoryUsedBytes is { } memory ? $"{memory / 1073741824d:0.0} GB" : "—");
            Set("GPU", telemetry.Item2 is { } gpu ? $"{gpu:0}%" : "—");
            Set("FPS", telemetry.Item1 is { } fps ? $"{fps:0}" : "—");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        finally { _sampling = false; }
    }

    private static (double? Fps, double? Gpu) ReadTelemetry()
    {
        double? gpu = null;
        // DRM exposes real device utilization on supported drivers. Select the busiest GPU.
        foreach (var path in Directory.EnumerateDirectories("/sys/class/drm", "card*")
            .Select(card => Path.Combine(card, "device", "gpu_busy_percent")).Where(File.Exists))
        {
            if (double.TryParse(File.ReadAllText(path), out var value) && value is >= 0 and <= 100)
                gpu = Math.Max(gpu ?? 0, value);
        }
        double? fps = null;
        // MangoHud CSV logging is optional; never substitute shell frame rate for game FPS.
        var directory = Environment.GetEnvironmentVariable("DAISYOS_MANGOHUD_LOG_DIR");
        if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
        {
            var file = new DirectoryInfo(directory).EnumerateFiles("*.csv")
                .Where(file => !file.Name.EndsWith("_summary.csv", StringComparison.OrdinalIgnoreCase) &&
                    DateTime.UtcNow - file.LastWriteTimeUtc < TimeSpan.FromSeconds(5))
                .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
            if (file is not null)
            {
                var logged = ReadMangoHudLog(file.FullName);
                fps = logged.Fps;
                gpu = logged.Gpu ?? gpu;
            }
        }
        return (fps, gpu);
    }

    private static async Task<double?> ReadNvidiaUsageAsync()
    {
        if (!File.Exists("/usr/bin/nvidia-smi")) return null;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo("/usr/bin/nvidia-smi")
            {
                UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true,
                CreateNoWindow = true,
                ArgumentList = { "--query-gpu=utilization.gpu", "--format=csv,noheader,nounits" }
            }
        };
        using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));
        try
        {
            process.Start();
            var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var error = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var text = await output;
            await error;
            if (process.ExitCode != 0) return null;
            var loads = text.Split('\n').Select(line =>
                double.TryParse(line.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                value is >= 0 and <= 100 ? (double?)value : null).OfType<double>().ToArray();
            return loads.Length > 0 ? loads.Max() : null;
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            return null;
        }
        catch (Exception ex) when (ex is Win32Exception or IOException or InvalidOperationException) { return null; }
    }

    internal static (double? Fps, double? Gpu) ReadMangoHudLog(string path)
    {
        double? fps = null;
        double? gpu = null;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        // Bound work even when a game has been logging for hours.
        using var reader = new StreamReader(stream);
        string? line;
        string[]? headers = null;
        string[]? latest = null;
        for (var i = 0; i < 8 && (line = reader.ReadLine()) is not null; i++)
        {
            var fields = line.Split(',').Select(field => field.Trim()).ToArray();
            if (fields.Contains("fps")) { headers = fields; break; }
        }
        if (headers is not null)
        {
            if (stream.Length > 16384) { stream.Seek(-16384, SeekOrigin.End); reader.DiscardBufferedData(); reader.ReadLine(); }
            while ((line = reader.ReadLine()) is not null)
            {
                var fields = line.Split(',');
                if (fields.Length == headers.Length) latest = fields;
            }
            if (latest is not null)
            {
                double? Read(string key) => Array.IndexOf(headers, key) is var index && index >= 0 &&
                    double.TryParse(latest[index], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) &&
                    double.IsFinite(value) && value >= 0 ? value : null;
                fps = Read("fps");
                gpu = Read("gpu_load") ?? gpu;
            }
        }
        return (fps, gpu);
    }
}
