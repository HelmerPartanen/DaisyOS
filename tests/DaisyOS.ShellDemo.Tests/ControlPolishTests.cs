using Avalonia.Controls.Presenters;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Avalonia;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class ControlPolishTests
{
    [AvaloniaFact]
    public void ButtonSizeVariantsHaveDistinctRenderedHeights()
    {
        var row = new StackPanel { Spacing = 8 };
        foreach (var size in new[] { "Small", "Medium", "Large" })
            row.Children.Add(new Button { Content = size, Classes = { "Primary", size } });
        var window = new Window { Content = row, Width = 240, SizeToContent = SizeToContent.Height };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(new double[] { 32, 36, 42 }, row.Children.Select(control => control.Bounds.Height).ToArray());
            Assert.All(row.Children.OfType<Button>(), button => Assert.Equal(RelativePoint.Center, button.RenderTransformOrigin));
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void StandardActionsAndFieldsHaveMatchingRenderedHeights(bool dark)
    {
        var row = new StackPanel { Spacing = 12, Margin = new Thickness(24) };
        foreach (var variant in new[] { "Primary", "Secondary", "Danger", "Compact", "Comfortable" })
            row.Children.Add(new Button { Content = variant, Classes = { variant } });
        row.Children.Add(new ComboBox { ItemsSource = new[] { "Choose an option" }, SelectedIndex = 0 });
        row.Children.Add(new TextBox { Text = "Single line" });
        row.Children.Add(new SearchBar { PlaceholderText = "Search" });
        var window = new Window { RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light, Content = row, Width = 420, SizeToContent = SizeToContent.Height };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var frame = window.CaptureRenderedFrame();
            foreach (var control in row.Children)
                Assert.Equal(control is TextBox or ComboBox or SearchBar ? 40 : 32, control.Bounds.Height);
            foreach (var button in row.Children.OfType<Button>())
                Assert.Equal(RelativePoint.Center, button.RenderTransformOrigin);
            Directory.CreateDirectory("/tmp/daisy-control-polish");
            frame!.Save("/tmp/daisy-control-polish/controls-" + (dark ? "dark" : "light") + ".png", PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ConfirmationKeepsCancelDefaultAndCompactActions(bool dark)
    {
        var type = typeof(DaisyOS.Shell.App).Assembly.GetType("DaisyOS.Shell.Views.Dialogs.ConfirmationDialog")!;
        var dialog = (Window)Activator.CreateInstance(type, "Delete note", "Delete ‘Untitled note’?",
            "This permanently deletes the note and its file. This action cannot be undone.", "Delete note", "Cancel")!;
        dialog.RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light;
        dialog.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            using var frame = dialog.CaptureRenderedFrame();
            var panel = (StackPanel)dialog.Content!;
            var actions = (Panel)panel.Children.Last();
            var cancel = (Button)actions.Children[0];
            Assert.True(cancel.IsDefault);
            Assert.True(cancel.IsCancel);
            Assert.Equal("Cancel", cancel.Content);
            Assert.All(actions.Children, control => Assert.Equal(32, control.Bounds.Height));
            Directory.CreateDirectory("/tmp/daisy-control-polish");
            frame!.Save("/tmp/daisy-control-polish/confirmation-" + (dark ? "dark" : "light") + ".png", PngBitmapEncoderOptions.Default);
        }
        finally { dialog.Close(); }
    }
    [AvaloniaFact]
    public async Task PressScalingKeepsLabelCenteredWithoutASecondTransform()
    {
        var button = new Button { Content = "Done", Classes = { "Primary" }, Width = 100 };
        var window = new Window { Content = button, Width = 160, Height = 80 };
        window.Show();
        try
        {
            using (var initial = window.CaptureRenderedFrame()) { }
            var presenter = button.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(control => control.Name == "PART_ContentPresenter");
            var center = presenter.TranslatePoint(new Rect(presenter.Bounds.Size).Center, window)!.Value;
            var point = button.TranslatePoint(new Rect(button.Bounds.Size).Center, window)!.Value;
            window.MouseDown(point, MouseButton.Left);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(220);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            using (var pressed = window.CaptureRenderedFrame()) { }
            Assert.True(button.IsPressed);
            Assert.Equal(RelativePoint.Center, button.RenderTransformOrigin);
            Assert.InRange(button.RenderTransform!.Value.M11, 0.979, 0.981);
            Assert.True(presenter.RenderTransform is null || presenter.RenderTransform.Value.IsIdentity);
            var pressedCenter = presenter.TranslatePoint(new Rect(presenter.Bounds.Size).Center, window)!.Value;
            Assert.InRange(Math.Abs(center.X - pressedCenter.X), 0, 0.01);
            Assert.InRange(Math.Abs(center.Y - pressedCenter.Y), 0, 0.01);
            window.MouseUp(point, MouseButton.Left);
        }
        finally { window.Close(); }
    }
}
