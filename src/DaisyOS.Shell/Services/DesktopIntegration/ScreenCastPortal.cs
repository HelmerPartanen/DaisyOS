using System.Diagnostics;
using Microsoft.Win32.SafeHandles;
using Avalonia.Controls;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Compositor;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class ScreenCastPortal(DBusConnection bus) : IDisposable
{
    private sealed class Session(string app)
    {
        public string App { get; } = app;
        public uint Types = 3, Cursor = 2;
        public bool Multiple, Selected, Started, Remote;
        public uint Devices;
        public List<int> EisConnections { get; } = [];
        public List<Process> Streams { get; } = [];
        public CancellationTokenSource Stop { get; } = new();
        public Window? Indicator;
    }
    private readonly Dictionary<string, Session> _sessions = [];
    private sealed record Source(string Id, string Label, bool Window, int X = 0, int Y = 0, int Width = 0, int Height = 0);
    private static string Helper => PortalCaptureHelper.Path;

    public async Task<(uint Code, Dictionary<string, VariantValue> Results)> HandleAsync(string portal, string method, List<string> args, Dictionary<string, VariantValue> options, CancellationToken stop)
    {
        if (method == "CreateSession")
        {
            if (!_sessions.TryAdd(args[1], new Session(args[2]) { Remote = portal == "RemoteDesktop" })) return (2, []);
            return (0, new() { ["session_id"] = args[1].Split('/').Last() });
        }
        if (!_sessions.TryGetValue(args[1], out var session) || session.App != args[2] || session.Started) return (2, []);
        if (method == "SelectDevices")
        {
            if (!session.Remote) return (2, []);
            session.Devices = options.TryGetValue("types", out var devices) ? devices.GetUInt32() : 3;
            return (session.Devices != 0 && (session.Devices & ~7u) == 0 ? 0u : 2u, []);
        }
        if (method == "SelectSources")
        {
            session.Types = options.TryGetValue("types", out var types) ? types.GetUInt32() : 1;
            session.Cursor = options.TryGetValue("cursor_mode", out var cursor) ? cursor.GetUInt32() : 2;
            session.Multiple = options.TryGetValue("multiple", out var multiple) && multiple.GetBool();
            if ((session.Types & 3) == 0 || (session.Types & ~3u) != 0 || session.Cursor is not (1 or 2 or 4)) return (2, []);
            session.Selected = true;
            return (0, []);
        }
        if (method != "Start" || (!session.Selected && !session.Remote)) return (2, []);
        if (session.Remote && !session.Selected)
        {
            var consent = PortalDialog.Create(() => new PortalDialog("Allow remote control?", $"{DisplayApp(session.App)} wants to control your computer. Requested access: {Devices(session.Devices)}. Only allow applications you trust.", "Allow remote control", "Deny"));
            using var cancelConsent = stop.Register(() => Dispatcher.UIThread.Post(consent.Close));
            if (!await consent.Present() || stop.IsCancellationRequested) return (1, []);
            if (!session.Selected)
            {
                session.Started = true;
                ShowIndicator(args[1], session);
                return (0, new() { ["devices"] = session.Devices });
            }
        }
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(stop, session.Stop.Token);
        var token = cancelled.Token;
        var sources = await SourcesAsync(session.Types, token);
        token.ThrowIfCancellationRequested();
        if (sources.Count == 0) return (2, []);
        var dialog = PortalDialog.Create(() => new PortalDialog(session.Remote ? "Share screen and control" : "Share your screen",
            session.Remote ? $"{DisplayApp(session.App)} wants to view your screen and control {Devices(session.Devices)}. Choose what to share."
                : $"{DisplayApp(session.App)} wants to view your screen. Choose what to share. Everything visible in the selected screen or window can be recorded.",
            session.Remote ? "Share and allow control" : "Share"));
        var choices = new List<(Source Source, CheckBox Check)>();
        foreach (var source in sources)
        {
            var check = new CheckBox { Content = source.Label };
            check.IsCheckedChanged += (_, _) =>
            {
                if (check.IsChecked == true && !session.Multiple)
                    foreach (var other in choices.Where(item => item.Check != check)) other.Check.IsChecked = false;
                dialog.Confirm.IsEnabled = choices.Any(item => item.Check.IsChecked == true);
            };
            choices.Add((source, check)); dialog.Fields.Children.Add(check);
        }
        if (choices.Count == 1) choices[0].Check.IsChecked = true;
        dialog.Confirm.IsEnabled = choices.Any(item => item.Check.IsChecked == true);
        using var cancelDialog = token.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        if (!await dialog.Present() || token.IsCancellationRequested) return (1, []);
        var streams = new Tmds.DBus.Protocol.Array<Struct<uint, Dict<string, VariantValue>>>();
        try
        {
            foreach (var (source, _) in choices.Where(item => item.Check.IsChecked == true))
            {
                var start = new ProcessStartInfo(Helper) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
                start.ArgumentList.Add(source.Window ? "--window" : "--output"); start.ArgumentList.Add(source.Id); start.ArgumentList.Add(session.Cursor.ToString());
                var process = Process.Start(start) ?? throw new IOException("Could not start screen capture.");
                session.Streams.Add(process);
                var line = await process.StandardOutput.ReadLineAsync(token).AsTask().WaitAsync(TimeSpan.FromSeconds(10), token);
                if (line is null || !line.StartsWith("node ") || !uint.TryParse(line[5..], out var node)) throw new IOException("The selected source is no longer available.");
                var properties = new Dict<string, VariantValue> { ["source_type"] = source.Window ? 2u : 1u };
                if (!source.Window)
                {
                    properties["position"] = VariantValue.Struct(source.X, source.Y);
                    properties["size"] = VariantValue.Struct(source.Width, source.Height);
                }
                streams.Add(new Struct<uint, Dict<string, VariantValue>>(node, properties));
                _ = WatchAsync(args[1], session, process);
            }
            session.Started = true;
            ShowIndicator(args[1], session);
            var results = new Dictionary<string, VariantValue> { ["streams"] = streams };
            if (session.Remote) results["devices"] = session.Devices;
            return (0, results);
        }
        catch { Close(args[1], true); throw; }
    }

    private static string Devices(uint value) => string.Join(", ", new[] { (1u, "keyboard"), (2u, "pointer"), (4u, "touch input") }.Where(item => (value & item.Item1) != 0).Select(item => item.Item2));
    private void ShowIndicator(string path, Session session)
    {
        var indicator = PortalSharingIndicator.Create(session.Remote ? "Remote control" : "Screen sharing",
            $"Sharing with {DisplayApp(session.App)}", () => Close(path, true));
        indicator.Closed += (_, _) => Close(path, true);
        session.Indicator = indicator; indicator.Show();
    }

    public async Task<SafeFileHandle> ConnectToEisAsync(string path, string app)
    {
        if (!_sessions.TryGetValue(path, out var session) || session.App != app || !session.Started || !session.Remote || session.Devices == 0)
            throw new InvalidOperationException("Remote control has not been authorized.");
        MessageBuffer call;
        using (var writer = bus.GetMessageWriter())
        {
            writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: "/org/kde/KWin/EIS/RemoteDesktop", @interface: "org.kde.KWin.EIS.RemoteDesktop", member: "connectToEIS", signature: "i");
            writer.WriteInt32((int)session.Devices); call = writer.CreateMessage();
        }
        var result = await bus.CallMethodAsync(call, (reply, _) =>
        {
            var reader = reply.GetBodyReader(); return (Handle: reader.ReadHandle<SafeFileHandle>(), Id: reader.ReadInt32());
        }, (object?)null);
        if (!_sessions.ContainsKey(path)) { result.Handle.Dispose(); DisconnectEis(result.Id); throw new OperationCanceledException(); }
        session.EisConnections.Add(result.Id);
        return result.Handle;
    }
    private void DisconnectEis(int id)
    {
        using var writer = bus.GetMessageWriter();
        writer.WriteMethodCallHeader(destination: "org.kde.KWin", path: "/org/kde/KWin/EIS/RemoteDesktop", @interface: "org.kde.KWin.EIS.RemoteDesktop", member: "disconnect", signature: "i"); writer.WriteInt32(id);
        _ = bus.CallMethodAsync(writer.CreateMessage());
    }

    private async Task WatchAsync(string path, Session session, Process process)
    {
        try { await process.WaitForExitAsync(session.Stop.Token); await Dispatcher.UIThread.InvokeAsync(() => Close(path, true)); }
        catch (OperationCanceledException) { }
    }
    public void Close(string path, bool signal = false)
    {
        if (!_sessions.Remove(path, out var session)) return;
        session.Stop.Cancel();
        foreach (var id in session.EisConnections) DisconnectEis(id);
        foreach (var stream in session.Streams)
        {
            try { if (!stream.HasExited) { stream.StandardInput.Close(); if (!stream.WaitForExit(500)) stream.Kill(); } } catch (InvalidOperationException) { }
            stream.Dispose();
        }
        session.Indicator?.Close(); session.Stop.Dispose();
        if (signal)
        {
            using var writer = bus.GetMessageWriter(); writer.WriteSignalHeader(path: path, @interface: "org.freedesktop.impl.portal.Session", member: "Closed"); bus.TrySendMessage(writer.CreateMessage());
        }
    }
    private async Task<List<Source>> SourcesAsync(uint types, CancellationToken stop)
    {
        var sources = new List<Source>();
        if ((types & 1) != 0)
        {
            var start = new ProcessStartInfo(Helper) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false }; start.ArgumentList.Add("--list");
            using var process = Process.Start(start) ?? throw new IOException("Capture unavailable.");
            var listing = await process.StandardOutput.ReadToEndAsync(stop); await process.WaitForExitAsync(stop);
            if (process.ExitCode != 0) throw new IOException("Compositor capture capability is unavailable.");
            foreach (var line in listing.Split('\n'))
            {
                var parts = line.Split('\t');
                if (parts.Length == 6 && parts[0] == "output") sources.Add(new Source(parts[1], "Screen · " + parts[1], false, int.Parse(parts[2]), int.Parse(parts[3]), int.Parse(parts[4]), int.Parse(parts[5])));
            }
        }
        if ((types & 2) != 0)
        {
            MessageBuffer call;
            using (var writer = bus.GetMessageWriter())
            {
                writer.WriteMethodCallHeader(destination: KWinWindowBridge.BusName, path: KWinWindowBridge.ObjectPath, @interface: KWinWindowBridge.InterfaceName, member: "GetCaptureWindows"); call = writer.CreateMessage();
            }
            var json = await bus.CallMethodAsync(call, (reply, _) => reply.GetBodyReader().ReadString(), (object?)null).WaitAsync(TimeSpan.FromSeconds(3), stop);
            if (KWinWindowSnapshotParser.TryParse(json, out var windows))
                sources.AddRange(windows.Where(window => window.AppId != "org.daisyos.DesktopServices").Select(window => new Source(window.Id, "Window · " + window.Title, true)));
        }
        return sources;
    }
    private static string DisplayApp(string id) => string.IsNullOrWhiteSpace(id) ? "this application" : id;
    public void Dispose() { foreach (var path in _sessions.Keys.ToArray()) Close(path); }
}
