using System.Reflection;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
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
            Assert.Contains(buttons, b => Equals(b.Content, Loc.Get("Link keyboard and mouse")) && b.IsEnabled);
            Assert.Contains(buttons, b => Equals(b.Content, Loc.Get("Use as audio output")));
            Assert.Contains(buttons, b => Equals(b.Content, Loc.Get("View screen")));
            Assert.DoesNotContain(buttons, b => Equals(b.Content, Loc.Get("Connect")));
            Assert.False(Assert.Single(row.GetVisualDescendants().OfType<Expander>()).IsExpanded);
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
    [AvaloniaFact]
    public void SharingIndicatorDoesNotStealKeyboardFocusOrAppearAsAnotherApp()
    {
        var indicator = DaisyOS.Shell.Services.DesktopIntegration.PortalSharingIndicator.Create("Keyboard and mouse", "Continuity", () => { });
        Assert.False(indicator.ShowActivated);
        Assert.False(indicator.ShowInTaskbar);
        Assert.Equal(64, indicator.Height);
        indicator.Close();
    }
    private static (SettingsWindow Window, Border Row) Render(bool linked)
    {
        var window = new SettingsWindow { Width = 1100, Height = 900 }; window.Show(); window.SelectCategory("Continuity");
        var view = window.FindControl<SettingsView>("SettingsContent")!;
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        ((CancellationTokenSource?)typeof(SettingsView).GetField("_continuityPage", flags)!.GetValue(view))?.Cancel();
        var state = new ContinuityStatus(true, true, false, true, "Laptop", "local", "", "",
            [new("peer", "Desktop", "192.168.1.20", "Discovered", true, false, false, InputEdge: "left")], [], [],
            linked ? [new("link", "peer", "Desktop", "input", true, false, "Ready")] : []);
        typeof(SettingsView).GetMethod("BuildContinuityPage", flags)!.Invoke(view, [state]);
        window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
        var row = view.GetVisualDescendants().OfType<Border>().Single(b => Equals(b.Tag, "Desktop") && b.GetVisualDescendants().OfType<ComboBox>().Any());
        return (window, row);
    }
}
