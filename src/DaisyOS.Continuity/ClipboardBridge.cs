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
    internal static string? ResolveWaylandDisplay(string? display, string? runtime)
    {
        static bool Exists(string? name, string? root) => !string.IsNullOrEmpty(name) &&
            File.Exists(Path.IsPathFullyQualified(name) ? name : Path.Combine(root ?? "", name));
        if (Exists(display, runtime)) return display;
        var record = Path.Combine(runtime ?? "", "daisyos-native-development", "wayland-display");
        try
        {
            var current = File.ReadAllText(record).Trim();
            if (Exists(current, runtime)) return current;
        }
        catch (IOException) { }
        return display;
    }
    private static string? WaylandDisplay() => ResolveWaylandDisplay(
        Environment.GetEnvironmentVariable("WAYLAND_DISPLAY"), Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR"));
    private static bool HasWaylandDisplay() => !string.IsNullOrEmpty(WaylandDisplay());

    private const string WaylandDataControlScript = """
import socket, struct, os, array, sys, select, subprocess

mode = sys.argv[1]
runtime = os.environ.get("XDG_RUNTIME_DIR") or f"/run/user/{os.getuid()}"
display = os.environ.get("WAYLAND_DISPLAY") or ""
if not display:
    try:
        with open(os.path.join(runtime, "daisyos-native-development", "wayland-display")) as f:
            display = f.read().strip()
    except OSError:
        display = "wayland-0"
sock_path = display if os.path.isabs(display) else os.path.join(runtime, display)

sock = socket.socket(socket.AF_UNIX, socket.SOCK_STREAM)
sock.connect(sock_path)

def wl_str(s):
    b = s.encode("utf-8") + bytes([0])
    pad = (4 - (len(b) % 4)) % 4
    return struct.pack("<I", len(b)) + b + (bytes([0]) * pad)

def wl_msg(obj, opcode, payload=b""):
    return struct.pack("<II", obj, ((len(payload) + 8) << 16) | opcode) + payload

def recv_events(sock):
    data, ancdata, _, _ = sock.recvmsg(65536, socket.CMSG_SPACE(16 * 4))
    if not data:
        raise EOFError()
    fds = []
    for cmsg_lvl, cmsg_type, cmsg_data in ancdata:
        if cmsg_lvl == socket.SOL_SOCKET and cmsg_type == socket.SCM_RIGHTS:
            arr = array.array("i")
            arr.frombytes(cmsg_data[:len(cmsg_data) - (len(cmsg_data) % arr.itemsize)])
            fds.extend(arr)
    return data, fds

sock.sendall(wl_msg(1, 1, struct.pack("<I", 2)) + wl_msg(1, 0, struct.pack("<I", 3)))
seat = dcm = None
buf = b""
sync_done = False
while not sync_done:
    chunk, _ = recv_events(sock)
    buf += chunk
    while len(buf) >= 8:
        obj, size_op = struct.unpack_from("<II", buf, 0)
        size = size_op >> 16; op = size_op & 0xffff
        if len(buf) < size: break
        body = buf[8:size]; buf = buf[size:]
        if obj == 2 and op == 0:
            name, slen = struct.unpack_from("<II", body, 0)
            iface = body[8:8+slen-1].decode("utf-8", "ignore")
            ver = struct.unpack_from("<I", body, 8 + ((slen + 3) & ~3))[0]
            if iface == "wl_seat" and seat is None: seat = (name, ver)
            if iface == "ext_data_control_manager_v1": dcm = (name, ver, iface)
            elif iface == "zwlr_data_control_manager_v1" and dcm is None: dcm = (name, ver, iface)
        elif obj == 3 and op == 0:
            sync_done = True

if not seat or not dcm:
    sys.exit(2)

bind_seat = wl_msg(2, 0, struct.pack("<I", seat[0]) + wl_str("wl_seat") + struct.pack("<II", 1, 4))
bind_dcm = wl_msg(2, 0, struct.pack("<I", dcm[0]) + wl_str(dcm[2]) + struct.pack("<II", 1, 5))
get_dev = wl_msg(5, 1, struct.pack("<II", 6, 4))

if mode == "copy":
    payload = sys.stdin.buffer.read()
    msg = bind_seat + bind_dcm + get_dev + wl_msg(5, 0, struct.pack("<I", 7))
    for m in ("text/plain;charset=utf-8", "text/plain", "UTF8_STRING", "STRING", "TEXT"):
        msg += wl_msg(7, 0, wl_str(m))
    msg += wl_msg(6, 0, struct.pack("<I", 7))
    sock.sendall(msg)
    fds_queue = []
    while True:
        chunk, fds = recv_events(sock)
        fds_queue.extend(fds)
        buf += chunk
        while len(buf) >= 8:
            obj, size_op = struct.unpack_from("<II", buf, 0)
            size = size_op >> 16; op = size_op & 0xffff
            if len(buf) < size: break
            body = buf[8:size]; buf = buf[size:]
            if obj == 6 and op == 0:
                oid = struct.unpack_from("<I", body, 0)[0]
                if oid: sock.sendall(wl_msg(oid, 1))
            elif obj == 7 and op == 0:
                fd = fds_queue.pop(0)
                try:
                    remaining = memoryview(payload)
                    while remaining:
                        written = os.write(fd, remaining)
                        remaining = remaining[written:]
                except OSError:
                    pass
                finally:
                    os.close(fd)
            elif obj == 7 and op == 1:
                sys.exit(0)
elif mode == "watch":
    cmd = sys.argv[2:]
    sock.sendall(bind_seat + bind_dcm + get_dev + wl_msg(1, 0, struct.pack("<I", 7)))
    offers = {}
    initial_sync = False
    while True:
        chunk, fds = recv_events(sock)
        for fd in fds: os.close(fd)
        buf += chunk
        while len(buf) >= 8:
            obj, size_op = struct.unpack_from("<II", buf, 0)
            size = size_op >> 16; op = size_op & 0xffff
            if len(buf) < size: break
            body = buf[8:size]; buf = buf[size:]
            if obj == 7 and op == 0:
                initial_sync = True
            elif obj == 6 and op == 0:
                oid = struct.unpack_from("<I", body, 0)[0]
                offers[oid] = []
            elif obj in offers and op == 0:
                slen = struct.unpack_from("<I", body, 0)[0]
                offers[obj].append(body[4:4+slen-1].decode("utf-8", "ignore"))
            elif obj == 6 and op == 3:
                oid = struct.unpack_from("<I", body, 0)[0]
                if oid:
                    offers.pop(oid, None)
                    sock.sendall(wl_msg(oid, 1))
            elif obj == 6 and op == 1:
                oid = struct.unpack_from("<I", body, 0)[0]
                mimes = offers.pop(oid, []) if oid else []
                if oid and initial_sync:
                    chosen = next((m for m in ("text/plain;charset=utf-8", "text/plain", "UTF8_STRING", "STRING", "TEXT") if m in mimes), None)
                    if chosen:
                        rfd, wfd = os.pipe()
                        msg = wl_msg(oid, 0, wl_str(chosen))
                        sock.sendmsg([msg], [(socket.SOL_SOCKET, socket.SCM_RIGHTS, array.array("i", [wfd]))])
                        os.close(wfd)
                        out = bytearray()
                        while len(out) <= 1024 * 1024:
                            r, _, _ = select.select([rfd], [], [], 2.0)
                            if not r: break
                            d = os.read(rfd, 65536)
                            if not d: break
                            out.extend(d)
                        os.close(rfd)
                        if 0 < len(out) <= 1024 * 1024:
                            try:
                                subprocess.run(cmd, input=bytes(out), stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=5)
                            except (subprocess.TimeoutExpired, OSError):
                                # A temporarily busy daemon must not stop future clipboard events.
                                pass
                if oid:
                    sock.sendall(wl_msg(oid, 1))
""";

    private static bool UseNativeScript() =>
        Available("python3") && (!Available("wl-copy") || !Available("wl-paste") ||
            (WaylandDisplay() ?? "").StartsWith("daisyos-native-", StringComparison.Ordinal));

    public void Start()
    {
        lock (_gate) {
        if (_watch is { HasExited: false }) return;
        _watch?.Dispose(); _watch = null;
        var hasWl = Available("wl-copy") && Available("wl-paste");
        var useScript = UseNativeScript();
        if ((!hasWl && !useScript) || !HasWaylandDisplay())
        { Status = "Clipboard unavailable: install wl-clipboard and use an authorized Wayland data-control session."; return; }
        try
        {
            ProcessStartInfo start;
            if (!useScript)
            {
                start = new ProcessStartInfo("wl-paste") { RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var arg in new[] { "--type", "text", "--watch", Environment.ProcessPath ?? "daisyos-continuity" }) start.ArgumentList.Add(arg);
            }
            else
            {
                start = new ProcessStartInfo("python3") { RedirectStandardError = true, RedirectStandardOutput = true };
                foreach (var arg in new[] { "-c", WaylandDataControlScript, "watch", Environment.ProcessPath ?? "daisyos-continuity" }) start.ArgumentList.Add(arg);
            }
            // Framework-dependent launches use dotnet plus the assembly path.
            if (Path.GetFileNameWithoutExtension(Environment.ProcessPath) == "dotnet") start.ArgumentList.Add(typeof(ClipboardBridge).Assembly.Location);
            start.ArgumentList.Add("--clipboard-event");
            if (WaylandDisplay() is { } display) start.Environment["WAYLAND_DISPLAY"] = display;
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
        ProcessStartInfo start;
        if (!UseNativeScript())
        {
            start = new ProcessStartInfo("wl-copy") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "--foreground", "--type", "text/plain;charset=utf-8" }) start.ArgumentList.Add(arg);
        }
        else
        {
            start = new ProcessStartInfo("python3") { RedirectStandardInput = true, RedirectStandardError = true, RedirectStandardOutput = true };
            foreach (var arg in new[] { "-c", WaylandDataControlScript, "copy" }) start.ArgumentList.Add(arg);
        }
        if (WaylandDisplay() is { } display) start.Environment["WAYLAND_DISPLAY"] = display;
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
