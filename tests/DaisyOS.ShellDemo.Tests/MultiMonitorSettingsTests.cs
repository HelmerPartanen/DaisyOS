using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class MultiMonitorSettingsTests
{
    [AvaloniaFact]
    public void ArrangementEditsAreDraftsUseLogicalCoordinatesAndRejectOverlap()
    {
        var editor = new DisplayArrangementEditor(DisplaySettingsHelper.ParseAllDisplaysKScreenDoctorJson(TwoDisplays), "eDP-1");
        IReadOnlyList<DisplayPosition>? applied = null;
        editor.ApplyRequested += positions => { applied = positions; return Task.CompletedTask; };
        var window = new Window { Width = 800, Height = 420, Content = editor };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var x = editor.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "DisplayPositionX");
            var y = editor.GetVisualDescendants().OfType<NumericUpDown>().Single(n => n.Name == "DisplayPositionY");
            var apply = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyDisplayArrangement");
            x.Value = -1536; y.Value = -200;
            Assert.Null(applied);
            Assert.True(apply.IsEnabled);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(applied!, p => p.Name == "eDP-1" && p.X == 0 && p.Y == 0);
            Assert.Contains(applied!, p => p.Name == "HDMI-A-1" && p.X == 3072 && p.Y == 200);
            using var frame = window.CaptureRenderedFrame();
            frame!.Save("/tmp/daisy-display-arrangement.png");
            x.Value = 1536; y.Value = 0;
            Assert.False(apply.IsEnabled);
        }
        finally { window.Close(); }
    }

    internal const string TwoDisplays = """
    {"outputs":[
      {"id":1,"name":"HDMI-A-1","connected":true,"enabled":true,"priority":1,"scale":1,"pos":{"x":1536,"y":0},"currentModeId":"4k","replicationSource":0,
       "modes":[{"id":"4k","size":{"width":3840,"height":2160},"refreshRate":60},{"id":"4k-5994","size":{"width":3840,"height":2160},"refreshRate":59.94}]},
      {"id":2,"name":"eDP-1","connected":true,"enabled":true,"priority":2,"scale":1.25,"pos":{"x":0,"y":0},"currentModeId":"fhd","replicationSource":0,
       "modes":[{"id":"fhd","size":{"width":1920,"height":1080},"refreshRate":60}]}]}
    """;

    [AvaloniaFact]
    public async Task SwitchingOutputsReplacesThePageAndUsesEachOutputsActualScale()
    {
        var runner = new DisplayRunner();
        var view = new SettingsView { DisplayCommandRunner = runner };
        var owner = new Window { Width = 1100, Height = 800, Content = view };
        owner.Show();
        try
        {
            view.SelectCategory("Displays");
            await view.DisplayPageReady;
            for (var pass = 0; pass < 6; pass++)
            {
                var target = pass % 2 == 0 ? "eDP-1" : "HDMI-A-1";
                view.GetVisualDescendants().OfType<Button>().Single(b => b.Content is string s && s.StartsWith(target))
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                await view.DisplayPageReady;
                Dispatcher.UIThread.RunJobs();
                var pickers = view.GetVisualDescendants().OfType<ComboBox>().Where(p => p.IsEffectivelyVisible).ToArray();
                Assert.Single(pickers, p => p.Name == "DisplayLayoutMode");
                Assert.Contains(pickers, p => p.SelectedItem is ComboBoxItem item && Equals(item.Tag, target == "eDP-1" ? "125%" : "100%"));
                Assert.Equal(4, pickers.Length); // One mode selector and three per-output settings.
                if (target == "HDMI-A-1")
                    Assert.Contains(pickers, p => p.Items.OfType<ComboBoxItem>().Any(i => Equals(i.Tag, "59.94 Hz")) && p.Items.OfType<ComboBoxItem>().Any(i => Equals(i.Tag, "60 Hz")));
            }
            Assert.DoesNotContain(runner.Calls, call => call != "kscreen-doctor -j" && !call.StartsWith("busctl "));
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task SupersededDisplayQueryCannotAppendItsOldOutputControls()
    {
        var pending = new TaskCompletionSource<CommandResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var runner = new DisplayRunner { Next = pending.Task };
        var view = new SettingsView { DisplayCommandRunner = runner };
        var owner = new Window { Content = view };
        owner.Show();
        try
        {
            view.SelectCategory("Displays");
            var old = view.DisplayPageReady;
            view.SelectCategory("About");
            pending.SetResult(CommandResult.Success(TwoDisplays)); // Simulate a backend ignoring cancellation.
            await old;
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ComboBox>(), p => p.Name == "DisplayLayoutMode");
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task SelectionSurvivesEnumerationReorderingAndUnplugFallsBackToRemainingOutput()
    {
        var runner = new DisplayRunner();
        var view = new SettingsView { DisplayCommandRunner = runner };
        var owner = new Window { Content = view };
        owner.Show();
        try
        {
            view.SelectCategory("Displays"); await view.DisplayPageReady;
            view.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, "eDP-1"))
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            await view.DisplayPageReady;
            using var doc = global::System.Text.Json.JsonDocument.Parse(TwoDisplays);
            var outputs = doc.RootElement.GetProperty("outputs").EnumerateArray().Select(o => o.GetRawText()).ToArray();
            runner.Json = "{\"outputs\":[" + string.Join(",", outputs.Reverse()) + "]}";
            view.SelectCategory("Displays"); await view.DisplayPageReady;
            Assert.Contains(view.GetVisualDescendants().OfType<ComboBox>(), p => p.SelectedItem is ComboBoxItem item && Equals(item.Tag, "125%"));
            runner.Json = "{\"outputs\":[" + outputs[0] + "]}";
            view.SelectCategory("Displays"); await view.DisplayPageReady;
            Assert.Contains(view.GetVisualDescendants().OfType<ComboBox>(), p => p.SelectedItem is ComboBoxItem item && Equals(item.Tag, "100%"));
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<ComboBox>(), p => p.Name == "DisplayLayoutMode");
        }
        finally { owner.Close(); }
    }

    internal sealed class DisplayRunner : ICommandRunner
    {
        public string Json = TwoDisplays;
        public Task<CommandResult>? Next;
        public readonly List<string> Calls = [];
        public Task<CommandResult> RunAsync(string fileName, IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken = default)
        {
            Calls.Add(fileName + " " + string.Join(" ", arguments));
            if (fileName != "kscreen-doctor") return Task.FromResult(CommandResult.Success("b false"));
            var next = Next; Next = null;
            return next ?? Task.FromResult(CommandResult.Success(Json));
        }
    }
}
