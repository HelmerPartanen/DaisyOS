using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using MimeKit;
using Avalonia.Automation;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Apps.Mail;

internal static class MailDialogs
{
    internal static Window Window(string title, int width = 540, int height = 650)
    {
        var window = new Window
        {
            Title = title, Width = width, Height = height, MinWidth = 440, MinHeight = 400,
            WindowStartupLocation = WindowStartupLocation.CenterOwner, ShowInTaskbar = false
        };
        window.Bind(Avalonia.Controls.Primitives.TemplatedControl.BackgroundProperty, window.GetResourceObservable("SurfaceBaseSolidBrush"));
        window.Bind(Avalonia.Controls.Primitives.TemplatedControl.ForegroundProperty, window.GetResourceObservable("ContentPrimaryBrush"));
        return window;
    }
    internal static void FollowTheme(Window dialog, Window owner)
    {
        var binding = dialog.Bind(TopLevel.RequestedThemeVariantProperty, owner.GetObservable(TopLevel.ActualThemeVariantProperty));
        dialog.Closed += (_, _) => binding.Dispose();
    }
    internal static TextBox Field(StackPanel panel, string label, string text = "")
    {
        var group = new StackPanel(); MailDialogLayout.Token(group, StackPanel.SpacingProperty, "Space1");
        var caption = MailDialogLayout.Note(label); group.Children.Add(caption);
        var box = new TextBox { Name = new string(label.Where(char.IsLetterOrDigit).ToArray()) + "Field", Text = text };
        AutomationProperties.SetName(box, label);
        group.Children.Add(box); panel.Children.Add(group); return box;
    }
    public static async Task<MailAccount?> AddAccountAsync(Window owner, MailService service, Func<MailAccount, Task> save)
    {
        var dialog = Window("Add account", height: 560); FollowTheme(dialog, owner);
        var setup = new MailAccountSetup { Service = service };
        setup.Connected = async account => { await save(account); dialog.Close(account); };
        dialog.Content = setup;
        dialog.Closed += (_, _) => setup.Cancel();
        return await dialog.ShowDialog<MailAccount?>(owner);
    }

}
