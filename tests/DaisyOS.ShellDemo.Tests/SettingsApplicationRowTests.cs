using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Core.Models;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Controls;
using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class SettingsApplicationRowTests
{
    [AvaloniaTheory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Install Linux application packages")]
    public void ApplicationCardsLoadNativeIconsAndOmitEmptyCaptions(string description)
    {
        var settings = new SettingsView();
        var app = new AppEntry("org.daisyos.appinstaller", "App Installer", description, "appinstaller", false, AppLaunchKind.Internal);
        var row = settings.CreateApplicationListRow(app, new ToggleSwitch());
        var window = new Window { Width = 640, Height = 200, Content = row };
        window.Show();
        try
        {
            window.UpdateLayout();
            Assert.Equal(new Thickness(18, 12), row.Padding);
            Assert.Equal(new CornerRadius(12), row.CornerRadius);
            Assert.Equal(34, Assert.Single(row.GetVisualDescendants().OfType<Image>()).Width);
            Assert.NotNull(Assert.Single(row.GetVisualDescendants().OfType<Image>()).Source);
            var labels = ((StackPanel)((Grid)row.Child!).Children[1]).Children.OfType<TextBlock>().ToArray();
            var title = Assert.Single(labels, label => label.Text == app.Name);
            if (string.IsNullOrWhiteSpace(description))
            {
                Assert.Single(labels);
                var center = title.TranslatePoint(new Point(0, title.Bounds.Height / 2), row)!.Value.Y;
                Assert.InRange(Math.Abs(center - row.Bounds.Height / 2), 0, 1);
            }
            else Assert.Equal(2, labels.Length);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DefaultApplicationSelectorMeasuresCompleteLabelsAndLoadsBundledIcons()
    {
        var choices = new[]
        {
            new AppAssociationInfo("org.daisyos.files.desktop", "DaisyOS File Manager", "files"),
            new AppAssociationInfo("org.daisyos.appinstaller.desktop", "Application Installer with a Longer Name", "appinstaller")
        };
        var picker = SettingsView.CreateDefaultApplicationPicker(choices);
        picker.SelectedItem = choices[0];
        picker.FontSize = 16;
        picker.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right;
        var window = new Window { Width = 640, Height = 200, Content = new StackPanel { Children = { picker } } };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var expected = SettingsView.BuildDefaultApplicationChoice(choices[1]);
            TextElement.SetFontSize(expected, picker.FontSize);
            TextElement.SetFontFamily(expected, picker.FontFamily);
            TextElement.SetFontWeight(expected, picker.FontWeight);
            expected.Measure(Size.Infinity);
            Assert.True(picker.Bounds.Width >= expected.DesiredSize.Width + 58);
            Assert.True(picker.Bounds.Width <= 440);
            Assert.NotNull(Assert.Single(picker.GetVisualDescendants().OfType<Image>()).Source);
            var label = picker.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == choices[0].DisplayName);
            Assert.True(label.Bounds.Width >= label.TextLayout.Width);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ApplicationOptionsGlyphIsBundledAndSidebarSaysSearch()
    {
        Assert.True(AssetLoader.Exists(new Uri("avares://DaisyOS.Shell/Assets/Icons/HugeIcons/dots-three-vertical.svg")));
        var view = new SettingsView();
        var window = new Window { Width = 900, Height = 700, Content = view };
        window.Show();
        try
        {
            var nav = view.FindControl<Button>("NavSearch")!;
            Assert.Equal("Search", Assert.Single(nav.GetVisualDescendants().OfType<TextBlock>()).Text);
        }
        finally { window.Close(); }
    }
}
