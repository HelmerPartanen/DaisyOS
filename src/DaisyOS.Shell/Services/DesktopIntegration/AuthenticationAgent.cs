using System.Diagnostics;
using System.Text;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class AuthenticationAgent : IDisposable
{
    private Process? _process;
    private bool _stopping;
    public event Action? Stopped;
    private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private AuthenticationDialog? _dialog;
    private readonly List<(string Uid, string Name)> _users = [];
    private string _message = "";
    private string _action = "";
    private string _program = "";

    public async Task StartAsync()
    {
        var helper = global::System.IO.Path.Combine(AppContext.BaseDirectory, "daisyos-polkit-agent");
        _process = Process.Start(new ProcessStartInfo(helper) { RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false });
        if (_process is null) throw new IOException("Could not start authentication agent.");
        _ = ReadAsync(_process);
        await _registered.Task.WaitAsync(TimeSpan.FromSeconds(10));
    }

    private async Task ReadAsync(Process process)
    {
        _ = Task.Run(async () => { while (await process.StandardError.ReadLineAsync() is { } line) Console.Error.WriteLine($"[authentication] {line}"); });
        try
        {
            while (await process.StandardOutput.ReadLineAsync() is { } line)
            {
                var parts = line.Split('\t', 2);
                var text = parts.Length == 2 ? Encoding.UTF8.GetString(Convert.FromBase64String(parts[1])) : "";
                await Dispatcher.UIThread.InvokeAsync(() => Receive(parts[0], text));
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[authentication] Agent stopped: {ex.Message}"); }
        finally { _registered.TrySetException(new IOException("Authentication agent could not register.")); await Dispatcher.UIThread.InvokeAsync(() => { _dialog?.Complete(false, cancelled: true); if (!_stopping) Stopped?.Invoke(); }); }
    }

    private void Send(string kind, string value = "")
    {
        if (_process is not { HasExited: false }) return;
        _process.StandardInput.WriteLine(kind + "\t" + Convert.ToBase64String(Encoding.UTF8.GetBytes(value)));
        _process.StandardInput.Flush();
    }

    private void Receive(string kind, string text)
    {
        switch (kind)
        {
            case "registered": _registered.TrySetResult(); break;
            case "begin": _dialog?.Complete(false, cancelled: true); _users.Clear(); _program = ""; _message = text; break;
            case "action": _action = text; break;
            case "program": _program = text; break;
            case "identity":
                var split = text.Split(':', 2); _users.Add((split[0], split.Length > 1 ? split[1] : split[0])); break;
            case "ready": Show(); break;
            case "password": case "echo": _dialog?.Prompt(text, kind == "echo"); break;
            case "info": case "error": _dialog?.ShowStatus(text, kind == "error"); break;
            case "success": case "failed": case "cancelled":
                _dialog?.Complete(kind == "success", kind == "cancelled"); break;
        }
    }

    internal static int SelectIdentity(IReadOnlyList<(string Uid, string Name)> users, string currentUser)
    {
        for (var index = 0; index < users.Count; index++)
            if (string.Equals(users[index].Name, currentUser, StringComparison.Ordinal)) return index;
        // A policy may require a different administrator. Never submit an
        // identity that PolicyKit did not offer, or imply its password is ours.
        return users.Count == 1 ? 0 : -1;
    }

    private void Show()
    {
        var selected = SelectIdentity(_users, Environment.UserName);
        if (selected < 0) { Send("cancel"); return; }
        var identity = _users[selected];
        var window = PortalDialog.Create(() => new AuthenticationDialog(_message, _action, identity.Name, Send, _program), useWindowChrome: false);
        _dialog = window;
        window.Closed += (_, _) => { if (ReferenceEquals(_dialog, window)) _dialog = null; };
        window.Show();
        Send("identity", identity.Uid);
    }

    public void Dispose()
    {
        _stopping = true;
        if (_process is { HasExited: false }) { _process.StandardInput.Close(); if (!_process.WaitForExit(1000)) _process.Kill(); }
        _process?.Dispose();
    }
}
