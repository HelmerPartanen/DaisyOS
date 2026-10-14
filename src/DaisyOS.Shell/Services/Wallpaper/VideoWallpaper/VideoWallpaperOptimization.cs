using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Runtime.InteropServices;
using DaisyOS.Shell.Services.Serialization;

namespace DaisyOS.Shell.Services.Wallpaper.VideoWallpaper;

public sealed record VideoOptimizationStatus(string Source, string Message, double Progress, bool Preparing);

/// <summary>One low-priority conversion at a time; sources are never rewritten.</summary>
public static class VideoWallpaperOptimization
{
    private const long MaximumCacheBytes = 5L * 1024 * 1024 * 1024;
    private const long FreeSpaceReserve = 2L * 1024 * 1024 * 1024;
    private static readonly SemaphoreSlim ConversionGate = new(1, 1);
    private static readonly ConcurrentDictionary<string, Job> Jobs = new();
    private static readonly ConcurrentDictionary<string, int> ActiveFiles = new();
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<long, bool>> Demands = new();
    private static readonly object ConverterSync = new();
    private static long _nextDemand;
    private static Process? _converter;
    private static string? _converterSource;
    private static volatile bool _converterPaused;

    internal sealed class PlaybackDemand(string source) : IDisposable
    {
        private readonly long _id = Interlocked.Increment(ref _nextDemand);
        private bool _disposed;
        public void SetVisible(bool visible)
        {
            if (_disposed) return;
            Demands.GetOrAdd(source, _ => new())[_id] = visible;
            ApplyConversionVisibility();
        }
        public void Dispose()
        {
            _disposed = true;
            if (Demands.TryGetValue(source, out var demands)) demands.TryRemove(_id, out _);
            ApplyConversionVisibility();
        }
    }
    private static void ApplyConversionVisibility()
    {
        if (!OperatingSystem.IsLinux()) return;
        lock (ConverterSync)
        {
            if (_converter is null || _converterSource is null) return;
            var hidden = Demands.TryGetValue(_converterSource, out var demands) && !demands.Values.Any(visible => visible);
            if (hidden == _converterPaused) return;
            try
            {
                if (!_converter.HasExited && Signal(_converter.Id, hidden ? 19 : 18) == 0) _converterPaused = hidden;
            }
            catch (InvalidOperationException) { }
        }
    }
    [DllImport("libc", EntryPoint = "kill", SetLastError = true)]
    private static extern int Signal(int pid, int signal);

    public static event Action<VideoOptimizationStatus>? StatusChanged;
    public static VideoOptimizationStatus? CurrentStatus { get; private set; }
    public static VideoOptimizationStatus? ReadStatus()
    {
        try
        {
            var path = Path.Combine(CacheDirectory, "status.json");
            return File.Exists(path) ? JsonSerializer.Deserialize(File.ReadAllText(path), DaisyShellJsonContext.Default.VideoOptimizationStatus) : CurrentStatus;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException) { return CurrentStatus; }
    }
    private sealed class Job
    {
        public readonly CancellationTokenSource Cancellation = new();
        public Task<string> Task = null!;
        public int Users;
    }

    public static (int Width, int Height) CoverSize(VideoMetadata source, int width, int height)
    {
        var rotated = Math.Abs(source.Rotation) % 180 == 90;
        var sw = rotated ? source.Height : source.Width;
        var sh = rotated ? source.Width : source.Height;
        var scale = Math.Min(1.0, Math.Max((double)width / sw, (double)height / sh));
        // Encode in the original orientation, preserving the rotation metadata.
        return (Math.Max(2, Math.Min(source.Width & ~1, (int)Math.Ceiling(source.Width * scale / 2) * 2)),
            Math.Max(2, Math.Min(source.Height & ~1, (int)Math.Ceiling(source.Height * scale / 2) * 2)));
    }
    private static string CacheDirectory => Path.Combine(Environment.GetEnvironmentVariable("XDG_CACHE_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache"), "DaisyOS", "video-wallpapers");

