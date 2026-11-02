using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DaisyOS.Shell.Controls;

namespace DaisyOS.Shell.Apps.Mail;

/// <summary>Shared system-app metrics, scrolling forms and fixed dialog actions.</summary>
internal sealed class MailDialogLayout
{
    public StackPanel Form { get; } = new();
    public WrapPanel Actions { get; } = new() { HorizontalAlignment = HorizontalAlignment.Right };
    public MailDialogLayout(Window window, string heading)
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var title = new TextBlock { Text = heading, FontWeight = Avalonia.Media.FontWeight.SemiBold };
        Token(title, TextBlock.FontSizeProperty, "TypeSizeHeading");
        Token(title, Control.MarginProperty, "DialogContentPadding");
        root.Children.Add(title);
        Token(Form, StackPanel.SpacingProperty, "Space4");
        Form.Margin = new Thickness(24, 0, 24, 24);
        var scroll = new ScrollViewer { Content = Form, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1); root.Children.Add(scroll);
        Token(Actions, WrapPanel.ItemSpacingProperty, "DialogActionSpacing");
        Token(Actions, WrapPanel.LineSpacingProperty, "DialogActionSpacing");
        Token(Actions, Control.MarginProperty, "DialogContentPadding");
        Grid.SetRow(Actions, 2); root.Children.Add(Actions);
        window.Styles.Add(new MailStyles());
        window.Content = root;
        MotionBehavior.SetRevealOnAttach(root, true);
    }
    public static void Token(Control control, AvaloniaProperty property, string key) => control.Bind(property, control.GetResourceObservable(key));
    public static StackPanel Stack(string spacing = "Space4")
    {
        var panel = new StackPanel(); Token(panel, StackPanel.SpacingProperty, spacing); return panel;
    }
    public static Button Action(string title, string style = "Secondary")
    {
        var button = new Button { Content = title, Classes = { style, "Small" } };
        Token(button, Control.MinWidthProperty, "DialogButtonMinWidth");
        return button;
    }
    public static TextBlock Note(string text = "")
    {
        var note = new TextBlock { Text = text, IsVisible = !string.IsNullOrWhiteSpace(text), TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        note.PropertyChanged += (_, change) => { if (change.Property == TextBlock.TextProperty) note.IsVisible = !string.IsNullOrWhiteSpace(note.Text); };
        Token(note, TextBlock.FontSizeProperty, "TypeSizeBodySmall");
        Token(note, TextBlock.ForegroundProperty, "ContentSecondaryBrush");
        return note;
    }
}
