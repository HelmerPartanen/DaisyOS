using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.Raw;
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
    [AvaloniaTheory]
    [InlineData(false, 1d)]
    [InlineData(true, 1.5d)]
    public void DraggingScreensSnapsTheirEdgesAndOnlyAppliesOnConfirmation(bool dark, double scaling)
    {
        var editor = new DisplayArrangementEditor(DisplaySettingsHelper.ParseAllDisplaysKScreenDoctorJson(TwoDisplays), "eDP-1");
        IReadOnlyList<DisplayPosition>? applied = null;
        editor.ApplyRequested += positions => { applied = positions; return Task.CompletedTask; };
        var window = new Window { Width = 800, Height = 420, Content = editor,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        window.SetRenderScaling(scaling);
        try
        {
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Empty(editor.GetVisualDescendants().OfType<NumericUpDown>());
            var apply = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyDisplayArrangement");
            Assert.False(apply.IsEnabled);
            var small = editor.GetVisualDescendants().OfType<Border>().Single(b => b.Tag as string == "eDP-1");
            var large = editor.GetVisualDescendants().OfType<Border>().Single(b => b.Tag as string == "HDMI-A-1");
            var original = editor.NormalizedPositions().ToArray();
            var start = small.TranslatePoint(new Point(small.Bounds.Width / 2, small.Bounds.Height / 2), window)!.Value;
            // Move the laptop to the right of the monitor, stopping within the snap radius.
            var target = large.TranslatePoint(new Point(large.Bounds.Width + small.Bounds.Width / 2 - 5, small.Bounds.Height / 2), window)!.Value;
            window.MouseDown(start, Avalonia.Input.MouseButton.Left);
            window.MouseMove(target);
            window.MouseUp(target, Avalonia.Input.MouseButton.Left);
            window.UpdateLayout();
            Assert.Null(applied);
            Assert.True(apply.IsEnabled);
            var draft = editor.NormalizedPositions();
            var laptop = draft.Single(p => p.Name == "eDP-1");
            var monitor = draft.Single(p => p.Name == "HDMI-A-1");
            Assert.Equal(monitor.X + 3840, laptop.X);
            Assert.Equal(monitor.Y, laptop.Y);
            apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(draft, applied);
            editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResetDisplayArrangement")
                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(original, editor.NormalizedPositions());
            Assert.False(apply.IsEnabled);
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-display-arrangement-{dark}.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task BrightnessUsesTheSharedSliderAndCancelsPendingWritesWhenLeavingDisplays()
    {
        var runner = new DisplayRunner { Json = TwoDisplays.Replace("\"priority\":1,", "\"priority\":1,\"brightness\":65,") };
        var view = new SettingsView { DisplayCommandRunner = runner };
        var owner = new Window { Width = 1100, Height = 800, Content = view };
        owner.Show();
        try
        {
            view.SelectCategory("Displays");
            await view.DisplayPageReady;
            var slider = view.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.QuickSettingsSlider>()
                .Single(control => control.Name == "DisplayBrightnessSlider");
            Assert.Equal(65, slider.Value);
            Assert.Empty(view.GetVisualDescendants().OfType<Slider>());
            slider.Value = 70;
            slider.Value = 75;
            slider.Value = 80;
            Assert.DoesNotContain(runner.Calls, call => call.Contains(".brightness."));
            for (var i = 0; i < 100 && !runner.Calls.Any(call => call.Contains(".brightness.")); i++) await Task.Delay(10);
            Assert.Equal("kscreen-doctor output.HDMI-A-1.brightness.80", Assert.Single(runner.Calls.Where(call => call.Contains(".brightness."))));
            slider.Value = 90;
            view.SelectCategory("Password");
            await Task.Delay(350);
            Assert.Single(runner.Calls.Where(call => call.Contains(".brightness.")));
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public void KeyboardAdjustmentsCannotApplyOverlappingScreensAndResetRestoresTheDraft()
    {
        var editor = new DisplayArrangementEditor(DisplaySettingsHelper.ParseAllDisplaysKScreenDoctorJson(TwoDisplays), "eDP-1");
        var window = new Window { Width = 800, Height = 420, Content = editor };
        window.Show();
        try
        {
            window.UpdateLayout();
            var original = editor.NormalizedPositions().ToArray();
            var small = editor.GetVisualDescendants().OfType<Border>().Single(b => b.Tag as string == "eDP-1");
            small.Focus();
            window.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            window.KeyReleaseQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
            Assert.NotEqual(original, editor.NormalizedPositions());
            var apply = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ApplyDisplayArrangement");
            var reset = editor.GetVisualDescendants().OfType<Button>().Single(b => b.Name == "ResetDisplayArrangement");
            Assert.False(apply.IsEnabled);
            Assert.True(reset.IsEnabled);
            reset.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(original, editor.NormalizedPositions());
            Assert.False(apply.IsEnabled);
            Assert.False(reset.IsEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DisplayAndColorSettingsHaveSeparateInsetGroupsOnTheSamePage()
    {
        var view = new SettingsView { DisplayCommandRunner = new DisplayRunner() };
        var owner = new Window { Width = 1000, Height = 800, Content = view };
        owner.Show();
        try
        {
            view.SelectCategory("Displays");
            await view.DisplayPageReady;
            var groups = view.GetVisualDescendants().OfType<Border>()
                .Where(border => border.Classes.Contains("SettingsGroupCard")).ToArray();
            var geometry = Assert.Single(groups.Where(group => group.GetVisualDescendants().OfType<Control>().Any(row => row.Tag as string == "Resolution")));
            var color = Assert.Single(groups.Where(group => group.GetVisualDescendants().OfType<Control>().Any(row => row.Tag as string == "Use night light")));
            Assert.NotSame(geometry, color);
            Assert.Contains(color.GetVisualDescendants().OfType<Control>(), row => row.Tag as string == "Color profile");
            Assert.Contains(color.GetVisualDescendants().OfType<Control>(), row => row.Tag as string == "Color accuracy");
            Assert.DoesNotContain(geometry.GetVisualDescendants().OfType<Control>(), row => row.Tag as string == "Use night light");
            owner.CaptureRenderedFrame()?.Save("/tmp/daisy-display-color-groups.png");
        }
        finally { owner.Close(); }
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
                Assert.Equal(7, pickers.Length); // Layout, orientation, resolution, scale, refresh rate, profile, and color accuracy.
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
