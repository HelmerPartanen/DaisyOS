using System.Diagnostics;
using System.Text;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;

namespace DaisyOS.Shell.Services.DesktopIntegration;

internal sealed class AuthenticationAgent : IDisposable
{
    private Process? _process;
    private bool _stopping;
    public event Action? Stopped;
    private readonly TaskCompletionSource _registered = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Window? _dialog;
    private StackPanel? _fields;
    private TextBlock? _status;
    private TextBox? _response;
    private Button? _submit;
    private ComboBox? _identities;
    private readonly List<(string Uid, string Name)> _users = [];
    private string _message = "";
    private string _action = "";
    private bool _completed;

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
        finally { _registered.TrySetException(new IOException("Authentication agent could not register.")); await Dispatcher.UIThread.InvokeAsync(() => { _completed = true; _dialog?.Close(); if (!_stopping) Stopped?.Invoke(); }); }
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
            case "begin": _users.Clear(); _message = text; _completed = false; break;
            case "action": _action = text; break;
            case "identity":
                var split = text.Split(':', 2); _users.Add((split[0], split.Length > 1 ? split[1] : split[0])); break;
            case "ready": Show(); break;
            case "password": case "echo":
                if (_fields is null || _response is null || _submit is null || _status is null) break;
                _status.Text = text; _response.PasswordChar = kind == "password" ? '●' : '\0';
                _response.Text = ""; _response.IsEnabled = true; _submit.IsEnabled = true; _submit.IsDefault = true; _response.Focus(); break;
            case "info": if (_status is not null) _status.Text = text; break;
            case "success": case "failed": case "cancelled":
                _completed = true; if (_response is not null) _response.Text = ""; _dialog?.Close(); break;
        }
    }

    private void Show()
    {
        var window = PortalDialog.Create(() => new Window
        {
            Title = Loc.Get("Authentication required"), Width = 480, CanResize = false, CanMinimize = false, CanMaximize = false,
            SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterScreen
        });
        _dialog = window;
        window.Bind(Window.BackgroundProperty, window.GetResourceObservable("SurfaceBaseSolidBrush"));
        _fields = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 14 };
        _fields.Children.Add(new TextBlock { Text = Loc.Get("Authentication required"), FontSize = 18, FontWeight = Avalonia.Media.FontWeight.SemiBold });
        _fields.Children.Add(new TextBlock { Text = _message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _fields.Children.Add(new TextBlock { Text = _action, FontSize = 11, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
        _identities = new ComboBox { ItemsSource = _users.Select(user => user.Name).ToArray(), SelectedIndex = 0 };
        _fields.Children.Add(_identities);
        _status = new TextBlock { Text = Loc.Get("Choose an account to continue."), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        _fields.Children.Add(_status);
        _response = new TextBox { PasswordChar = '●', IsEnabled = false };
        _fields.Children.Add(_response);
        var cancel = new Button { Content = Loc.Get("Cancel"), Classes = { "Secondary" }, IsCancel = true };
        _submit = new Button { Content = Loc.Get("Authenticate"), Classes = { "Primary" } };
        var started = false;
        _submit.Click += (_, _) =>
        {
            if (!started)
            {
                if (_identities.SelectedIndex < 0) return;
                started = true; _identities.IsEnabled = false; Send("identity", _users[_identities.SelectedIndex].Uid);
            }
            else
            {
                var response = _response.Text ?? "";
                _response.Text = ""; _response.IsEnabled = false; Send("response", response);
            }
            _submit.IsEnabled = false;
        };
        cancel.Click += (_, _) => window.Close();
        _fields.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, _submit } });
        window.Content = _fields;
        window.Closed += (_, _) => { if (!_completed) Send("cancel"); _response.Text = ""; _dialog = null; };
        window.Show(); cancel.Focus();
    }

    public void Dispose()
    {
        _stopping = true;
        if (_process is { HasExited: false }) { _process.StandardInput.Close(); if (!_process.WaitForExit(1000)) _process.Kill(); }
        _process?.Dispose();
    }
}
