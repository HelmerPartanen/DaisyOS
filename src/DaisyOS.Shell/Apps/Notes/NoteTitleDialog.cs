using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace DaisyOS.Shell.Apps.Notes;

internal sealed class NoteTitleDialog : Window
{
    internal NoteTitleDialog(string title)
    {
        Title = Loc.Get("Edit note title");
        Width = 440;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("SurfaceBaseSolidBrush"));
        var input = Loc.Bind(new TextBox { Text = title, PlaceholderText = "Use the first line of the note", MaxLength = 200 }, TextBox.PlaceholderTextProperty, "Use the first line of the note");
        var cancel = Loc.Bind(new Button { Content = "Cancel", Classes = { "Subtle" }, IsCancel = true }, Button.ContentProperty, "Cancel");
        var save = Loc.Bind(new Button { Content = "Save title", Classes = { "Primary" }, IsDefault = true }, Button.ContentProperty, "Save title");
        cancel.Click += (_, _) => Close(null);
        save.Click += (_, _) => Close(input.Text ?? string.Empty);
        var content = new StackPanel
        {
            Children =
            {
                Loc.Bind(new TextBlock { Text = "Note title", Classes = { "Title" } }, TextBlock.TextProperty, "Note title"),
                input,
                Loc.Bind(new TextBlock { Text = "Leave this empty to use the first line of your note.", TextWrapping = Avalonia.Media.TextWrapping.Wrap, Classes = { "SecondaryText" } }, TextBlock.TextProperty, "Leave this empty to use the first line of your note."),
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Spacing = 8, Children = { cancel, save } }
            }
        };
        content.Bind(MarginProperty, content.GetResourceObservable("SpaceInset6"));
        content.Bind(StackPanel.SpacingProperty, content.GetResourceObservable("Space4"));
        Content = content;
        Opened += (_, _) => { input.Focus(); input.SelectAll(); };
    }
}
