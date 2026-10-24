using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.Compositor;
using DaisyOS.Shell.Views.Components.Taskbar;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class TaskbarPreviewTests
{
    [AvaloniaFact]
    public void SystemAppIndicatorsRecoverFromStartupAndReopening()
    {
        var view = new TaskbarView();
        var window = new Window { Content = view, Width = 800, Height = 300 };
        window.Show();
        try
        {
            view.SetAppWindowState("notes", DaisyOS.Shell.Services.Windows.NativeAppWindowState.NotRunning);
            view.SetCompositorWindows([Note("first")]);
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "notes"));
            var indicator = button.GetVisualDescendants().OfType<Border>().First(b => b.Classes.Contains("TaskbarWindowIndicator"));
            Assert.Contains("WindowRunning", indicator.Classes);
            view.SetCompositorWindows([Note("first") with { IsMinimized = true }]);
            Assert.Contains("WindowMinimized", indicator.Classes);
            view.SetAppWindowState("notes", DaisyOS.Shell.Services.Windows.NativeAppWindowState.NotRunning);
            view.SetCompositorWindows([Note("first")]);
            Assert.DoesNotContain("WindowRunning", indicator.Classes);
            view.SetCompositorWindows([Note("reopened") with { IsActive = true }]);
            Assert.Contains("WindowActive", indicator.Classes);
            Assert.Contains("WindowRunning", indicator.Classes);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GroupedTilesExcludeFrontAndReserveTheirOwnSpace(bool dark, bool active)
    {
        var view = new TaskbarView();
        var window = new Window { Content = view, Width = 800, Height = 180,
            RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        try
        {
            view.SetCompositorWindows([Note("one") with { IsActive = active }, Note("two"), Note("three")]);
            view.SetAppWindowState("notes", active ? DaisyOS.Shell.Services.Windows.NativeAppWindowState.Active
                : DaisyOS.Shell.Services.Windows.NativeAppWindowState.RunningInactive);
            window.UpdateLayout();
            await Task.Delay(250);
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "notes"));
            var rails = button.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.TaskbarStackedWindowBorder>().ToArray();
            Assert.Single(rails);
            var presenter = button.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>()
                .First(p => p.Name == "PART_ContentPresenter");
            foreach (var rail in rails)
            {
                Assert.Equal(new Size(40, 40), rail.Bounds.Size);
                Assert.False(rail.Clip!.FillContains(new Point(20, 20)));
                Assert.False(rail.Clip.FillContains(new Point(35, 20)));
                Assert.True(rail.Clip.FillContains(new Point(38, 20)));
                Assert.Equal(presenter.Background, rail.Background);
            }
            var canvas = view.FindControl<Canvas>("AppIconsCanvas")!;
            var ordered = canvas.Children.OfType<Button>().OrderBy(Canvas.GetLeft).ToArray();
            var index = Array.IndexOf(ordered, button);
            Assert.True(Canvas.GetLeft(ordered[index + 1]) >= Canvas.GetLeft(button) + 50);
            window.CaptureRenderedFrame()!.Save($"/tmp/daisy-taskbar-stack-{dark}-{active}.png");
            view.SetCompositorWindows([Note("one")]);
            window.UpdateLayout();
            Assert.All(rails, rail => Assert.False(rail.IsVisible));
        }
        finally { window.Close(); }
    }

    private static readonly Type State = typeof(KWinWindowBridge).Assembly.GetType("DaisyOS.Shell.Services.Compositor.TaskbarPreviewState")!;
    private static string Read(bool showing = false) => (string)State.GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)!.Invoke(null, [showing])!;
    private static CompositorWindowRecord Note(string id, bool current = true) =>
        new(id, "org.daisyos.notes", "Note " + id, "org.daisyos.notes", null, null, 1, false, false, false, false, true,
            DaisyAppId: "notes", IsOnCurrentWorkspace: current);

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task HoverWaitsThenPublishesGroupedWindowsAndLeavingClearsRequest(bool nativePanel)
    {
        var view = new TaskbarView();
        typeof(TaskbarView).GetProperty("IsNativePanelHosted", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(view, nativePanel);
        var window = new Window { Content = view, Width = 800, Height = 300 };
        window.Show();
        try
        {
            Read(); // A native renderer has completed its handshake.
            view.SetCompositorWindows([Note("one"), Note("two"), Note("other-desktop", false)]);
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "notes"));
            var point = button.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(point);
            Assert.DoesNotContain("windowIds", Read());
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            using var request = JsonDocument.Parse(Read());
            Assert.Equal(new[] { "one", "two" }, request.RootElement.GetProperty("windowIds").EnumerateArray().Select(v => v.GetString()));
            Assert.InRange(request.RootElement.GetProperty("anchorX").GetDouble(), 0, 1);
            Assert.InRange(request.RootElement.GetProperty("anchorY").GetDouble(), 0, 1);
            Assert.Equal(nativePanel ? window.Bounds.Height : 0, request.RootElement.GetProperty("panelHeight").GetDouble());
            Assert.False(ToolTip.GetIsOpen(button));
            window.MouseMove(new Point(4, 4));
            Assert.DoesNotContain("windowIds", Read());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task LeavingBeforeDelayAndWindowRemovalPreventStalePreviews()
    {
        var view = new TaskbarView();
        var window = new Window { Content = view, Width = 800, Height = 300 };
        window.Show();
        try
        {
            Read();
            view.SetCompositorWindows([Note("one")]);
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "notes"));
            var point = button.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(point);
            window.MouseMove(new Point(4, 4));
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("windowIds", Read());
            window.MouseMove(point);
            view.SetCompositorWindows([]);
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            Assert.DoesNotContain("windowIds", Read());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ClickCancelsPendingPreviewAndKeepsNormalAppActivation()
    {
        var view = new TaskbarView();
        var window = new Window { Content = view, Width = 800, Height = 300 };
        var clicks = 0;
        view.AppIconClicked += (_, _) => clicks++;
        window.Show();
        try
        {
            Read();
            view.SetCompositorWindows([Note("one")]);
            Dispatcher.UIThread.RunJobs();
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "notes"));
            var point = button.TranslatePoint(new Point(20, 20), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            await Task.Delay(450);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1, clicks);
            Assert.DoesNotContain("windowIds", Read());
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void PointerPressPreservesFocusedAppStateUntilTaskbarActivation()
    {
        var view = new TaskbarView();
        var window = new Window { Content = view, Width = 800, Height = 300 };
        bool? wasActive = null;
        view.AppIconClicked += (_, appId) => wasActive = view.TakeWasActiveAtActivation(appId);
        window.Show();
        try
        {
            view.SetCompositorWindows([Note("focused") with { IsActive = true }]);
            window.UpdateLayout();
            var button = view.GetVisualDescendants().OfType<Button>().First(b => Equals(b.Tag, "notes"));
            var point = button.TranslatePoint(new Point(20, 20), window)!.Value;

            window.MouseDown(point, MouseButton.Left);
            // Simulate the compositor publishing focus loss after the layer
            // surface receives the press but before Avalonia raises Click.
            view.SetCompositorWindows([Note("focused") with { IsActive = false }]);
            window.MouseUp(point, MouseButton.Left);

            Assert.True(wasActive);
        }
        finally { window.Close(); }
    }
}