    public static IDisposable KeepActive(string path)
    {
        ActiveFiles.AddOrUpdate(path, 1, (_, count) => count + 1);
        return new ActiveLease(path);
    }
    private sealed class ActiveLease(string path) : IDisposable
    {
        private int _disposed;
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) ActiveFiles.AddOrUpdate(path, 0, (_, count) => Math.Max(0, count - 1));
        }
    }

    public static async Task<string> PrepareAsync(string source, VideoMetadata metadata, int width, int height,
        bool force, CancellationToken token)
    {
        if (!force && metadata.Fps <= 60.01) return source;
        var size = CoverSize(metadata, width, height);
        // Sharing the task prevents desktop/lockscreen instances from racing
        // to generate the same derivative. Hashing is streamed off the UI thread.
        var digest = await Task.Run(async () =>
        {
            await using var input = File.OpenRead(source);
            return Convert.ToHexString(await SHA256.HashDataAsync(input, token));
        }, token);
        var key = $"v3-{digest}-{size.Width}x{size.Height}-60-main10-crf16";
        var target = Path.Combine(CacheDirectory, key + ".mp4");
        if (File.Exists(target))
        {
            File.SetLastAccessTimeUtc(target, DateTime.UtcNow);
            Publish(source, "High-quality optimized copy ready", 1, false);
            return target;
        }
        Job job;
        lock (Jobs)
        {
            if (!Jobs.TryGetValue(key, out job!))
            {
                job = new Job();
                job.Task = ConvertAsync(source, target, metadata, size, job.Cancellation.Token);
                Jobs[key] = job;
            }
            job.Users++;
        }
        try { return await job.Task.WaitAsync(token); }
        finally
        {
            lock (Jobs)
            {
                if (--job.Users == 0)
                {
                    if (!job.Task.IsCompleted) job.Cancellation.Cancel();
                    Jobs.TryRemove(key, out _);
                }
            }
        }
    }

    private static void Publish(string source, string message, double progress, bool preparing)
    {
        var status = new VideoOptimizationStatus(source, message, Math.Clamp(progress, 0, 1), preparing);
        CurrentStatus = status;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var json = JsonSerializer.Serialize(status, DaisyShellJsonContext.Default.VideoOptimizationStatus);
            global::DaisyOS.Core.AtomicTextFile.Write(Path.Combine(CacheDirectory, "status.json"), json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        StatusChanged?.Invoke(status);
    }
    private static async Task<string> ConvertAsync(string source, string target, VideoMetadata metadata,
        (int Width, int Height) size, CancellationToken token)
    {
        await ConversionGate.WaitAsync(token);
        var temporary = target + ".partial.mp4";
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            // A process crash can leave an unpublished temporary file. Never
            // remove a current job; the conversion gate owns this operation.
            foreach (var stale in new DirectoryInfo(CacheDirectory).GetFiles("*.partial.mp4"))
                if (stale.LastWriteTimeUtc < DateTime.UtcNow.AddHours(-1)) stale.Delete();
            EvictInactiveFiles();
            if (new DriveInfo(Path.GetPathRoot(CacheDirectory)!).AvailableFreeSpace < FreeSpaceReserve)
                throw new IOException("Not enough free space for an optimized wallpaper.");
            Publish(source, "Preparing a high-quality wallpaper copy…", 0, true);
            var info = new ProcessStartInfo("setpriv") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--pdeathsig", "KILL", "nice", "-n", "10", "ffmpeg", "-nostdin", "-hide_banner", "-loglevel", "error", "-y",
                "-threads", "2", "-noautorotate", "-i", source, "-map", "0:v:0", "-an", "-sn", "-dn", "-filter_threads", "2",
                "-vf", $"{ColorFilter(metadata)}fps=fps=60:round=near,scale={size.Width}:{size.Height}:flags=lanczos,format=yuv420p10le{ColorFilter(metadata, trailing: true)}",
                "-c:v", "libx265", "-preset", "medium", "-crf", "16", "-x265-params", EncoderParameters(metadata),
                "-tag:v", "hvc1", "-movflags", "+faststart", "-progress", "pipe:1", "-nostats" }) info.ArgumentList.Add(arg);
            foreach (var (name, value) in new[] { ("-color_primaries", metadata.Primaries), ("-color_trc", metadata.Transfer),
                ("-colorspace", metadata.Matrix), ("-color_range", metadata.Range) })
                if (value is not ("" or "unknown" or "unspecified")) { info.ArgumentList.Add(name); info.ArgumentList.Add(value); }
            info.ArgumentList.Add(temporary);
            using var process = Process.Start(info) ?? throw new IOException("Cannot prepare wallpaper.");
            lock (ConverterSync) { _converter = process; _converterSource = source; _converterPaused = false; }
            ApplyConversionVisibility();
            // Decoder, filters and encoder share two CPUs, rather than each
            // independently creating two busy workers.
            if (OperatingSystem.IsLinux())
            {
                try
                {
                    using var current = Process.GetCurrentProcess();
                    var allowed = (ulong)current.ProcessorAffinity.ToInt64();
                    ulong selected = 0;
                    for (var bit = 0; bit < 64 && global::System.Numerics.BitOperations.PopCount(selected) < 2; bit++)
                        if ((allowed & (1UL << bit)) != 0) selected |= 1UL << bit;
                    if (selected != 0) process.ProcessorAffinity = (nint)selected;
                }
                catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or PlatformNotSupportedException or InvalidOperationException) { }
            }
            using var cancellation = token.Register(() => { try { process.Kill(true); } catch (InvalidOperationException) { } });
            var stderr = process.StandardError.ReadToEndAsync(token);
            while (await process.StandardOutput.ReadLineAsync(token) is { } line)
            {
                if (line.StartsWith("out_time_us=", StringComparison.Ordinal) && long.TryParse(line.AsSpan(12), out var us))
                    Publish(source, _converterPaused ? "Preparation paused while wallpaper is hidden" : "Preparing a high-quality wallpaper copy…", metadata.Duration > 0 ? us / (metadata.Duration * 1_000_000) : 0, true);
                if (File.Exists(temporary) && (new FileInfo(temporary).Length > MaximumCacheBytes ||
                    new DriveInfo(Path.GetPathRoot(CacheDirectory)!).AvailableFreeSpace < FreeSpaceReserve))
                { process.Kill(true); throw new IOException("Wallpaper cache storage limit reached."); }
            }
            await process.WaitForExitAsync(token);
            var error = await stderr;
            if (process.ExitCode != 0) throw new IOException(error[..Math.Min(error.Length, 1000)]);
            var verified = await VideoMetadata.ProbeAsync(temporary, token);
            if (verified.Width != size.Width || verified.Height != size.Height || Math.Abs(verified.Fps - 60) > .1 ||
                verified.IsHdr != metadata.IsHdr || (metadata.Transfer != "" && verified.Transfer != metadata.Transfer) ||
                (metadata.Primaries != "" && verified.Primaries != metadata.Primaries) ||
                (metadata.Matrix != "" && verified.Matrix != metadata.Matrix) ||
                (metadata.Range != "" && verified.Range != metadata.Range) ||
                verified.Rotation != metadata.Rotation ||
                (metadata.MasteringPrimaries[0] > 0 && (!verified.MasteringPrimaries.SequenceEqual(metadata.MasteringPrimaries) || verified.PeakNits != metadata.PeakNits)) ||
                (metadata.MaxCll > 0 && verified.MaxCll != metadata.MaxCll) ||
                (metadata.MaxFall > 0 && verified.MaxFall != metadata.MaxFall) ||
                (metadata.Duration > 0 && Math.Abs(verified.Duration - metadata.Duration) > .1))
                throw new InvalidDataException("Optimized wallpaper did not preserve video metadata.");
            File.Move(temporary, target, true);
            EvictInactiveFiles(target);
            Publish(source, "High-quality optimized copy ready", 1, false);
            return target;
        }
        catch (OperationCanceledException) { Publish(source, "", 0, false); throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or global::System.ComponentModel.Win32Exception)
        {
            Console.Error.WriteLine($"[wallpaper] Optimization unavailable: {ex.Message}");
            Publish(source, "Playing the original video", 0, false);
            return source;
        }
        finally
        {
            lock (ConverterSync) { _converter = null; _converterSource = null; _converterPaused = false; }
            if (Demands.TryGetValue(source, out var demands) && demands.IsEmpty) Demands.TryRemove(source, out _);
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            finally { ConversionGate.Release(); }
        }
    }
    private static string ColorFilter(VideoMetadata metadata, bool trailing = false)
    {
        var parameters = new List<string>();
        foreach (var (name, value) in new[] { ("color_primaries", metadata.Primaries), ("color_trc", metadata.Transfer),
            ("colorspace", metadata.Matrix), ("range", metadata.Range) })
            if (value is not ("" or "unknown" or "unspecified") && value.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                parameters.Add(name + "=" + value);
        if (parameters.Count == 0) return "";
        var filter = "setparams=" + string.Join(':', parameters);
        return trailing ? "," + filter : filter + ",";
    }

    private static string EncoderParameters(VideoMetadata metadata)
    {
        var options = "pools=2:frame-threads=1:log-level=error";
        if (metadata.Transfer == "smpte2084") options += ":hdr10=1:repeat-headers=1";
        if (metadata.MasteringPrimaries[0] > 0)
        {
            var p = metadata.MasteringPrimaries;
            options += $":master-display=G({p[2]/20},{p[3]/20})B({p[4]/20},{p[5]/20})R({p[0]/20},{p[1]/20})WP({p[6]/20},{p[7]/20})L({metadata.PeakNits*10000},{metadata.MinimumLuminance})";
        }
        if (metadata.MaxCll > 0 || metadata.MaxFall > 0) options += $":max-cll={metadata.MaxCll},{metadata.MaxFall}";
        return options;
    }
    private static void EvictInactiveFiles(string? preserve = null)
    {
        var files = new DirectoryInfo(CacheDirectory).GetFiles("*.mp4").Where(f => !f.Name.Contains(".partial.")).OrderBy(f => f.LastAccessTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= MaximumCacheBytes && new DriveInfo(Path.GetPathRoot(CacheDirectory)!).AvailableFreeSpace >= FreeSpaceReserve) break;
            if (file.FullName == preserve || ActiveFiles.GetValueOrDefault(file.FullName) > 0) continue;
            total -= file.Length; file.Delete();
        }
    }
}
