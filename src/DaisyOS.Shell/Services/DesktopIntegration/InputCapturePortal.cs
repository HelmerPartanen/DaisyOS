using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Threading;
using Microsoft.Win32.SafeHandles;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class InputCapturePortal(DBusConnection bus) : IDisposable
{
    private const string ManagerPath = "/org/kde/KWin/EIS/InputCapture";
    private const string ManagerInterface = "org.kde.KWin.EIS.InputCaptureManager";
    private const string Interface = "org.kde.KWin.EIS.InputCapture";
    private sealed class Session(string app)
    {
        public string App { get; } = app;
        public string? Native;
        public uint ZoneSet, Activation;
        public List<(uint Width, uint Height, int X, int Y)> Zones = [];
        public List<(uint Id, int X1, int Y1, int X2, int Y2)> Barriers = [];
        public List<IDisposable> Watches { get; } = [];
        public CancellationTokenSource Stop { get; } = new();
        public Window? Indicator;
    }
    private readonly Dictionary<string, Session> _sessions = [];
    public async Task<(uint Code, Dictionary<string, VariantValue> Results)> HandleAsync(string method, List<string> args, Dictionary<string, VariantValue> options, CancellationToken stop)
    {
        if (method == "CreateSession")
        {
            try
            {
            var capabilities = options.TryGetValue("capabilities", out var value) ? value.GetUInt32() : 3u;
            if (capabilities == 0 || (capabilities & ~7u) != 0 || _sessions.ContainsKey(args[1])) return (2, []);
            var session = new Session(args[2]); _sessions.Add(args[1], session);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, session.Stop.Token);
            var dialog = PortalDialog.Create(() => new PortalDialog("Allow input sharing?", $"{args[2]} wants to capture your input when the pointer reaches a configured screen edge. Allow only applications you trust.", "Allow", "Deny"));
            using var cancel = linked.Token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
            if (!await dialog.Present() || linked.IsCancellationRequested) { Close(args[1]); return (1, []); }
            var call = Call(ManagerPath, ManagerInterface, "addInputCapture", "u", capabilities);
            session.Native = (await bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadObjectPath().ToString(), (object?)null));
            if (linked.IsCancellationRequested || !_sessions.ContainsKey(args[1]))
            {
                if (_sessions.ContainsKey(args[1])) Close(args[1]); else RemoveNative(session.Native);
                return (1, []);
            }
            await WatchAsync(args[1], session);
            var indicator = PortalSharingIndicator.Create("Keyboard and mouse", "Input sharing allowed for " + args[2], () => Close(args[1], true));
            indicator.Closed += (_, _) => Close(args[1], true); session.Indicator = indicator; indicator.Show();
            return (0, new() { ["capabilities"] = capabilities });
            }
            catch { Close(args[1]); throw; }
        }
        var path = method is "GetZones" or "SetPointerBarriers" ? args[1] : args[0];
        var app = method is "GetZones" or "SetPointerBarriers" ? args[2] : args[1];
        if (!_sessions.TryGetValue(path, out var current) || current.App != app || current.Native is null) return (2, []);
        using var linkedStop = CancellationTokenSource.CreateLinkedTokenSource(stop, current.Stop.Token);
        var token = linkedStop.Token;
        if (method == "GetZones")
        {
            var start = new ProcessStartInfo(PortalCaptureHelper.Path) { ArgumentList = { "--list" }, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            using var process = Process.Start(start) ?? throw new IOException("Output information unavailable.");
            var output = await process.StandardOutput.ReadToEndAsync(token); await process.WaitForExitAsync(token);
            current.Zones.Clear();
            var zones = new Tmds.DBus.Protocol.Array<Struct<uint, uint, int, int>>();
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split('\t'); if (parts.Length != 6 || parts[0] != "output") continue;
                var zone = (uint.Parse(parts[4]), uint.Parse(parts[5]), int.Parse(parts[2]), int.Parse(parts[3]));
                current.Zones.Add(zone); zones.Add(new Struct<uint, uint, int, int>(zone.Item1, zone.Item2, zone.Item3, zone.Item4));
            }
            current.ZoneSet++; current.Barriers.Clear();
            return (0, new() { ["zones"] = zones, ["zone_set"] = current.ZoneSet });
        }
        if (method == "SetPointerBarriers")
        {
            var barriers = options["daisy_barriers"];
            var failed = new List<uint>(); current.Barriers.Clear();
            for (var i = 0; i < barriers.Count; i++)
            {
                var barrier = barriers.GetItem(i).GetDictionary<string, VariantValue>();
                var id = barrier["barrier_id"].GetUInt32(); var pos = barrier["position"];
                var x1 = pos.GetItem(0).GetInt32(); var y1 = pos.GetItem(1).GetInt32(); var x2 = pos.GetItem(2).GetInt32(); var y2 = pos.GetItem(3).GetInt32();
                var valid = options["daisy_zone_set"].GetUInt32() == current.ZoneSet && id != 0 && !current.Barriers.Any(b => b.Id == id) && current.Zones.Any(zone =>
                    (x1 == x2 && y1 != y2 && (x1 == zone.X || x1 == zone.X + zone.Width) && Math.Min(y1, y2) >= zone.Y && Math.Max(y1, y2) <= zone.Y + zone.Height) ||
                    (y1 == y2 && x1 != x2 && (y1 == zone.Y || y1 == zone.Y + zone.Height) && Math.Min(x1, x2) >= zone.X && Math.Max(x1, x2) <= zone.X + zone.Width));
                if (valid) current.Barriers.Add((id, x1, y1, x2, y2)); else failed.Add(id);
            }
            return (0, new() { ["failed_barriers"] = VariantValue.Array(failed.ToArray()) });
        }
        MessageBuffer command;
        if (method == "Enable")
        {
            using var writer = bus.GetMessageWriter(); writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: current.Native, @interface: Interface, member: "enable", signature: "a(u(ii)(ii))");
            var array = writer.WriteArrayStart(DBusType.Struct);
            foreach (var barrier in current.Barriers)
            { writer.WriteStructureStart(); writer.WriteUInt32(barrier.Id); writer.WriteStructureStart(); writer.WriteInt32(barrier.X1); writer.WriteInt32(barrier.Y1); writer.WriteStructureStart(); writer.WriteInt32(barrier.X2); writer.WriteInt32(barrier.Y2); }
            writer.WriteArrayEnd(array); command = writer.CreateMessage();
        }
        else if (method == "Disable") command = Call(current.Native, Interface, "disable");
        else if (method == "Release")
        {
            if (options.TryGetValue("activation_id", out var activation) && activation.GetUInt32() != current.Activation) return (2, []);
            var hasPosition = options.TryGetValue("cursor_position", out var position);
            using var writer = bus.GetMessageWriter(); writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: current.Native, @interface: Interface, member: "release", signature: "(dd)b");
            writer.WriteStructureStart(); writer.WriteDouble(hasPosition ? position.GetItem(0).GetDouble() : 0); writer.WriteDouble(hasPosition ? position.GetItem(1).GetDouble() : 0); writer.WriteBool(hasPosition); command = writer.CreateMessage();
        }
        else return (2, []);
        await bus.CallMethodAsync(command); token.ThrowIfCancellationRequested(); return (0, []);
    }
    public async Task<SafeFileHandle> ConnectAsync(string path, string app)
    {
        if (!_sessions.TryGetValue(path, out var session) || session.App != app || session.Native is null) throw new UnauthorizedAccessException();
        var handle = await bus.CallMethodAsync(Call(session.Native, Interface, "connectToEIS"), (reply, _) => reply.GetBodyReader().ReadHandle<SafeFileHandle>(), (object?)null);
        if (!_sessions.ContainsKey(path)) { handle.Dispose(); throw new OperationCanceledException(); }
        return handle;
    }
    private async Task WatchAsync(string path, Session session)
    {
        foreach (var member in new[] { "activated", "deactivated", "disabled" })
            session.Watches.Add(await bus.AddMatchAsync<(uint Activation, uint Barrier, double X, double Y)>(new MatchRule { Type = MessageType.Signal, Sender = "org.kde.KWin", Path = session.Native, Interface = Interface, Member = member }, (message, _) =>
            {
                var reader = message.GetBodyReader();
                if (message.MemberAsString == "disabled") return (0, 0, 0, 0);
                var activation = reader.ReadUInt32(); if (message.MemberAsString == "deactivated") return (activation, 0, 0, 0);
                var barrier = reader.ReadUInt32(); reader.AlignStruct(); return (activation, barrier, reader.ReadDouble(), reader.ReadDouble());
            }, notification =>
            {
                if (!notification.HasValue) return;
                var value = notification.Value; session.Activation = value.Activation;
                var options = new Dictionary<string, VariantValue>();
                if (member != "disabled") options["activation_id"] = value.Activation;
                if (member == "activated") { options["barrier_id"] = value.Barrier; options["cursor_position"] = VariantValue.Struct(value.X, value.Y); }
                Emit(path, char.ToUpperInvariant(member[0]) + member[1..], options);
            }, emitOnCapturedContext: false));
    }
    private void Emit(string path, string member, Dictionary<string, VariantValue> options)
    {
        using var writer = bus.GetMessageWriter(); writer.WriteSignalHeader(path: "/org/freedesktop/portal/desktop", @interface: "org.freedesktop.impl.portal.InputCapture", member: member, signature: "oa{sv}"); writer.WriteObjectPath(path); writer.WriteDictionary(options); bus.TrySendMessage(writer.CreateMessage());
    }
    private MessageBuffer Call(string path, string iface, string member, string? signature = null, uint? value = null)
    {
        using var writer = bus.GetMessageWriter(); writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: path, @interface: iface, member: member, signature: signature);
        if (value.HasValue) writer.WriteUInt32(value.Value); return writer.CreateMessage();
    }
    private void RemoveNative(string path)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: ManagerPath, @interface: ManagerInterface, member: "removeInputCapture", signature: "o");
        writer.WriteObjectPath(path); _ = RemoveNativeAsync(writer.CreateMessage());
    }
    private async Task RemoveNativeAsync(MessageBuffer message)
    {
        try { await bus.CallMethodAsync(message); }
        catch (Exception ex) { Console.Error.WriteLine($"[portal] Input capture cleanup failed: {ex.Message}"); }
    }
    public void Close(string path, bool signal = false)
    {
        if (!_sessions.Remove(path, out var session)) return;
        session.Stop.Cancel(); foreach (var watch in session.Watches) watch.Dispose(); session.Indicator?.Close();
        if (session.Native is not null) RemoveNative(session.Native);
        session.Stop.Dispose();
        if (signal) { using var writer = bus.GetMessageWriter(); writer.WriteSignalHeader(path: path, @interface: "org.freedesktop.impl.portal.Session", member: "Closed"); bus.TrySendMessage(writer.CreateMessage()); }
    }
    public void Dispose() { foreach (var path in _sessions.Keys.ToArray()) Close(path); }
}
