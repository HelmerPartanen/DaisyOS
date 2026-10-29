using Avalonia;
using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Services.Localization;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ContinuityFlowTests
{
    [AvaloniaFact]
    public void PairedComputersOfferActionsWithoutSeparateConnectOrSenderPermissionSteps()
    {
        var (window, row) = Render(false);
        try
        {
            var buttons = row.GetVisualDescendants().OfType<Button>().ToArray();
            Assert.Contains(buttons, b => Equals(b.Content, Loc.Get("Link")) && b.IsEnabled);
            Assert.Contains(buttons, b => Equals(b.Content, Loc.Get("Use as output")));
            Assert.Contains(buttons, b => Equals(b.Content, Loc.Get("View")));
            Assert.DoesNotContain(buttons, b => Equals(b.Content, Loc.Get("Connect")));
            Assert.Empty(row.GetVisualDescendants().OfType<ToggleSwitch>().Where(t => Avalonia.Automation.AutomationProperties.GetName(t) == Loc.Get("Text clipboard")));
            var access = Assert.Single(buttons, b => b.Classes.Contains("SettingsGroupAction"));
            access.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(6, row.GetVisualDescendants().OfType<ToggleSwitch>().Count(t => Avalonia.Automation.AutomationProperties.GetName(t) is not null));
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var back = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b =>
                Avalonia.Automation.AutomationProperties.GetName(b) == Loc.Format("Back to {0}", "Desktop"));
            back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Contains(row.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Loc.Get("Link")));

        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void ActiveLinkKeepsItsSavedSideAndCannotBeStartedTwice()
    {
        var (window, row) = Render(true);
        try
        {
            var layout = Assert.Single(row.GetVisualDescendants().OfType<ComboBox>());
            Assert.Equal(0, layout.SelectedIndex);
            Assert.False(layout.IsEnabled);
            var linked = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Loc.Get("Linked")));
            Assert.False(linked.IsEnabled);
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(780)]
    [InlineData(1100)]
    public void DeviceRowsFitNarrowAndWideSettingsWindows(int width)
    {
        var (window, row) = Render(false, width);
        window.RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        window.UpdateLayout();
        try
        {
            foreach (var button in row.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
            {
                var origin = button.TranslatePoint(default, row)!.Value;
                Assert.True(origin.X >= 0 && origin.X + button.Bounds.Width <= row.Bounds.Width + 1);
            }
            var artifacts = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                using var frame = window.CaptureRenderedFrame();
                frame!.Save(Path.Combine(artifacts, $"continuity-dark-{width}.png"));
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaFact]
    public void SharingIndicatorDoesNotStealKeyboardFocusOrAppearAsAnotherApp()
    {
        var indicator = DaisyOS.Shell.Services.DesktopIntegration.PortalSharingIndicator.Create("Keyboard and mouse", "Continuity", () => { });
        Assert.False(indicator.ShowActivated);
        Assert.False(indicator.ShowInTaskbar);
        Assert.Equal(64, indicator.Height);
        indicator.Close();
    }
    private static (SettingsWindow Window, StackPanel Row) Render(bool linked, int width = 1100)
    {
        var window = new SettingsWindow { Width = width, Height = 1100 }; window.Show(); window.SelectCategory("Continuity");
        var view = window.FindControl<SettingsView>("SettingsContent")!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((CancellationTokenSource?)typeof(SettingsView).GetField("_continuityPage", flags)!.GetValue(view))?.Cancel();
        var state = new ContinuityStatus(true, true, false, true, "Laptop", "local", "", "",
            [new("peer", "Desktop", "192.168.1.20", "Discovered", true, false, false, InputEdge: "left")], [], [],
            linked ? [new("link", "peer", "Desktop", "input", true, false, "Ready")] : []);
        typeof(SettingsView).GetMethod("BuildContinuityPage", flags)!.Invoke(view, [state]);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var row = view.FindControl<StackPanel>("CategoryContent")!;
        Assert.DoesNotContain(row.GetVisualDescendants().OfType<ComboBox>(), b => Avalonia.Automation.AutomationProperties.GetName(b) == Loc.Get("Screen position"));
        var name = Assert.Single(row.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.SearchBar>(),
            field => field.PlaceholderText == Loc.Get("Device name"));
        Assert.Equal("Laptop", name.Text);
        Assert.False(name.ShowSearchIcon);
        Assert.False(name.ShowClearButton);
        var details = Assert.Single(row.GetVisualDescendants().OfType<Button>(), b => Equals(b.Content, Loc.Get("Details")));
        details.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        return (window, row);
    }
}
