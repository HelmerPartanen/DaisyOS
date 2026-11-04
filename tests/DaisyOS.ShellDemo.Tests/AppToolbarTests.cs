using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.Notes.Documents;
using DaisyOS.Shell.Apps.Notes.Editor.Rich;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class AppToolbarTests
{
    [AvaloniaTheory]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(40)]
    [InlineData(48)]
    public void IconButtonsStayRoundWhilePressedCheckedAndResized(int size)
    {
        var button = new ToggleButton { Classes = { "ShellButton", "IconButton" }, Height = size, Content = new DaisyIcon { Icon = "eye" } };
        var toolbar = new Border { Classes = { "AppToolbar" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, Child = button };
        var window = new Window { Width = 400, Height = 200, Content = toolbar };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Equal(size, button.Bounds.Width);
            Assert.Equal(size, button.Bounds.Height);
            Assert.Equal(size / 2d, button.CornerRadius.TopLeft);
            Assert.Equal(toolbar.Bounds.Height / 2, toolbar.CornerRadius.TopLeft);
            var surface = button.GetVisualDescendants().OfType<Border>().Single(b => b.Name == "IconSurface");
            var shape = surface.Bounds;
            var point = button.TranslatePoint(new Point(size / 2d, size / 2d), window)!.Value;
            window.MouseMove(point);
            window.MouseDown(point, MouseButton.Left);
            Assert.Equal(shape, surface.Bounds);
            Assert.True(button.RenderTransform is null || button.RenderTransform.Value.IsIdentity);
            window.MouseUp(point, MouseButton.Left);
            Assert.True(button.IsChecked);
            button.Height = size + 8;
            window.UpdateLayout();
            Assert.Equal(size + 8, button.Bounds.Width);
            Assert.Equal((size + 8) / 2d, button.CornerRadius.TopLeft);
            button.IsEnabled = false;
            Assert.False(button.IsEffectivelyEnabled);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ToolbarTokensUpdateAllGroupsTogether()
    {
        var first = new Border { Classes = { "AppToolbar" }, Child = new TextBlock { Text = "Navigation" } };
        var second = new Border { Classes = { "AppToolbar" }, Child = new TextBlock { Text = "Breadcrumbs" } };
        var host = new StackPanel { Children = { first, second } };
        var window = new Window { Width = 400, Height = 240, Content = host };
        window.Show();
        try
        {
            host.Resources["AppToolbarPadding"] = new Thickness(12);
            window.UpdateLayout();
            Assert.Equal(new Thickness(12), first.Padding);
            Assert.Equal(first.Padding, second.Padding);
            Assert.Equal(first.Bounds.Height / 2, first.CornerRadius.TopLeft);
            Assert.Equal(second.Bounds.Height / 2, second.CornerRadius.TopLeft);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NotesOverflowKeepsSelectionAndClosesWhenSwitchingDocuments()
    {
        var package = new NotePackage { Document = NoteDocument.FromPlainText("Keep this selection") };
        var view = new RichEditorView();
        view.LoadPackage(package);
        var window = new Window { Width = 760, Height = 640, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            var editor = view.GetVisualDescendants().OfType<RichTextBlockEditor>().Single();
            editor.Focus();
            editor.SelectRange(0, 4);
            var more = view.FindControl<Button>("MoreFormattingButton")!;
            var flyout = (Flyout)more.Flyout!;
            var point = more.TranslatePoint(new Point(16, 16), window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            Assert.True(flyout.IsOpen);
            window.UpdateLayout();
            var artifacts = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
            if (!string.IsNullOrEmpty(artifacts))
            {
                Directory.CreateDirectory(artifacts);
                window.CaptureRenderedFrame()!.Save(Path.Combine(artifacts, "notes-more.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
            }
            view.OnIncreaseFontSizeClicked(null, new Avalonia.Interactivity.RoutedEventArgs());
            view.OnIncreaseFontSizeClicked(null, new Avalonia.Interactivity.RoutedEventArgs());
            Assert.True(flyout.IsOpen);
            Assert.Equal(20, package.Document.Blocks[0].Runs[0].Style.FontSize);
            Assert.Null(package.Document.Blocks[0].Runs[^1].Style.FontSize);
            var family = view.FindControl<ComboBox>("FontFamilyCombo")!;
            family.SelectedItem = family.Items.OfType<ComboBoxItem>().First(item => Equals(item.Content, "Roboto"));
            Assert.False(flyout.IsOpen);
            Assert.Equal("Roboto", package.Document.Blocks[0].Runs[0].Style.FontFamily);
            Assert.Null(package.Document.Blocks[0].Runs[^1].Style.FontFamily);
            flyout.ShowAt(more);
            view.LoadPackage(new NotePackage());
            Assert.False(flyout.IsOpen);
        }
        finally { window.Close(); }
    }
}
