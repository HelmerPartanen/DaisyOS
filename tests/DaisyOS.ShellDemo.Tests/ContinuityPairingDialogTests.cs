using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Continuity;
using DaisyOS.Shell.Services.Localization;
using DaisyOS.Shell.Views.Continuity;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ContinuityPairingDialogTests
{
    [AvaloniaTheory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void PairingDialogsRenderWithSixDigitsAndSharedActions(bool receiving, bool dark)
    {
        var window = Create(receiving);
        window.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        window.Show();
        try
        {
            window.UpdateLayout(); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Equal(WindowDecorations.None, window.WindowDecorations);
            Assert.False(window.ShowInTaskbar);
            var digits = window.GetVisualDescendants().OfType<TextBlock>().Where(t => t.FontSize == 42).ToArray();
            Assert.Equal(6, digits.Length);
            Assert.Equal(receiving ? "243645" : "______", string.Concat(digits.Select(d => d.Text)));
            var action = Action(window);
            Assert.True(action.IsEnabled);
            Assert.Equal(Loc.Get(receiving ? "Done" : "Cancel"), action.Content);
            Assert.Contains("Small", action.Classes);
            Assert.Contains("Flexible", action.Classes);
            var directory = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                frame.Save(Path.Combine(directory, $"pairing-{(receiving ? "display" : "entry")}-{(dark ? "dark" : "light")}.png"));
            }
        }
        finally { window.Complete(false); window.Close(); }
    }

    [AvaloniaFact]
    public void EnteredCodePreservesLeadingZeroAndCanOnlySubmitOnce()
    {
        var window = Create(false); window.Show();
        try
        {
            var input = Assert.Single(window.GetVisualDescendants().OfType<TextBox>());
            var submitted = new List<string>(); window.Submitted += submitted.Add;
            input.Text = "abc"; Dispatcher.UIThread.RunJobs();
            Assert.Empty(submitted);
            input.Text = "012 345"; Dispatcher.UIThread.RunJobs();
            Assert.Equal("012345", input.Text);

            Assert.Equal("012345", Assert.Single(submitted));
            Assert.False(input.IsEnabled);
            Assert.True(Action(window).IsEnabled);
            var cancelled = false; window.Cancelled += () => cancelled = true;
            window.Complete(true);
            Assert.False(window.IsVisible);
            Assert.False(cancelled);
        }
        finally { window.Complete(false); window.Close(); }
    }

    [AvaloniaFact]
    public void ClosingPendingPairingCancelsAndFailureCanBeDismissed()
    {
        var pending = Create(false); var cancelled = false;
        pending.Cancelled += () => cancelled = true;
        pending.Show(); pending.Close();
        Assert.True(cancelled);
        var failed = Create(false); failed.Show();
        try
        {
            failed.Complete(false);
            Assert.True(Action(failed).IsEnabled);
            Assert.Contains(failed.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == Loc.Get("Pairing ended. Check the code and try again."));
        }
        finally { failed.Close(); }
    }

    [AvaloniaFact]
    public void ShellOwnsOnePopupPerRequestAndRoutesCodeAndCancellation()
    {
        var client = new PairingClient();
        var windows = new List<ContinuityPairingDialog>();
        var service = new DaisyOS.Shell.Services.ContinuityPairingService(client, prompt =>
        {
            var window = new ContinuityPairingDialog(prompt); windows.Add(window); return window;
        });
        var prompt = new ContinuityPrompt("one", "pair-display", "peer", "Desktop", "243645");
        var state = new ContinuityStatus(true, true, false, true, "Laptop", "local", "", "", [], [prompt], []);
        try
        {
            service.Update(state); service.Update(state);
            var first = Assert.Single(windows);
            Assert.True(first.IsVisible);
            service.Update(state with { Prompts = [], Devices = [new("peer", "Desktop", "", "Trusted", true, true, true)] });
            Assert.True(first.Finished);
            Action(first).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Empty(client.Commands);
            var next = state with { Prompts = [prompt with { Id = "two", Kind = "pair-code", Detail = "" }] };
            service.Update(next);
            var second = windows.Last();
            Assert.Single(windows.Where(w => w.IsVisible));
            var input = Assert.Single(second.GetVisualDescendants().OfType<TextBox>());
            input.Text = "012345"; Dispatcher.UIThread.RunJobs();

            Assert.Contains(client.Commands, c => c.Action == "pair-code" && c.Id == "two" && c.Value == "012345");
            second.Close();
            Assert.Contains(client.Commands, c => c.Action == "pair-cancel" && c.Id == "two");
            service.Update(next);
            Assert.Equal(2, windows.Count);
        }
        finally { foreach (var window in windows) { window.Complete(false); window.Close(); } }
    }

    [AvaloniaFact]
    public void ReceiverDoneDismissesCodeWithoutCancellingThePairing()
    {
        var window = Create(true); var cancelled = false;
        window.Cancelled += () => cancelled = true;
        window.Show();
        Action(window).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(window.IsVisible);
        Assert.False(cancelled);
    }

    private sealed class PairingClient : IContinuityClient
    {
        public List<ContinuityCommand> Commands { get; } = [];
        public Task<ContinuityStatus> ExecuteAsync(ContinuityCommand command, CancellationToken token = default)
        {
            Commands.Add(command);
            return Task.FromResult(new ContinuityStatus(true, true, false, true, "Laptop", "local", "", "", [], [], []));
        }
    }

    private static ContinuityPairingDialog Create(bool receiving) => new(new ContinuityPrompt("request", receiving ? "pair-display" : "pair-code", "peer", "Desktop", receiving ? "243645" : ""));
    private static Button Action(ContinuityPairingDialog window) => Assert.Single(window.GetVisualDescendants().OfType<Button>());
}
