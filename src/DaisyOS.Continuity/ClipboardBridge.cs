using global::System.Diagnostics;
using global::System.Security.Cryptography;
using global::System.Text;
using DaisyOS.Core.Continuity;
using DaisyOS.System.Continuity;

namespace DaisyOS.Continuity;

public sealed class ClipboardBridge(TimeProvider? timeProvider = null)
{
    private readonly object _gate = new();
    private string? _sourcePeer;
    private Process? _watch;
    private Process? _source;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly Dictionary<string, DateTimeOffset> _injectedDigests = new();
    internal int RetainedInjectionCount { get { lock (_gate) return _injectedDigests.Count; } }
    private string _status = "Clipboard sharing is off.";
    public string Status
    {
        get { lock (_gate) return _watch is { HasExited: true } ? "Wayland clipboard data-control is unavailable or was denied by the compositor." : _status; }
        private set => _status = value;
    }
    private static bool Available(string name) => (Environment.GetEnvironmentVariable("PATH") ?? "").Split(':').Any(p => File.Exists(Path.Combine(p, name)));
    public void Start()
    {
        lock (_gate) {
        if (_watch is { HasExited: false }) return;
        if (!Available("wl-copy") || !Available("wl-paste") || string.IsNullOrEmpty(Environment.GetEnvironmentVariable("WAYLAND_DISPLAY")))
        { Status = "Clipboard unavailable: install wl-clipboard and use an authorized Wayland data-control session."; return; }
        try
        {
            var start = new ProcessStartInfo("wl-paste") { RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "--type", "text", "--watch", Environment.ProcessPath ?? "daisyos-continuity" }) start.ArgumentList.Add(arg);
            // Framework-dependent launches use dotnet plus the assembly path.
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(ClipboardBridge).Assembly.Location);
            start.ArgumentList.Add("--clipboard-event");
            _watch = Process.Start(start);
            if (_watch is not null) { _ = _watch.StandardError.ReadToEndAsync(); _ = _watch.StandardOutput.ReadToEndAsync(); }
            Status = "Text clipboard uses Wayland data-control. Copying from a peer replaces this device’s clipboard.";
        }
        catch { Status = "Wayland clipboard access unavailable."; }
        }
    }
    private void PruneInjections()
    {
        var now = _time.GetUtcNow();
        foreach (var key in _injectedDigests.Where(p => now - p.Value >= TimeSpan.FromMinutes(5)).Select(p => p.Key).ToArray())
            _injectedDigests.Remove(key);
    }
    internal void RememberInjection(byte[] data)
    {
        lock (_gate)
        {
            PruneInjections();
            var digest = SecureState.Fingerprint(data);
            if (_injectedDigests.Count >= 256 && !_injectedDigests.ContainsKey(digest))
                _injectedDigests.Remove(_injectedDigests.MinBy(p => p.Value).Key);
            _injectedDigests[digest] = _time.GetUtcNow();
        }
    }
    public bool IsEcho(byte[] data)
    {
        lock (_gate)
        {
            PruneInjections();
            return _injectedDigests.ContainsKey(SecureState.Fingerprint(data));
        }
    }
    public void ClearPeer(string peer) { lock (_gate) if (_sourcePeer == peer) StopSource(); }
    public async Task SetAsync(byte[] data, string peer, CancellationToken token)
    {
        _ = new UTF8Encoding(false, true).GetCharCount(data);
        Process source;
        lock (_gate)
        {
        token.ThrowIfCancellationRequested();
        StopSource();
        _sourcePeer = peer;
        RememberInjection(data);
        var start = new ProcessStartInfo("wl-copy") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
        foreach (var arg in new[] { "--foreground", "--type", "text/plain;charset=utf-8" }) start.ArgumentList.Add(arg);
        _source = Process.Start(start) ?? throw new IOException("Clipboard unavailable.");
        _ = _source.StandardError.ReadToEndAsync(); _ = _source.StandardOutput.ReadToEndAsync();
        source = _source;
        }
        try
        {
            await source.StandardInput.BaseStream.WriteAsync(data, token);
            source.StandardInput.Close(); token.ThrowIfCancellationRequested();
        }
        catch { lock (_gate) { if (_source == source) StopSource(); } throw; }
    }
    private void StopSource()
    {
        try { if (_source is { HasExited: false }) _source.Kill(true); } catch (InvalidOperationException) { }
        _source?.Dispose(); _source = null; _sourcePeer = null;
    }
    public void Stop()
    {
        lock (_gate)
        {
        try { if (_watch is { HasExited: false }) _watch.Kill(true); } catch (InvalidOperationException) { }
        _watch?.Dispose(); _watch = null; StopSource(); Status = "Clipboard sharing is paused.";
        }
    }
    public static async Task<int> ForwardAsync()
    {
        if (Environment.GetEnvironmentVariable("CLIPBOARD_STATE") is "nil" or "clear" or "sensitive") return 0;
        using var input = Console.OpenStandardInput();
        using var buffer = new MemoryStream();
        var bytes = new byte[8192];
        try
        {
            int count;
            while ((count = await input.ReadAsync(bytes)) > 0)
            {
                if (buffer.Length + count > 1024 * 1024) return 0;
                await buffer.WriteAsync(bytes.AsMemory(0, count));
            }
            var data = buffer.ToArray();
            try { await new ContinuityClient().ExecuteAsync(new("clipboard", Value: Convert.ToBase64String(data))); }
            finally { CryptographicOperations.ZeroMemory(data); }
            return 0;
        }
        catch { return 1; }
        finally { CryptographicOperations.ZeroMemory(bytes); if (buffer.TryGetBuffer(out var segment)) CryptographicOperations.ZeroMemory(segment.AsSpan()); }
    }
}
