using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class DesktopItemLabelTests
{
    [Theory]
    [InlineData("Muistiinpanot")]
    [InlineData("Disk Manager")]
    [InlineData("QuarterlyReport2026.pdf")]
    [InlineData("👍🏽👍🏽👍🏽👍🏽👍🏽")]
    public void WrapsAtBalancedGraphemeOrWordBoundaries(string name)
    {
        double Measure(string s) => global::System.Globalization.StringInfo.ParseCombiningCharacters(s).Length * 6;
        var result = DesktopItemLabel.Balance(name, 74, Measure);
        Assert.Equal(name.Replace(" ", ""), result.Replace("\n", "").Replace(" ", ""));
        if (result.Contains('\n')) Assert.All(result.Split('\n'), line => Assert.True(global::System.Globalization.StringInfo.ParseCombiningCharacters(line).Length >= 2));
    }
    [AvaloniaTheory]
    [InlineData(false, 74)]
    [InlineData(true, 74)]
    [InlineData(false, 82)]
    [InlineData(true, 82)]
    public async Task DesktopNamesRenderWithoutOrphanLettersAndUpdateAfterRename(bool dark, int width)
    {
        var panel = new StackPanel { Orientation = Avalonia.Layout.Orientation.Horizontal, Spacing = 12 };
        foreach (var name in new[] { "Muistiinpanot", "Disk Manager", "A very long desktop file name.pdf", "e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301e\u0301" })
            panel.Children.Add(new DesktopItemLabel { FullText = name, FontFamily = (FontFamily)Application.Current!.FindResource("ShellFontFamily")!, Width = width, FontSize = 12, MaxLines = 2, TextWrapping = TextWrapping.NoWrap, TextTrimming = TextTrimming.CharacterEllipsis, TextAlignment = TextAlignment.Center, Foreground = dark ? Brushes.White : Brushes.Black });
        var window = new Window { Content = panel, Width = 420, Height = 100, Background = dark ? Brushes.Black : Brushes.White, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            window.UpdateLayout(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var label = (DesktopItemLabel)panel.Children[0];
            Assert.DoesNotContain(label.Text!.Split('\n'), line => line.Length == 1);
            Assert.True(label.TextLayout.TextLines.Count <= 2);
            label.FullText = "Notes"; window.UpdateLayout(); Dispatcher.UIThread.RunJobs();
            Assert.Equal("Notes", label.Text);
            label.FullText = "Muistiinpanot"; window.UpdateLayout();
            await Task.Delay(250); Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            foreach (var item in panel.Children.Cast<DesktopItemLabel>())
            {
                Assert.True(item.Bounds.Height > 0, $"{item.FullText}: bounds={item.Bounds} text={item.Text}");
                Assert.True(item.TextLayout.TextLines.Count > 0, $"{item.FullText}: no lines, text={item.Text}");
            }
            Directory.CreateDirectory("/tmp/desktop-labels");
            using var frame = window.CaptureRenderedFrame(); frame!.Save($"/tmp/desktop-labels/{dark}-{width}.png");
        }
        finally { window.Close(); }
    }
}
