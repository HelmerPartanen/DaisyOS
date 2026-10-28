using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Threading;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.System.Continuity;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private readonly ContinuityClient _continuity = new();
    private CancellationTokenSource? _continuityPage;
    private StackPanel? _continuityLive;
    private string? _continuityStructure;
    private readonly Dictionary<string, (TextBlock Label, ProgressBar Progress)> _continuityTransferControls = new();
    private string? _continuitySendPath;

    public void PrepareContinuitySend(string path)
    {
        _continuitySendPath = path;
        SelectCategory("Continuity");
    }
    private void BuildContinuity(int version)
    {
        _continuityPage?.Cancel(); _continuityPage?.Dispose();
        _continuityPage = new();
        _ = LoadContinuityAsync(version, _continuityPage.Token);
    }
    private async Task LoadContinuityAsync(int version, CancellationToken token)
    {
        try
        {
            var state = await _continuity.ExecuteAsync(new("status"), token);
            if (version != _categoryBuildVersion || token.IsCancellationRequested) return;
            AddSection(Loc.Get("CONTINUITY"), Loc.Get("Works across local Wi-Fi and Ethernet. Both devices must be reachable on the same local network."),
                Toggle("Continuity", "continuity.enabled", state.Enabled, value => _ = SetContinuityEnabledAsync(value)),
                Toggle("Visible to nearby devices", "continuity.visible", state.Visible, value => _ = ContinuityActionAsync(new("visible", Allowed: value))),
                Toggle("Pause Continuity", "continuity.paused", state.Paused, value => _ = ContinuityActionAsync(new("pause", Allowed: value))));
            var name = new TextBox { Text = state.Name, Watermark = Loc.Get("Device name"), MinWidth = 180 };
            var rename = ContinuityButton("Save name", () => new("name", Value: name.Text ?? "DaisyOS"));
            AddSection(Loc.Get("THIS DEVICE"), null, PreferenceRow(Loc.Get("Device name"), null, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { name, rename } }),
                InfoRow(Loc.Get("Public identity fingerprint"), state.DeviceId));
            if (_continuitySendPath is { } selected) AddSection(Loc.Get("SEND FILE"), null, InfoRow(Loc.Get("Selected file"), Path.GetFileName(selected)));
            _continuityStructure = null; _continuityTransferControls.Clear();
            _continuityLive = new StackPanel { Spacing = 12 };
            CategoryContent.Children.Add(_continuityLive);
            UpdateContinuityLive(state);
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (version != _categoryBuildVersion || _activeCategory != "Continuity") return;
                var refreshed = await _continuity.ExecuteAsync(new("status"), token);
                UpdateContinuityLive(refreshed);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            if (version == _categoryBuildVersion && !token.IsCancellationRequested)
            {
                AddUnavailableMessage(Loc.Get("Continuity service is unavailable. Start the per-user service, then reopen this page."));
                var start = new Button { Content = Loc.Get("Start Continuity service") };
                start.Click += async (_, _) =>
                {
                    await new DaisyOS.System.Processes.SafeCommandRunner().RunAsync("systemctl", ["--user", "start", "daisyos-continuity.service"], TimeSpan.FromSeconds(5));
                    SelectCategory("Continuity");
                };
                CategoryContent.Children.Add(start);
            }
        }
    }
    private async Task SetContinuityEnabledAsync(bool enabled)
    {
        try
        {
            // Turning sharing off must succeed independently of startup-unit state.
            if (!enabled) UpdateContinuityLive(await _continuity.ExecuteAsync(new("enable", Allowed: false)));
            if (enabled) await EnsureContinuityFirewallAllowedAsync();
            var result = await new DaisyOS.System.Processes.SafeCommandRunner().RunAsync("systemctl",
                ["--user", enabled ? "enable" : "disable", "daisyos-continuity.service"], TimeSpan.FromSeconds(5));
            if (!result.Succeeded) throw new IOException();
            if (enabled) UpdateContinuityLive(await _continuity.ExecuteAsync(new("enable", Allowed: true)));
        }
        catch
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t update Continuity startup. Check that the user service is installed."));
            if (_activeCategory == "Continuity") SelectCategory("Continuity");
        }
    }
    private static async Task EnsureContinuityFirewallAllowedAsync()
    {
        try
        {
            if (!Directory.Exists("/etc/firewalld/zones")) return;
            var configured = Directory.EnumerateFiles("/etc/firewalld/zones", "*.xml")
                .Any(path => File.ReadAllText(path).Contains("47621", StringComparison.Ordinal));
            if (configured) return;
            var runner = new DaisyOS.System.Processes.SafeCommandRunner();
            var active = await runner.RunAsync("systemctl", ["is-active", "--quiet", "firewalld"], TimeSpan.FromSeconds(2));
            if (!active.Succeeded) return;
            await runner.RunAsync("pkexec",
                ["sh", "-c", "firewall-cmd --permanent --add-port=47621/tcp --add-port=47621/udp --add-service=mdns && firewall-cmd --add-port=47621/tcp --add-port=47621/udp --add-service=mdns"],
                TimeSpan.FromSeconds(60));
        }
        catch { }
    }
    private Button ContinuityButton(string label, Func<ContinuityCommand> command)
    {
        var button = new Button { Content = Loc.Get(label), Classes = { "Subtle", "Small" } };
        button.Click += async (_, _) => { button.IsEnabled = false; try { await ContinuityActionAsync(command()); } finally { button.IsEnabled = true; } };
        return button;
    }
    private async Task ContinuityActionAsync(ContinuityCommand command)
    {
        try
        {
            var state = await _continuity.ExecuteAsync(command);
            UpdateContinuityLive(state);
        }
        catch { (Application.Current as App)?.Feedback.Show(Loc.Get("Continuity couldn’t complete the request. Check the service, session and device connection.")); }
    }
    private void UpdateContinuityLive(ContinuityStatus state)
    {
        if (_activeCategory != "Continuity" || _continuityLive is null) return;
        var structure = global::System.Text.Json.JsonSerializer.Serialize(
            state with { Transfers = state.Transfers.Select(t => t with { Completed = 0 }).ToArray() },
            DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.ContinuityStatus);
        if (_continuityStructure == structure)
        {
            foreach (var transfer in state.Transfers)
                if (_continuityTransferControls.TryGetValue(transfer.Id, out var controls))
                {
                    controls.Label.Text = $"{transfer.Name} · {transfer.Status} · {transfer.Completed:N0}/{transfer.Length:N0}";
                    controls.Progress.Value = transfer.Completed;
                }
            return;
        }
        _continuityStructure = structure; _continuityTransferControls.Clear();
        _continuityLive.Children.Clear();
        _continuityLive.Children.Add(Secondary(Loc.Get(state.SessionReady ? state.Diagnostic : "Sharing is unavailable while locked or outside an active DaisyOS session.")));
        _continuityLive.Children.Add(Secondary(Loc.Get(state.ClipboardStatus)));
        foreach (var prompt in state.Prompts)
        {
            var row = new StackPanel { Spacing = 8 };
            row.Children.Add(new TextBlock { Text = prompt.Name, Classes = { "SettingsSectionTitle" } });
            row.Children.Add(new SelectableTextBlock { Text = prompt.Detail, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            row.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children =
            { ContinuityButton(prompt.Kind == "pair" ? "Both fingerprints match — approve" : "Accept file", () => new("decide", Id: prompt.Id, Allowed: true)),
              ContinuityButton("Decline", () => new("decide", Id: prompt.Id, Allowed: false)) } });
            _continuityLive.Children.Add(row);
        }
        foreach (var peer in state.Devices)
        {
            var controls = new StackPanel { Spacing = 8 };
            controls.Children.Add(new TextBlock { Text = peer.Name + " · " + Loc.Get(peer.Status), Classes = { "SettingsSectionTitle" } });
            if (!peer.Trusted)
                controls.Children.Add(ContinuityButton("Pair", () => new("pair", peer.Id, peer.Address)));
            else
            {
                controls.Children.Add(Toggle("Text clipboard", "continuity.peer.clipboard", peer.Clipboard,
                    allowed => _ = ContinuityActionAsync(new("permission", peer.Id, "clipboard", allowed))));
                controls.Children.Add(Toggle("File sharing", "continuity.peer.files", peer.Files,
                    allowed => _ = ContinuityActionAsync(new("permission", peer.Id, "files", allowed))));
                controls.Children.Add(ContinuityButton("Connect", () => new("connect", peer.Id, peer.Address)));
                controls.Children.Add(ContinuityButton("Forget and revoke", () => new("forget", peer.Id)));
                if (_continuitySendPath is { } path)
                    controls.Children.Add(ContinuityButton("Send selected file", () => new("send", peer.Id, path)));
            }
            _continuityLive.Children.Add(controls);
        }
        if (state.Devices.Length == 0) _continuityLive.Children.Add(Secondary(Loc.Get("No devices found. Check guest Wi-Fi isolation, Avahi, and the TCP firewall, or connect by address.")));
        foreach (var transfer in state.Transfers)
        {
            var row = new StackPanel { Spacing = 4 };
            var label = new TextBlock { Text = $"{transfer.Name} · {transfer.Status} · {transfer.Completed:N0}/{transfer.Length:N0}" };
            var progress = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, transfer.Length), Value = transfer.Completed };
            row.Children.Add(label); row.Children.Add(progress);
            _continuityTransferControls[transfer.Id] = (label, progress);
            if (transfer.Status is not ("Complete" or "Failed" or "Cancelled")) row.Children.Add(ContinuityButton("Cancel transfer", () => new("cancel", transfer.Id)));
            _continuityLive.Children.Add(row);
        }
        _continuityLive.Children.Add(Secondary(Loc.Get("Keyboard & Mouse, app handoff and display streaming are not available in this release.")));
    }
}
