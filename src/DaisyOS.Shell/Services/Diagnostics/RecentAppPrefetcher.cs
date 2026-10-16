using System.Runtime.InteropServices;
using System.Text.Json;
using DaisyOS.Shell.Services.Compositor;
using Microsoft.Win32.SafeHandles;

namespace DaisyOS.Shell.Services.Diagnostics;

/// <summary>Idle-only prefetch of observed executable/library mappings; never executes an app.</summary>
internal sealed partial class RecentAppPrefetcher : IDisposable
{
    private const int PosixFadvWillneed = 3;

    [LibraryImport("libc", EntryPoint = "posix_fadvise", SetLastError = true)]
    private static partial int PosixFadvise(int fd, long offset, long len, int advice);

    private readonly object _gate = new();
    private readonly object _cacheWriteGate = new();
    private readonly LinkedList<(string Id, string[] Paths)> _recent = [];
    private readonly Timer _idle;
    private readonly string _cachePath;
    private CancellationTokenSource? _work;
    private int _lastPid;
    private bool _disposed;
    private bool _suspended;

    internal bool IsSuspended { get { lock (_gate) return _suspended; } }

    public void SetSuspended(bool suspended)
    {
        lock (_gate)
        {
            if (_disposed || _suspended == suspended) return;
            _suspended = suspended;
            if (suspended) _work?.Cancel();
            _idle.Change(suspended ? Timeout.Infinite : 4000, Timeout.Infinite);
        }
    }

    public RecentAppPrefetcher(bool enabled = true)
    {
        if (!enabled)
        {
            _disposed = true;
            _suspended = true;
            _cachePath = string.Empty;
            _idle = new Timer(_ => { }, null, Timeout.Infinite, Timeout.Infinite);
            return;
        }

        _cachePath = Path.Combine(
            Environment.GetEnvironmentVariable("XDG_STATE_HOME")
                ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "state"),
            "DaisyOS", "prefetch-cache.json");

        LoadCache();

        // Trigger initial prefetch 3 seconds after startup if previous session cache exists.
        _idle = new Timer(_ => Start(), null, _recent.Count > 0 ? 3000 : Timeout.Infinite, Timeout.Infinite);
    }

    public void Interaction()
    {
        lock (_gate)
        {
            _work?.Cancel();
            if (!_disposed && !_suspended) _idle.Change(4000, Timeout.Infinite);
        }
    }

    public void Observe(CompositorWindowRecord window)
    {
        lock (_gate) if (_disposed || _suspended) return;
        if (!window.IsActive || window.ProcessId <= 1 || window.ProcessId == Environment.ProcessId || window.ProcessId == _lastPid) return;
        _lastPid = window.ProcessId;
        _ = Task.Run(() =>
        {
            try
            {
                var executable = File.ResolveLinkTarget($"/proc/{window.ProcessId}/exe", true)?.FullName;
                var paths = File.ReadLines($"/proc/{window.ProcessId}/maps")
                    .Select(line => line.Split(' ', 6, StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(5))
                    .Where(path => path is not null && path.StartsWith('/') && !path.Contains("(deleted)") &&
                        (path == executable || Path.GetFileName(path).Contains(".so", StringComparison.Ordinal)))
                    .Cast<string>().Distinct(StringComparer.Ordinal).Take(256).ToArray();
                lock (_gate)
                {
                    if (_disposed || _suspended) return;
                    var old = _recent.First;
                    while (old is not null) { var next = old.Next; if (old.Value.Id == window.AppId) _recent.Remove(old); old = next; }
                    _recent.AddFirst((window.AppId, paths));
                    while (_recent.Count > 7) _recent.RemoveLast();
                }
                SaveCache();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        });
    }

    private void Start()
    {
        if (Environment.GetEnvironmentVariable("DAISYOS_PREFETCH") == "0" || MemoryPressure()) return;
        lock (_gate)
        {
            if (_disposed || _suspended || _work is { IsCancellationRequested: false }) return;
            _work = new CancellationTokenSource();
            var paths = _recent.SelectMany(entry => entry.Paths).Distinct(StringComparer.Ordinal).ToArray();
            var work = _work;
            // Linux fadvise is synchronous. Never issue readahead while holding
            // the gate that input and Gaming Mode need to cancel this worker.
            _ = Task.Run(() => RunAsync(paths, work));
        }
    }

    internal static bool MemoryPressure()
    {
        try
        {
            var available = File.ReadLines("/proc/meminfo").First(line => line.StartsWith("MemAvailable:"));
            return long.Parse(available.Split(' ', StringSplitOptions.RemoveEmptyEntries)[1]) < 1024 * 1024;
        }
        catch { return true; }
    }

    private async Task RunAsync(string[] paths, CancellationTokenSource work)
    {
        var token = work.Token;
        long remaining = 512L * 1024 * 1024;
        var isLinux = OperatingSystem.IsLinux();
        var filesChecked = 0;

        try
        {
            foreach (var path in paths)
            {
                token.ThrowIfCancellationRequested();
                if (remaining <= 0) break;

                if (++filesChecked % 32 == 0 && MemoryPressure()) break;

                try
                {
                    var fileInfo = new FileInfo(path);
                    if (!fileInfo.Exists) continue;
                    var fileSize = fileInfo.Length;
                    if (fileSize <= 0) continue;

                    var bytesToPrefetch = Math.Min(fileSize, remaining);

                    if (isLinux)
                    {
                        using var handle = File.OpenHandle(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        PosixFadvise(handle.DangerousGetHandle().ToInt32(), 0, bytesToPrefetch, PosixFadvWillneed);
                    }
                    else
                    {
                        // Fallback for non-Linux or environments without posix_fadvise
                        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 64 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        var buffer = new byte[64 * 1024];
                        var fileRemaining = bytesToPrefetch;
                        while (fileRemaining > 0)
                        {
                            token.ThrowIfCancellationRequested();
                            var read = await stream.ReadAsync(buffer.AsMemory(0, (int)Math.Min(buffer.Length, fileRemaining)), token);
                            if (read == 0) break;
                            fileRemaining -= read;
                        }
                    }

                    remaining -= bytesToPrefetch;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
            }
            ResponsivenessTrace.Mark("external.prefetch", durationMs: null);
        }
        catch (OperationCanceledException) { }
        finally { lock (_gate) { if (ReferenceEquals(_work, work)) _work = null; work.Dispose(); } }
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return;
            var json = File.ReadAllText(_cachePath);
            var records = JsonSerializer.Deserialize(json, PrefetchJsonContext.Default.ListPrefetchCacheRecord);
            if (records is null) return;

            foreach (var record in records)
            {
                if (!string.IsNullOrWhiteSpace(record.Id) && record.Paths is { Length: > 0 })
                {
                    _recent.AddLast((record.Id, record.Paths));
                }
            }
        }
        catch { }
    }

    private void SaveCache()
    {
        try
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            List<PrefetchCacheRecord> records;
            lock (_gate)
            {
                if (_disposed || _suspended) return;
                records = _recent.Select(r => new PrefetchCacheRecord(r.Id, r.Paths)).ToList();
            }
            var json = JsonSerializer.Serialize(records, PrefetchJsonContext.Default.ListPrefetchCacheRecord);
            lock (_cacheWriteGate) File.WriteAllText(_cachePath, json);
        }
        catch { }
    }

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _work?.Cancel(); _idle.Dispose(); _recent.Clear(); }
    }
}

internal sealed record PrefetchCacheRecord(string Id, string[] Paths);

[global::System.Text.Json.Serialization.JsonSerializable(typeof(List<PrefetchCacheRecord>))]
internal partial class PrefetchJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext { }
