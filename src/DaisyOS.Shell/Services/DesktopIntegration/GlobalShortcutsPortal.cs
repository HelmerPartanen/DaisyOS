using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Avalonia.Controls;
using Avalonia.Threading;
using Tmds.DBus.Protocol;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class GlobalShortcutsPortal(DBusConnection bus) : IDisposable
{
    private sealed class Session(string app)
    {
        public string App { get; } = app;
        public List<(string Id, string Description, string Key)> Bindings { get; } = [];
        public Process? Helper;
        public CancellationTokenSource Stop { get; } = new();
    }
    private readonly Dictionary<string, Session> _sessions = [];
    public async Task<(uint Code, Dictionary<string, VariantValue> Results)> HandleAsync(string method, List<string> args, Dictionary<string, VariantValue> options, CancellationToken stop)
    {
        if (method == "CreateSession") return (_sessions.TryAdd(args[1], new Session(args[2])) ? 0u : 2u, []);
        if (!_sessions.TryGetValue(args[1], out var session)) return (2, []);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, session.Stop.Token);
        stop = linked.Token;
        if (method == "ListShortcuts") return (0, Results(session));
        var shortcuts = options["daisy_shortcuts"];
        var dialog = PortalDialog.Create(() => new PortalDialog("Global keyboard shortcuts", $"{session.App} wants shortcuts that work while other applications are focused. Leave a shortcut empty to disable it.", "Save shortcuts", "Deny"));
        var fields = new List<(string Id, string Description, TextBox Input)>();
        for (var i = 0; i < shortcuts.Count; i++)
        {
            var shortcut = shortcuts.GetItem(i);
            var id = shortcut.GetItem(0).GetString();
            var properties = shortcut.GetItem(1).GetDictionary<string, VariantValue>();
            var description = properties.TryGetValue("description", out var label) ? label.GetString() : id;
            var preferred = properties.TryGetValue("preferred_trigger", out var trigger) ? trigger.GetString() : "";
            var input = new TextBox { Text = session.Bindings.FirstOrDefault(binding => binding.Id == id).Key ?? Normalize(preferred), PlaceholderText = "Ctrl+Alt+S" };
            dialog.Fields.Children.Add(new TextBlock { Text = description, TextWrapping = Avalonia.Media.TextWrapping.Wrap }); dialog.Fields.Children.Add(input);
            fields.Add((id, description, input));
        }
        using var cancel = stop.Register(() => Dispatcher.UIThread.Post(dialog.Close));
        if (!await dialog.Present() || stop.IsCancellationRequested) return (1, []);
        using var cancelRegistration = stop.Register(() => Dispatcher.UIThread.Post(() => Close(args[1])));
        stop.ThrowIfCancellationRequested();
        StopHelper(session);
        session.Bindings.Clear();
        session.Bindings.AddRange(fields.Select(field => (field.Id, field.Description, field.Input.Text?.Trim() ?? "")));
        var start = new ProcessStartInfo(global::System.IO.Path.Combine(AppContext.BaseDirectory, "daisyos-portal-shortcuts")) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        var process = Process.Start(start) ?? throw new IOException("Shortcut registration is unavailable.");
        session.Helper = process;
        using var payload = new MemoryStream();
        using (var json = new Utf8JsonWriter(payload))
        {
            json.WriteStartArray();
            foreach (var binding in session.Bindings)
            { json.WriteStartObject(); json.WriteString("id", binding.Id); json.WriteString("description", binding.Description); json.WriteString("key", binding.Key); json.WriteEndObject(); }
            json.WriteEndArray();
        }
        await process.StandardInput.WriteLineAsync(Encoding.UTF8.GetString(payload.ToArray())); await process.StandardInput.FlushAsync(stop);
        var ready = await process.StandardOutput.ReadLineAsync(stop).AsTask().WaitAsync(TimeSpan.FromSeconds(5), stop);
        if (ready != "ready")
        {
            StopHelper(session); session.Bindings.Clear();
            var error = PortalDialog.Create(() => new PortalDialog("Shortcut unavailable", "One of the shortcuts is already in use or could not be registered. Choose another shortcut.", "OK"));
            using var cancelError = stop.Register(() => Dispatcher.UIThread.Post(error.Close));
            await error.Present(); return (2, []);
        }
        _ = ObserveAsync(args[1], process);
        return (0, Results(session));
    }
    private async Task ObserveAsync(string path, Process process)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var parts = line.Split('\t');
                if (parts.Length != 3 || parts[0] is not ("pressed" or "released") || !ulong.TryParse(parts[2], out var timestamp)) continue;
                using var writer = bus.GetMessageWriter();
                writer.WriteSignalHeader(path: "/org/freedesktop/portal/desktop", @interface: "org.freedesktop.impl.portal.GlobalShortcuts", member: parts[0] == "pressed" ? "Activated" : "Deactivated", signature: "osta{sv}");
                writer.WriteObjectPath(path); writer.WriteString(Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]))); writer.WriteUInt64(timestamp); writer.WriteDictionary(new Dictionary<string, VariantValue>()); bus.TrySendMessage(writer.CreateMessage());
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[portal] Shortcuts stopped: {ex.Message}"); }
    }
    private static string Normalize(string text) => text.Replace("<Control>", "Ctrl+", StringComparison.OrdinalIgnoreCase).Replace("<Ctrl>", "Ctrl+", StringComparison.OrdinalIgnoreCase).Replace("<Alt>", "Alt+", StringComparison.OrdinalIgnoreCase).Replace("<Shift>", "Shift+", StringComparison.OrdinalIgnoreCase).Replace("<Super>", "Meta+", StringComparison.OrdinalIgnoreCase);
    private static Dictionary<string, VariantValue> Results(Session session)
    {
        var array = new Tmds.DBus.Protocol.Array<Struct<string, Dict<string, VariantValue>>>();
        foreach (var binding in session.Bindings) array.Add(new Struct<string, Dict<string, VariantValue>>(binding.Id, new Dict<string, VariantValue> { ["description"] = binding.Description, ["trigger_description"] = binding.Key }));
        return new() { ["shortcuts"] = array };
    }
    private static void StopHelper(Session session)
    {
        if (session.Helper is not { } process) return;
        if (!process.HasExited) { process.StandardInput.Close(); if (!process.WaitForExit(1000)) process.Kill(); }
        process.Dispose(); session.Helper = null;
    }
    public void Close(string path)
    {
        if (!_sessions.Remove(path, out var session)) return;
        session.Stop.Cancel(); StopHelper(session); session.Stop.Dispose();
    }
    public void Dispose() { foreach (var path in _sessions.Keys.ToArray()) Close(path); }
}
