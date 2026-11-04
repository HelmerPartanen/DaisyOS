using Avalonia.Controls;
using Avalonia.Layout;
using DaisyOS.Shell.Services.Localization;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private IReadOnlyList<DetectedDisplay>? _lastExtendedDisplayLayout;
    private async Task RefreshDisplayTopologyAsync()
    {
        if (_activeCategory != "Displays" || _displayRefreshRunning || _displayChangeGuard.IsBusy ||
            !DisplayPageReady.IsCompleted || _displayLifetime.IsCancellationRequested) return;
        _displayRefreshRunning = true;
        var version = _categoryBuildVersion;
        try
        {
            var displays = await DisplaySettingsHelper.DetectAllDisplaysAsync(DisplayCommandRunner, _displayLifetime.Token);
            if (version == _categoryBuildVersion && !_displayChangeGuard.IsBusy &&
                DisplaySettingsHelper.ConfigurationKey(displays) != _displayConfigurationKey)
                ShowCategory("Displays");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { global::System.Console.Error.WriteLine($"[display-settings] Refresh failed: {ex.Message}"); }
        finally { _displayRefreshRunning = false; }
    }

    private Control BuildDisplayArrangement(IReadOnlyList<DetectedDisplay> displays, DetectedDisplay selected, int version)
    {
        var mirror = displays.Any(d => d.MirrorSource is not null);
        if (!mirror) _lastExtendedDisplayLayout = displays;
        var modes = new[] { Loc.Get("Extend displays"), Loc.Get("Mirror displays") };
        var picker = new ComboBox { Name = "DisplayLayoutMode", ItemsSource = modes, SelectedIndex = mirror ? 1 : 0,
            MinWidth = 200, HorizontalAlignment = HorizontalAlignment.Right };
        picker.SelectionChanged += async (_, _) =>
        {
            if (picker.SelectedIndex < 0 || picker.SelectedIndex == (mirror ? 1 : 0)) return;
            var requestedMirror = picker.SelectedIndex == 1;
            await ApplyDisplayLayoutAsync(current => !requestedMirror && _lastExtendedDisplayLayout is { } extended &&
                extended.Select(d => d.Name).Order().SequenceEqual(current.Select(d => d.Name).Order())
                    ? DisplaySettingsHelper.RestoreArguments(extended, current)
                    : DisplaySettingsHelper.LayoutArguments(current, requestedMirror), version);
        };
        var panel = new StackPanel { Spacing = 12 };
        panel.Children.Add(PreferenceRow(Loc.Get("Display mode"), null, picker));
        if (!mirror)
        {
            var editor = new DisplayArrangementEditor(displays, selected.Name);
            editor.ApplyRequested += async positions => await ApplyDisplayLayoutAsync(current =>
            {
                // A hotplug while editing invalidates the draft instead of moving another screen.
                if (!current.Select(d => d.Name).Order().SequenceEqual(displays.Select(d => d.Name).Order()))
                    return Array.Empty<string>();
                return positions.Select(p => $"output.{p.Name}.position.{p.X},{p.Y}").ToArray();
            }, version);
            panel.Children.Add(editor);
        }
        return panel;
    }

    private async Task ApplyDisplayLayoutAsync(Func<IReadOnlyList<DetectedDisplay>, IReadOnlyList<string>> arguments, int version)
    {
        IReadOnlyList<DetectedDisplay>? previous = null;
        await RunDisplayChangeAsync(async () =>
        {
            previous = await DisplaySettingsHelper.DetectAllDisplaysAsync(DisplayCommandRunner);
            var commands = arguments(previous);
            return commands.Count > 0 && (await DisplayCommandRunner.RunAsync("kscreen-doctor", commands,
                TimeSpan.FromSeconds(5))).Succeeded;
        }, () => previous is null ? Task.FromResult(true) :
            DisplaySettingsHelper.RestoreConfigurationAsync(previous, DisplayCommandRunner), version);
    }
}
