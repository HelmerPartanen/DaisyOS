using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.System.Continuity;
using DaisyOS.System.Processes;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private readonly ContinuityClient _continuity = new();
    private CancellationTokenSource? _continuityPage;
    private StackPanel? _continuityLive;
    private string? _continuityStructure;
    private readonly Dictionary<string, (TextBlock Label, ProgressBar Progress)> _continuityTransferControls = new();
    private string? _continuitySendPath;
    private readonly HashSet<string> _continuityExpandedAccess = new();

    public void PrepareContinuitySend(string path)
    {
        _continuitySendPath = path;
        SelectCategory("Continuity");
    }

    private void BuildContinuity(int version)
    {
        _continuityPage?.Cancel();
        _continuityPage?.Dispose();
        _continuityPage = new CancellationTokenSource();
        _ = LoadContinuityAsync(version, _continuityPage.Token);
    }

    private async Task LoadContinuityAsync(int version, CancellationToken token)
    {
        try
        {
            var state = await _continuity.ExecuteAsync(new("status"), token);
            if (version != _categoryBuildVersion || token.IsCancellationRequested) return;
            BuildContinuityPage(state);

            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));
            while (await timer.WaitForNextTickAsync(token))
            {
                if (version != _categoryBuildVersion || _activeCategory != "Continuity") return;
                UpdateContinuityLive(await _continuity.ExecuteAsync(new("status"), token));
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        catch
        {
            if (version == _categoryBuildVersion && !token.IsCancellationRequested) BuildContinuityUnavailable();
        }
    }

    private void BuildContinuityPage(ContinuityStatus state)
    {
        while (CategoryContent.Children.Count > 1)
            CategoryContent.Children.RemoveAt(CategoryContent.Children.Count - 1);

        AddSection(Loc.Get("CONTINUITY"), Loc.Get("Nearby trusted devices connect automatically on your local network."),
            Toggle("Continuity", "continuity.enabled", state.Enabled, value => _ = SetContinuityEnabledAsync(value)));

        if (state.Enabled)
        {
            var name = new TextBox
            {
                Text = state.Name,
                PlaceholderText = Loc.Get("Device name"),
                MinWidth = 200,
                MaxWidth = 320,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            var savedName = state.Name;
            async Task SaveNameAsync()
            {
                var requested = name.Text?.Trim();
                if (string.IsNullOrWhiteSpace(requested) || requested == savedName)
                {
                    name.Text = savedName;
                    return;
                }
                name.IsEnabled = false;
                try
                {
                    var refreshed = await _continuity.ExecuteAsync(new("name", Value: requested));
                    savedName = refreshed.Name;
                    name.Text = savedName;
                    UpdateContinuityLive(refreshed);
                }
                catch
                {
                    name.Text = savedName;
                    (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t rename this device."));
                }
                finally { name.IsEnabled = true; }
            }
            name.LostFocus += async (_, _) => await SaveNameAsync();
            name.KeyDown += async (_, args) =>
            {
                if (args.Key != Key.Enter) return;
                args.Handled = true;
                await SaveNameAsync();
            };
            AddSection(Loc.Get("THIS DEVICE"), Loc.Get("This is the name shown to nearby DaisyOS devices."),
                PreferenceRow(Loc.Get("Device name"), Loc.Get("Changes save automatically."), name, "computer-settings"));
        }

        _continuityStructure = null;
        _continuityTransferControls.Clear();
        _continuityLive = new StackPanel { Spacing = 24 };
        CategoryContent.Children.Add(_continuityLive);
        UpdateContinuityLive(state);
    }

    private void BuildContinuityUnavailable()
    {
        var start = new Button { Content = Loc.Get("Turn on Continuity") };
        start.Classes.Add("Primary");
        start.Click += async (_, _) =>
        {
            start.IsEnabled = false;
            try
            {
                await EnsureContinuityFirewallAllowedAsync();
                var result = await new SafeCommandRunner().RunAsync("systemctl",
                    ["--user", "enable", "--now", "daisyos-continuity.service"], TimeSpan.FromSeconds(8));
                if (!result.Succeeded) throw new IOException();
                await Task.Delay(250);
                SelectCategory("Continuity");
            }
            catch
            {
                start.IsEnabled = true;
                (Application.Current as App)?.Feedback.Show(Loc.Get("Continuity isn’t available yet. Check that the Continuity service is installed."));
            }
        };
        AddSection(Loc.Get("GET STARTED"), Loc.Get("Turn on Continuity to find and pair with nearby DaisyOS devices."),
            PreferenceRow(Loc.Get("Continuity"), Loc.Get("Uses your local Wi-Fi or Ethernet connection. No account or internet connection is required."), start, "continuity"));
    }

    private async Task SetContinuityEnabledAsync(bool enabled)
    {
        try
        {
            if (!enabled) UpdateContinuityLive(await _continuity.ExecuteAsync(new("enable", Allowed: false)));
            if (enabled) await EnsureContinuityFirewallAllowedAsync();
            var result = await new SafeCommandRunner().RunAsync("systemctl",
                ["--user", enabled ? "enable" : "disable", "--now", "daisyos-continuity.service"], TimeSpan.FromSeconds(8));
            if (!result.Succeeded) throw new IOException();
            if (enabled)
            {
                await Task.Delay(250);
                UpdateContinuityLive(await _continuity.ExecuteAsync(new("enable", Allowed: true)));
            }
        }
        catch
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t update Continuity. Check that the Continuity service is installed."));
        }
        finally
        {
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
            var runner = new SafeCommandRunner();
            var active = await runner.RunAsync("systemctl", ["is-active", "--quiet", "firewalld"], TimeSpan.FromSeconds(2));
            if (!active.Succeeded) return;
            await runner.RunAsync("firewall-cmd", ["--permanent", "--add-port=47621/tcp", "--add-port=47621/udp"], TimeSpan.FromSeconds(60));
            await runner.RunAsync("firewall-cmd", ["--reload"], TimeSpan.FromSeconds(30));
        }
        catch { }
    }

    private Button ContinuityButton(string label, Func<ContinuityCommand> command, bool primary = false)
    {
        var button = new Button { Content = Loc.Get(label) };
        button.Classes.Add(primary ? "Primary" : "Subtle");
        button.Classes.Add("Small");
        button.Click += async (_, _) =>
        {
            button.IsEnabled = false;
            try { await ContinuityActionAsync(command()); }
            finally { button.IsEnabled = true; }
        };
        return button;
    }

    private async Task ContinuityActionAsync(ContinuityCommand command)
    {
        try
        {
            if (command.Action is "pair" or "connect") await EnsureContinuityFirewallAllowedAsync();
            UpdateContinuityLive(await _continuity.ExecuteAsync(command));
        }
        catch
        {
            (Application.Current as App)?.Feedback.Show(Loc.Get("Continuity couldn’t complete the request. Make sure both devices are awake and on the same local network."));
        }
    }

    private void UpdateContinuityLive(ContinuityStatus state)
    {
        if (_activeCategory != "Continuity" || _continuityLive is null) return;
        var structure = global::System.Text.Json.JsonSerializer.Serialize(
            state with { Transfers = state.Transfers.Select(transfer => transfer with { Completed = 0 }).ToArray() },
            DaisyOS.Core.Serialization.DaisyCoreJsonContext.Default.ContinuityStatus);
        if (_continuityStructure == structure)
        {
            foreach (var transfer in state.Transfers)
            {
                if (!_continuityTransferControls.TryGetValue(transfer.Id, out var controls)) continue;
                controls.Label.Text = TransferLabel(transfer);
                controls.Progress.Value = transfer.Completed;
            }
            return;
        }

        _continuityStructure = structure;
        _continuityTransferControls.Clear();
        _continuityLive.Children.Clear();
        if (!state.Enabled) return;

        foreach (var prompt in state.Prompts)
        {
            var actions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    ContinuityButton(prompt.Kind == "pair" ? "Approve pairing" : prompt.Kind == "file" ? "Accept file" : "Allow this session", () => new("decide", Id: prompt.Id, Allowed: true), true),
                    ContinuityButton("Decline", () => new("decide", Id: prompt.Id, Allowed: false))
                }
            };
            _continuityLive.Children.Add(CreateSection(
                prompt.Kind == "pair" ? Loc.Get("PAIRING REQUEST") : Loc.Get("CONTINUITY REQUEST"),
                prompt.Detail,
                PreferenceRow(prompt.Name, prompt.Kind == "pair" ? Loc.Get("Confirm that the fingerprint shown on both devices matches.") : Loc.Get("Review the requested access before allowing it."), actions,
                    prompt.Kind == "pair" ? "continuity" : "file-download")));
        }

        foreach (var session in state.Sessions ?? [])
        {
            var description = Loc.Get(session.Kind == "audio" ? "Audio playback" : session.Kind == "input" ? "Keyboard and mouse" : session.Control ? "Remote control" : "Screen viewing") + " · " + Loc.Get(session.State);
            if (session.Detail.Length > 0) description += "\n" + Loc.Get(session.Detail);
            _continuityLive.Children.Add(CreateSection(Loc.Get("CONTINUITY"), null,
                PreferenceRow(session.Name, description,
                    session.State is "Failed" or "Declined" ? new TextBlock() : ContinuityButton("Disconnect", () => new("live-stop", session.Id)), "continuity")));
        }

        var paired = state.Devices.Where(device => device.Trusted).ToArray();
        if (paired.Length > 0)
        {
            _continuityLive.Children.Add(CreateSection(Loc.Get("MY DEVICES"), Loc.Get("Trusted devices reconnect automatically when they are nearby."),
                paired.Select(peer => CreateTrustedDeviceRow(peer, state.Sessions ?? [])).ToArray()));
        }

        var nearby = state.Devices.Where(device => !device.Trusted).ToArray();
        var nearbyRows = nearby.Length > 0
            ? nearby.Select(CreateNearbyDeviceRow).ToArray()
            : [InfoRow(Loc.Get("Looking for devices…"), Loc.Get("Keep Continuity open on the other DaisyOS device."))];
        _continuityLive.Children.Add(CreateSection(Loc.Get("NEARBY DEVICES"), Loc.Get("Devices appear automatically when they are on the same local network."), nearbyRows));

        if (state.Transfers.Length > 0)
        {
            _continuityLive.Children.Add(CreateSection(Loc.Get("FILE TRANSFERS"), null,
                state.Transfers.Select(CreateTransferRow).ToArray()));
        }

        if (!state.SessionReady)
        {
            _continuityLive.Children.Add(CreateSection(Loc.Get("AVAILABILITY"), null,
                InfoRow(Loc.Get("Sharing paused"), Loc.Get("Unlock this DaisyOS session to resume Continuity."))));
        }
    }

    private Border CreateNearbyDeviceRow(ContinuityDevice peer) =>
        PreferenceRow(peer.Name, Loc.Get("Ready to pair"),
            ContinuityButton("Pair", () => new("pair", peer.Id, peer.Address), true), "continuity");

    private Border CreateTrustedDeviceRow(ContinuityDevice peer, ContinuityLiveSession[] sessions)
    {
        var rows = new StackPanel { Spacing = 0 };
        void AddRow(Control control)
        {
            if (rows.Children.Count > 0)
                rows.Children.Add(new Border { Height = 1, Margin = new Thickness(20, 0), Background = TryBrush("StrokeDefaultBrush") });
            rows.Children.Add(control);
        }

        var nearby = !peer.Status.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
        AddRow(PreferenceRow(peer.Name,
            nearby ? Loc.Get("Available") : Loc.Get("Not nearby. Keep both computers awake and on the same local network."),
            null, "continuity"));
        if (_continuitySendPath is { } path)
            AddRow(PreferenceRow(Loc.Get("File sharing"), Path.GetFileName(path),
                ContinuityButton("Send file", () => new("send", peer.Id, path), true), "file-download"));

        var linked = sessions.Any(s => s.PeerId == peer.Id && s.Kind == "input" && s.Outgoing && s.State is not ("Failed" or "Declined"));
        var edgeSide = new ComboBox
        {
            ItemsSource = new[] { Loc.Get("On my left"), Loc.Get("On my right"), Loc.Get("Above me"), Loc.Get("Below me") },
            SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "left", "right", "top", "bottom" }, peer.InputEdge)),
            MinWidth = 140, IsEnabled = !linked
        };
        Avalonia.Automation.AutomationProperties.SetName(edgeSide, Loc.Get("Screen position"));
        AddRow(PreferenceRow(Loc.Get("Screen position"), Loc.Get("Where is this computer relative to yours?"), edgeSide, "monitor"));
        var link = new Button { Content = Loc.Get(linked ? "Linked" : "Link keyboard and mouse"), IsEnabled = !linked, Classes = { "Subtle", "Small" } };
        link.Click += async (_, _) =>
        {
            var sides = new[] { "left", "right", "top", "bottom" };
            link.IsEnabled = false;
            try { await ContinuityActionAsync(new("edge", peer.Id, sides[Math.Clamp(edgeSide.SelectedIndex, 0, 3)])); }
            finally { link.IsEnabled = !linked; }
        };
        AddRow(PreferenceRow(Loc.Get("Keyboard and mouse"),
            Loc.Get("Move the pointer across the selected screen edge."), link, "keyboard"));
        AddRow(PreferenceRow(Loc.Get("Audio playback"), Loc.Format("Use {0} as speakers for this device.", peer.Name),
            ContinuityButton("Use as audio output", () => new("live", peer.Id, "audio")), "speaker-high"));
        var view = new Button { Content = Loc.Get("View screen"), Classes = { "Subtle", "Small" } };
        view.Click += async (_, _) => await OpenContinuityViewerAsync(peer, false);
        AddRow(PreferenceRow(Loc.Get("Screen viewing"), Loc.Get("See the other computer’s screen in a window."), view, "monitor"));

        var permissions = new StackPanel { Spacing = 0, IsVisible = _continuityExpandedAccess.Contains(peer.Id) };
        void AddPermission(string label, string description, string feature, bool enabled)
        {
            permissions.Children.Add(new Border { Height = 1, Margin = new Thickness(20, 0), Background = TryBrush("StrokeDefaultBrush") });
            permissions.Children.Add(ContinuityPermissionRow(label, description, enabled,
                allowed => _ = ContinuityActionAsync(new("permission", peer.Id, feature, allowed))));
        }
        AddPermission("Text clipboard", "Copy on one device and paste on the other.", "clipboard", peer.Clipboard);
        AddPermission("File sharing", "Send files from the Files app.", "files", peer.Files);
        AddPermission("Keyboard and mouse", "Control this computer after local approval.", "input", peer.Input);
        AddPermission("App handoff", "Continue a Notes text activity on this computer.", "handoff", peer.Handoff);
        AddPermission("Screen viewing", "Share a selected screen after local approval.", "screen", peer.Screen);
        AddPermission("Audio playback", "Play the other computer’s audio on this computer.", "audio", peer.Audio);
        permissions.Children.Add(PreferenceRow(Loc.Get("Forget device"), Loc.Get("Pair again to use Continuity with this computer."),
            ContinuityButton("Forget", () => new("forget", peer.Id)), "continuity"));

        Button? access = null;
        access = SettingsAction("Access to this computer", "Choose what this paired computer can share with you.", "shield-check", () =>
        {
            permissions.IsVisible = !permissions.IsVisible;
            if (permissions.IsVisible) _continuityExpandedAccess.Add(peer.Id);
            else _continuityExpandedAccess.Remove(peer.Id);
            Avalonia.Automation.AutomationProperties.SetHelpText(access!, Loc.Get(permissions.IsVisible ? "Expanded" : "Collapsed"));
            if (access!.Content is Grid grid && grid.Children.OfType<DaisyIcon>().LastOrDefault() is { } arrow)
                arrow.Icon = permissions.IsVisible ? "caret-down" : "caret-right";
        });
        Avalonia.Automation.AutomationProperties.SetName(access, Loc.Get("Access to this computer"));
        if (permissions.IsVisible && access.Content is Grid accessGrid)
            accessGrid.Children.OfType<DaisyIcon>().Last().Icon = "caret-down";
        AddRow(access);
        rows.Children.Add(permissions);
        return new Border { Child = rows, Tag = peer.Name };
    }

    private async Task OpenContinuityViewerAsync(ContinuityDevice peer, bool control)
    {
        try
        {
            var status = await _continuity.ExecuteAsync(new("live", peer.Id, "screen", control));
            var session = status.Sessions?.LastOrDefault(s => s.PeerId == peer.Id && s.Kind == "screen" && s.Outgoing);
            if (session is null) throw new IOException();
            new DaisyOS.Shell.Views.Continuity.ContinuityViewer(session).Show();
            UpdateContinuityLive(status);
        }
        catch { (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t start screen viewing. Check the device permissions.")); }
    }

    private Control ContinuityPermissionRow(string label, string description, bool enabled, Action<bool> changed)
    {
        var toggle = new ToggleSwitch
        {
            IsChecked = enabled,
            OnContent = string.Empty,
            OffContent = string.Empty,
            VerticalAlignment = VerticalAlignment.Center
        };
        toggle.Click += (_, _) => changed(toggle.IsChecked == true);
        Avalonia.Automation.AutomationProperties.SetName(toggle, Loc.Get(label));
        return PreferenceRow(Loc.Get(label), Loc.Get(description), toggle);
    }

    private Border CreateTransferRow(ContinuityTransfer transfer)
    {
        var label = new TextBlock { Text = TransferLabel(transfer), Classes = { "SettingsDescription" } };
        var progress = new ProgressBar { Minimum = 0, Maximum = Math.Max(1, transfer.Length), Value = transfer.Completed, MinWidth = 180 };
        var controls = new StackPanel { Spacing = 8, Children = { label, progress } };
        if (transfer.Status is not ("Complete" or "Failed" or "Cancelled"))
            controls.Children.Add(ContinuityButton("Cancel", () => new("cancel", transfer.Id)));
        _continuityTransferControls[transfer.Id] = (label, progress);
        return PreferenceRow(transfer.Name, null, controls, "file-download");
    }

    private static string TransferLabel(ContinuityTransfer transfer)
    {
        var percent = transfer.Length <= 0 ? 0 : (int)Math.Clamp(transfer.Completed * 100 / transfer.Length, 0, 100);
        return $"{Loc.Get(transfer.Status)} · {percent}%";
    }
}
