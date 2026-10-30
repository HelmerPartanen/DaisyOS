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
    private StackPanel? _continuityPreferences;
    private string? _continuityStructure;
    private readonly Dictionary<string, (TextBlock Label, ProgressBar Progress)> _continuityTransferControls = new();
    private readonly Dictionary<string, string> _continuityPairEntries = new();
    private string? _continuitySendPath;
    private string? _continuitySelectedPeer;
    private ContinuityStatus? _continuityLastState;

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

        var preferences = new List<Control>
        {
            CreateContinuityPowerRow(state.Enabled)
        };
        if (state.Enabled)
            preferences.Add(CreateContinuityNameRow(state.Name));
        _continuityPreferences = CreateSection(Loc.Get("CONTINUITY"), Loc.Get("Connect and share with nearby DaisyOS devices."), preferences.ToArray());
        CategoryContent.Children.Add(_continuityPreferences);

        _continuityStructure = null;
        _continuityTransferControls.Clear();
        _continuityLive = new StackPanel { Spacing = 24 };
        CategoryContent.Children.Add(_continuityLive);
        UpdateContinuityLive(state);
    }

    private Control CreateContinuityPowerRow(bool enabled)
    {
        var button = new Button { Content = Loc.Get(enabled ? "Turn off" : "Turn on"), Classes = { "Subtle", "Small" } };
        button.Click += async (_, _) => await SetContinuityEnabledAsync(!enabled);
        return PreferenceRow(Loc.Get("Continuity"), Loc.Get("Paired devices share clipboard, files, keyboard, mouse, screen and audio automatically."), button, "continuity");
    }

    private async Task AddContinuityNameToAboutAsync(int version)
    {
        ContinuityStatus state;
        try { state = await _continuity.ExecuteAsync(new("status")); }
        catch { return; }
        if (version != _categoryBuildVersion || _activeCategory != "About") return;

        AddSection(Loc.Get("DEVICE IDENTITY"), Loc.Get("The name nearby devices see in Continuity."),
            CreateContinuityNameRow(state.Name));
    }

    private Border CreateContinuityNameRow(string deviceName)
    {
        var name = new SearchBar
        {
            Text = deviceName, PlaceholderText = Loc.Get("Device name"),
            ShowSearchIcon = false, ShowClearButton = false,
            MinWidth = 200, MaxWidth = 320, HorizontalAlignment = HorizontalAlignment.Right
        };
        var savedName = deviceName;
        var saving = false;
        async Task SaveAsync()
        {
            if (saving) return;
            var requested = name.Text?.Trim();
            if (string.IsNullOrWhiteSpace(requested) || requested == savedName)
            {
                name.Text = savedName;
                return;
            }
            saving = true;
            name.IsEnabled = false;
            try
            {
                var updated = await _continuity.ExecuteAsync(new("name", Value: requested));
                savedName = updated.Name;
                name.Text = savedName;
            }
            catch
            {
                name.Text = savedName;
                (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t change the Continuity name."));
            }
            finally { name.IsEnabled = true; saving = false; }
        }
        name.LostFocus += async (_, _) => await SaveAsync();
        name.KeyDown += async (_, args) =>
        {
            if (args.Key != Key.Enter) return;
            args.Handled = true;
            await SaveAsync();
        };
        return PreferenceRow(Loc.Get("Device name"), Loc.Get("Shown to nearby devices. Changes save automatically."), name, "continuity");
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
        _continuityLastState = state;
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
        if (!state.Enabled)
        {
            if (_continuityPreferences is not null) _continuityPreferences.IsVisible = true;
            UpdateContinuityHeader(null);
            return;
        }

        if (state.Diagnostic.Contains("Pairing code", StringComparison.Ordinal))
            _continuityLive.Children.Add(CreateSection(Loc.Get("PAIRING"), null, InfoRow(Loc.Get("Couldn’t pair devices"), Loc.Get(state.Diagnostic))));

        foreach (var expired in _continuityPairEntries.Keys.Where(id => !state.Prompts.Any(p => p.Id == id)).ToArray())
            _continuityPairEntries.Remove(expired);
        foreach (var prompt in state.Prompts)
        {
            if (prompt.Kind == "pair-display")
            {
                _continuityLive.Children.Add(CreateSection(Loc.Get("PAIRING CODE"),
                    Loc.Format("Enter this code on {0} to pair. Pairing enables all Continuity features.", prompt.Name),
                    InfoRow(prompt.Detail, Loc.Get("This code expires in two minutes."))));
            }
            else if (prompt.Kind == "pair-code")
            {
                var code = new TextBox { PlaceholderText = Loc.Get("6-digit code"), MaxLength = 6, Width = 160, Text = _continuityPairEntries.GetValueOrDefault(prompt.Id, "") };
                Avalonia.Automation.AutomationProperties.SetName(code, Loc.Get("Pairing code"));
                var pair = ContinuityButton("Pair", () => new("pair-code", prompt.Id, code.Text?.Trim() ?? ""), true);
                pair.IsEnabled = code.Text is { Length: 6 } initial && initial.All(char.IsAsciiDigit);
                code.TextChanged += (_, _) =>
                {
                    _continuityPairEntries[prompt.Id] = code.Text ?? "";
                    pair.IsEnabled = code.Text is { Length: 6 } value && value.All(char.IsAsciiDigit);
                };
                code.KeyDown += (_, e) => { if (e.Key == Key.Enter && pair.IsEnabled) { _ = ContinuityActionAsync(new("pair-code", prompt.Id, code.Text!)); e.Handled = true; } };
                _continuityLive.Children.Add(CreateSection(Loc.Get("PAIR DEVICE"),
                    Loc.Format("Enter the code shown on {0}. Pairing enables all Continuity features.", prompt.Name),
                    PreferenceRow(prompt.Name, null, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { code, pair } }, "continuity")));
            }
        }

        foreach (var session in (state.Sessions ?? []).Where(s => s.Kind != "input"))
        {
            var description = Loc.Get(session.Kind == "audio" ? "Audio playback" : session.Kind == "input" ? "Keyboard and mouse" : session.Control ? "Remote control" : "Screen viewing") + " · " + Loc.Get(session.State);
            if (session.Detail.Length > 0) description += "\n" + Loc.Get(session.Detail);
            _continuityLive.Children.Add(CreateSection(Loc.Get("CONTINUITY"), null,
                PreferenceRow(session.Name, description,
                    session.State is "Failed" or "Declined" ? new TextBlock() : ContinuityButton("Disconnect", () => new("live-stop", session.Id)), "continuity")));
        }

        if (_continuitySelectedPeer is { } selectedId &&
            state.Devices.FirstOrDefault(device => device.Trusted && device.Id == selectedId) is { } selectedPeer)
        {
            if (_continuityPreferences is not null) _continuityPreferences.IsVisible = false;
            UpdateContinuityHeader(selectedPeer);
            BuildContinuityPeerDetails(selectedPeer, state.Sessions ?? []);
            return;
        }
        if (_continuityPreferences is not null) _continuityPreferences.IsVisible = true;
        _continuitySelectedPeer = null;
        UpdateContinuityHeader(null);

        var paired = state.Devices.Where(device => device.Trusted).ToArray();
        if (paired.Length > 0)
        {
            _continuityLive.Children.Add(CreateSection(Loc.Get("MY DEVICES"), Loc.Get("Trusted devices reconnect automatically when they are nearby."),
                paired.Select(peer => CreateTrustedDeviceRow(peer, state)).ToArray()));
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

    private void ShowContinuityPeer(string? peerId)
    {
        _continuitySelectedPeer = peerId;
        _continuityStructure = null;
        if (_continuityLastState is { } state) UpdateContinuityLive(state);
    }

    private Border CreateTrustedDeviceRow(ContinuityDevice peer, ContinuityStatus state)
    {
        var nearby = !peer.Status.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
        var details = new Button { Content = Loc.Get("Details"), Classes = { "Subtle", "Small" } };
        details.Click += (_, _) =>
        {
            _continuitySelectedPeer = peer.Id;
            _continuityStructure = null;
            UpdateContinuityLive(state);
        };
        var row = PreferenceRow(peer.Name, Loc.Get(nearby ? "Available" : "Not nearby"), details, "continuity");
        row.Tag = peer.Id;
        return row;
    }

    private void UpdateContinuityHeader(ContinuityDevice? peer)
    {
        if (CategoryContent.Children.FirstOrDefault() is not StackPanel header) return;
        header.Children.Clear();
        if (peer is null)
        {
            header.Children.Add(new TextBlock { Text = Loc.Get("Continuity"), Classes = { "SettingsPageTitle" } });
            header.Children.Add(Secondary(Loc.Get("Use your keyboard, mouse and speakers across nearby DaisyOS computers.")));
            return;
        }

        Button Crumb(string title, string accessibleName, Action action)
        {
            var text = new TextBlock { Text = title, Classes = { "SettingsPageTitle" } };
            text.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
            var button = new Button { Content = text, Classes = { "BreadcrumbItem" } };
            Avalonia.Automation.AutomationProperties.SetName(button, accessibleName);
            button.Click += (_, _) => action();
            return button;
        }
        DaisyIcon Separator()
        {
            var icon = new DaisyIcon { Icon = "caret-right", Size = 18, VerticalAlignment = VerticalAlignment.Center };
            icon.Bind(DaisyIcon.ForegroundProperty, this.GetResourceObservable("ContentTertiaryBrush"));
            return icon;
        }
        var breadcrumbs = new WrapPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        breadcrumbs.Children.Add(Crumb(Loc.Get("Continuity"), Loc.Get("Back to Continuity"), () => ShowContinuityPeer(null)));
        breadcrumbs.Children.Add(Separator());
        breadcrumbs.Children.Add(new TextBlock { Text = peer.Name, Classes = { "SettingsPageTitle" }, VerticalAlignment = VerticalAlignment.Center });
        header.Children.Add(breadcrumbs);
        header.Children.Add(Secondary(Loc.Get("Use Continuity with this computer.")));
    }

    private void BuildContinuityPeerDetails(ContinuityDevice peer, ContinuityLiveSession[] sessions)
    {
        var nearby = !peer.Status.Contains("unreachable", StringComparison.OrdinalIgnoreCase);
        var status = nearby ? Loc.Get("Available") : Loc.Get("Not nearby");
        _continuityLive!.Children.Add(CreateSection(Loc.Get("CONNECTION"), null,
            PreferenceRow(peer.Name, status, null, "continuity")));

        var edgeSide = new ComboBox
        {
            ItemsSource = new[] { Loc.Get("On my left"), Loc.Get("On my right"), Loc.Get("Above me"), Loc.Get("Below me") },
            SelectedIndex = Math.Max(0, Array.IndexOf(new[] { "left", "right", "top", "bottom" }, peer.InputEdge)),
            MinWidth = 140
        };
        Avalonia.Automation.AutomationProperties.SetName(edgeSide, Loc.Get("Screen position"));
        edgeSide.SelectionChanged += async (_, _) =>
        {
            var sides = new[] { "left", "right", "top", "bottom" };
            if (edgeSide.SelectedIndex >= 0 && sides[edgeSide.SelectedIndex] != peer.InputEdge)
                await ContinuityActionAsync(new("edge", peer.Id, sides[edgeSide.SelectedIndex]));
        };
        var actions = new List<Control>
        {
            PreferenceRow(Loc.Get("Keyboard and mouse"), Loc.Get("Move your pointer between screens."), edgeSide, "keyboard"),
            PreferenceRow(Loc.Get("Audio playback"), Loc.Get("Play audio through this computer."),
                ContinuityButton("Use as output", () => new("live", peer.Id, "audio")), "speaker-high")
        };
        var view = new Button { Content = Loc.Get("View"), Classes = { "Subtle", "Small" } };
        view.Click += async (_, _) => await OpenContinuityViewerAsync(peer, false);
        actions.Add(PreferenceRow(Loc.Get("Screen"), Loc.Get("View this computer’s screen."), view, "monitor"));
        if (_continuitySendPath is { } path)
            actions.Add(PreferenceRow(Loc.Get("Send file"), Path.GetFileName(path),
                ContinuityButton("Send", () => new("send", peer.Id, path), true), "file-download"));
        _continuityLive.Children.Add(CreateSection(Loc.Get("USE WITH THIS COMPUTER"),
            Loc.Get("All Continuity features are enabled. Choose where the other screen is and move your pointer between screens."), actions.ToArray()));
        _continuityLive.Children.Add(CreateSection(Loc.Get("REMOVE DEVICE"), null,
            PreferenceRow(Loc.Get("Forget device"), Loc.Get("Disconnect both computers. Pair again to use Continuity."),
                ContinuityButton("Forget", () => new("forget", peer.Id)), "continuity")));
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
        catch { (Application.Current as App)?.Feedback.Show(Loc.Get("Couldn’t start screen viewing. Make sure the other computer is awake and connected.")); }
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
