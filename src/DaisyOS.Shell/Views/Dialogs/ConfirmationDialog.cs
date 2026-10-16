using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace DaisyOS.Shell.Views.Dialogs;

internal sealed class ConfirmationDialog : Window
{
    public static Task<bool> ShowApplicationRemovalAsync(Window owner, string appName) =>
        new ConfirmationDialog(Loc.Get("Remove application"), Loc.Format("Remove {0}?", appName),
            Loc.Get("The application and its package files will be removed. Your personal files are not deleted."),
            Loc.Get("Remove"), Loc.Get("Keep application")).ShowDialog<bool>(owner);

    public ConfirmationDialog(string title, string heading, string message, string confirmText, string cancelText = "Cancel")
    {
        Title = title;
        Width = 420;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        CanMinimize = false;
        CanMaximize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));

        var keepEditing = new Button { Content = cancelText, Classes = { "Secondary" }, IsDefault = true, IsCancel = true };
        var discard = new Button { Content = confirmText, Classes = { "Danger" } };
        keepEditing.Click += (_, _) => Close(false);
        discard.Click += (_, _) => Close(true);
        var headingText = new TextBlock { Text = heading, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap };
        headingText.Bind(TextBlock.FontSizeProperty, this.GetResourceObservable("TypeSizeBodyLarge"));
        var messageText = new TextBlock { Text = message, Classes = { "Body" }, TextWrapping = TextWrapping.Wrap };
        messageText.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("ContentSecondaryBrush"));
        Content = new StackPanel
        {
            Margin = new Thickness(24), Spacing = 12,
            Children =
            {
                headingText,
                messageText,
                new WrapPanel
                {
                    Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, ItemSpacing = 8, LineSpacing = 8,
                    Margin = new Thickness(0, 8, 0, 0),
                    Children = { keepEditing, discard }
                }
            }
        };
        Controls.MotionBehavior.SetRevealOnAttach((Control)Content, true);
        Opened += (_, _) => keepEditing.Focus();
    }
}
