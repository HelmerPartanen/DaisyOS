using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Services.DesktopIntegration;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AuthenticationDialogTests
{
    [Fact]
    public void CurrentUserIsSelectedEvenWhenNotFirstAndOnlyAllowedIdentitiesAreUsed()
    {
        Assert.Equal(1, AuthenticationAgent.SelectIdentity([("0", "root"), ("1000", "helmeri")], "helmeri"));
        Assert.Equal(0, AuthenticationAgent.SelectIdentity([("0", "root")], "helmeri"));
        Assert.Equal(-1, AuthenticationAgent.SelectIdentity([("0", "root"), ("1001", "admin")], "helmeri"));
        Assert.Equal(-1, AuthenticationAgent.SelectIdentity([], "helmeri"));
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompactDialogRendersWithoutChromeInBothThemes(bool dark)
    {
        var dialog = new AuthenticationDialog("Authentication is needed to run a system command.", "org.freedesktop.policykit.exec", "helmeri", (_, _) => { }, "/usr/bin/install");
        dialog.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        dialog.Show();
        try
        {
            dialog.Prompt("Password:", false);
            Dispatcher.UIThread.RunJobs();
            dialog.UpdateLayout();
            using var frame = dialog.CaptureRenderedFrame();
            Assert.NotNull(frame);
            Directory.CreateDirectory("/tmp/daisy-authentication-preview");
            frame.Save("/tmp/daisy-authentication-preview/" + (dark ? "dark" : "light") + ".png", PngBitmapEncoderOptions.Default);
            Assert.InRange(dialog.Bounds.Height, 200, 480);
            Assert.Empty(dialog.GetVisualDescendants().OfType<ComboBox>());
        }
        finally { dialog.Complete(true); }
    }

    [AvaloniaFact]
    public void DialogHasNoWindowChromeOrAccountSelectorAndFocusesTheChallenge()
    {
        var sent = new List<(string Kind, string Text)>();
        var dialog = new AuthenticationDialog("Allow changing a system setting?", "org.example.test", "helmeri", (kind, text) => sent.Add((kind, text)));
        dialog.Show();
        try
        {
            dialog.Prompt("Password:", false);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(WindowDecorations.None, dialog.WindowDecorations);
            Assert.False(dialog.CanMinimize); Assert.False(dialog.CanMaximize); Assert.False(dialog.CanResize);
            Assert.Empty(dialog.GetVisualDescendants().OfType<ComboBox>());
            Assert.True(dialog.Response.IsFocused);
            Assert.Equal('●', dialog.Response.PasswordChar);
            Assert.Contains("Small", dialog.Submit.Classes);
            Assert.Equal("Submit", dialog.Submit.Content);
            Assert.Empty(dialog.GetVisualDescendants().OfType<Expander>());
            var details = Assert.Single(dialog.GetVisualDescendants().OfType<Avalonia.Controls.Primitives.ToggleButton>());
            Assert.Contains("Small", details.Classes); Assert.Contains("Ghost", details.Classes);
            var cancel = Assert.Single(dialog.GetVisualDescendants().OfType<Button>().Where(button => button.IsCancel));
            Assert.Contains("Small", cancel.Classes); Assert.Contains("Secondary", cancel.Classes);
            dialog.Response.Text = "fixture-secret";
            Assert.Equal("", dialog.PasswordField.Text);
            dialog.Submit.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(("response", "fixture-secret"), Assert.Single(sent));
            Assert.Equal("", dialog.Response.Text);
            Assert.False(dialog.Response.IsEnabled);
            dialog.Prompt("Password:", false);
            Assert.True(dialog.Response.IsEnabled);
            dialog.Complete(false);
            Assert.True(dialog.Status.IsVisible);
            Assert.False(dialog.Submit.IsEnabled);
            Assert.True(dialog.IsVisible);
        }
        finally { dialog.Close(); }
        Assert.Single(sent); // A finished request must not cancel a later conversation.
    }

    [AvaloniaFact]
    public void ClosingAnActiveDialogCancelsOnceAndClearsTheResponse()
    {
        var sent = new List<string>();
        var dialog = new AuthenticationDialog("Test", "org.example.test", "helmeri", (kind, _) => sent.Add(kind));
        dialog.Show(); dialog.Prompt("Password:", false); dialog.Response.Text = "fixture-secret";
        dialog.Close();
        Assert.Equal("", dialog.Response.Text);
        Assert.Equal(["cancel"], sent);
    }
}
