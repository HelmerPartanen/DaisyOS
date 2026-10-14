using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using System.Reflection;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DisplayChangePanelTests
{
    [AvaloniaFact]
    public async Task OwnerWaitsForAnInFlightApplyAndRollbackBeforeClosing()
    {
        var view = new SettingsView();
        var owner = new Window { Content = view, Width = 760, Height = 600 };
        owner.Show();
        var applied = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restored = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var restoring = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var method = typeof(SettingsView).GetMethod("RunDisplayChangeAsync", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var change = (Task)method.Invoke(view, [new Func<Task<bool>>(() => applied.Task),
            new Func<Task<bool>>(() => { restoring.SetResult(); return restored.Task; }), -1])!;
        try
        {
            Assert.False(view.FindControl<StackPanel>("CategoryContent")!.IsEnabled);
            owner.Close();
            owner.Close();
            Assert.True(owner.IsVisible);
            applied.SetResult(true);
            await restoring.Task.WaitAsync(TimeSpan.FromSeconds(2));
            Assert.True(owner.IsVisible);
            restored.SetResult(true);
            await change;
            await Task.Delay(50);
            Dispatcher.UIThread.RunJobs();
            Assert.False(owner.IsVisible);
        }
        finally { applied.TrySetResult(true); restored.TrySetResult(true); owner.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task PanelShowsCountdownAndButtonsResolveTheDecision(bool dark, bool keep)
    {
        var view = new SettingsView();
        var owner = new Window { Content = view, Width = 760, Height = 600,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        owner.Show();
        using var confirmation = new DisplayChangeConfirmation(default);
        try
        {
            view.OpenDisplayConfirmation(confirmation);
            var panel = Assert.Single(owner.OwnedWindows);
            Dispatcher.UIThread.RunJobs();
            var countdown = panel.GetVisualDescendants().OfType<TextBlock>().Single(b => b.Name == "DisplayChangeCountdown");
            Assert.Contains("15 seconds", countdown.Text);
            var progress = Assert.Single(panel.GetVisualDescendants().OfType<ProgressBar>());
            Assert.Equal(15, progress.Value);
            Assert.True(panel.ClientSize.Width >= 400);
            using var frame = panel.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Directory.CreateDirectory("/tmp/daisy-display-confirmation");
            frame!.Save($"/tmp/daisy-display-confirmation/{(dark ? "dark" : "light")}.png", PngBitmapEncoderOptions.Default);
            var button = panel.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, keep ? "Keep changes" : "Revert"));
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(keep, await confirmation.Decision);
            Assert.False(panel.IsVisible);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task ClosingThePanelReverts()
    {
        var view = new SettingsView();
        var owner = new Window { Content = view, Width = 760, Height = 600 };
        owner.Show();
        using var confirmation = new DisplayChangeConfirmation(default);
        try
        {
            view.OpenDisplayConfirmation(confirmation);
            Assert.Single(owner.OwnedWindows).Close();
            Assert.False(await confirmation.Decision);
        }
        finally { owner.Close(); }
    }

    [AvaloniaFact]
    public async Task ExpiryClosesThePanelWithoutUserInput()
    {
        var view = new SettingsView();
        var owner = new Window { Content = view, Width = 760, Height = 600 };
        owner.Show();
        using var confirmation = new DisplayChangeConfirmation(default, duration: TimeSpan.FromMilliseconds(200));
        try
        {
            view.OpenDisplayConfirmation(confirmation);
            var panel = Assert.Single(owner.OwnedWindows);
            Assert.False(await confirmation.Decision.WaitAsync(TimeSpan.FromSeconds(2)));
            await Task.Delay(150);
            Dispatcher.UIThread.RunJobs();
            Assert.False(panel.IsVisible);
        }
        finally { owner.Close(); }
    }
}
