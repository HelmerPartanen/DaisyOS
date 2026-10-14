using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class SettingsLayoutTests
{
    [AvaloniaTheory]
    [InlineData(false, 760)]
    [InlineData(true, 760)]
    [InlineData(false, 1280)]
    [InlineData(true, 1280)]
    public async Task SettingsPagesKeepReadableHierarchyAndControlsInsideContent(bool dark, int width)
    {
        var window = new SettingsWindow { Width = width, Height = 900,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            foreach (var category in new[] { "Appearance", "System", "Displays", "Sound", "Network", "Ethernet", "Bluetooth", "Notifications", "Apps", "InstalledApps", "Storage", "Accessibility", "Input", "DateTime", "About" })
            {
                window.SelectCategory(category);
                Dispatcher.UIThread.RunJobs();
                var categoryContent = view.FindControl<StackPanel>("CategoryContent")!;
                for (var attempt = 0; category != "Appearance" && attempt < 160 && categoryContent.Children.OfType<TextBlock>().Any(text => text.Text?.EndsWith("…") == true); attempt++)
                {
                    await Task.Delay(50);
                    Dispatcher.UIThread.RunJobs();
                }
                if (category != "Appearance")
                    Assert.True(!categoryContent.Children.OfType<TextBlock>().Any(text => text.Text?.EndsWith("…") == true), $"{category} did not finish loading");
                if (category == "Appearance")
                {
                    view.FindControl<ComboBox>("WallpaperTypePicker")!.SelectedIndex = 0;
                    view.FindControl<ComboBox>("ThemeModePicker")!.SelectedIndex = 0;
                    view.FindControl<ComboBox>("TextScalePicker")!.SelectedIndex = 1;
                }
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                var content = view.FindControl<StackPanel>(category == "Appearance" ? "AppearanceContent" : "CategoryContent")!;
                var labels = content.GetVisualDescendants().OfType<TextBlock>()
                    .Where(text => text.IsEffectivelyVisible && text.Classes.Contains("SettingsLabel")).ToArray();
                Assert.All(labels, text => { Assert.Equal(14, text.FontSize); Assert.Equal(FontWeight.Regular, text.FontWeight); });
                var title = content.GetVisualDescendants().OfType<TextBlock>().First(text => text.Classes.Contains("SettingsPageTitle"));
                Assert.Equal(24, title.FontSize);
                Assert.Equal(FontWeight.SemiBold, title.FontWeight);
                var controls = content.GetVisualDescendants().OfType<Control>()
                    .Where(control => control.IsEffectivelyVisible && control is Button or ComboBox or ToggleSwitch or TextBlock);
                foreach (var control in controls)
                {
                    var point = control.TranslatePoint(default, content);
                    Assert.True(point is not null);
                    Assert.True(point.Value.X >= -1 && point.Value.X + control.Bounds.Width <= content.Bounds.Width + 1,
                        $"{category}/{width}: {control.GetType().Name} {control.Name} extends outside the page: {point.Value.X} + {control.Bounds.Width} > {content.Bounds.Width}");
                }
                if (category == "Appearance")
                {
                    var fontHeader = view.FindControl<Grid>("FontHeaderLayout")!;
                    Assert.Equal(width == 760 ? 2 : 1, fontHeader.RowDefinitions.Count);
                    var browse = view.FindControl<Button>("WallpaperBrowseButton")!;
                    Assert.Contains(browse.GetVisualAncestors().OfType<Border>(), border => border.Classes.Contains("SettingsGroupCard"));
                }
                foreach (var row in content.GetVisualDescendants().OfType<Border>().Where(row => row.Classes.Contains("SettingsGroupRow")))
                {
                    if (row.Child is not Grid grid) continue;
                    var offset = grid.TranslatePoint(default, row)!.Value;
                    Assert.InRange(Math.Abs(row.Bounds.Height / 2 - (offset.Y + grid.Bounds.Height / 2)), 0, 1);
                }
                await Task.Delay(300);
                Dispatcher.UIThread.RunJobs();
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);
                var artifacts = Environment.GetEnvironmentVariable("DAISYOS_UX_ARTIFACTS");
                if (!string.IsNullOrWhiteSpace(artifacts))
                {
                    Directory.CreateDirectory(artifacts);
                    frame.Save(Path.Combine(artifacts, $"settings-{category}-{(dark ? "dark" : "light")}-{width}.png"), new Avalonia.Media.Imaging.PngBitmapEncoderOptions());
                }
            }
        }
        finally { window.Close(); }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SettingsCardsAndIconsUseTheirWindowTheme(bool dark)
    {
        var window = new SettingsWindow { Width = 1000, Height = 800,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            window.SelectCategory("Apps");
            window.UpdateLayout();
            var action = view.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("SettingsGroupAction"));
            var presenter = action.GetVisualDescendants().OfType<Avalonia.Controls.Presenters.ContentPresenter>().Single(control => control.Name == "PART_ContentPresenter");
            view.TryFindResource("ActionSecondaryBrush", view.ActualThemeVariant, out var surface);
            await Task.Delay(300);
            using (window.CaptureRenderedFrame()) { }
            Assert.Equal(((ISolidColorBrush)surface!).Color, ((ISolidColorBrush)presenter.Background!).Color);
            var arrow = action.GetVisualDescendants().OfType<DaisyOS.Shell.Controls.DaisyIcon>().Single(icon => icon.Icon == "caret-right");
            view.TryFindResource("ContentSecondaryBrush", view.ActualThemeVariant, out var foreground);
            Assert.Equal(((ISolidColorBrush)foreground!).Color, ((ISolidColorBrush)arrow.Foreground!).Color);
            var actionPadding = action.Padding;
            var actionMinHeight = action.MinHeight;
            var actionRadius = action.CornerRadius;
            var actionHeight = action.Bounds.Height;
            window.SelectCategory("Storage");
            for (var attempt = 0; attempt < 700; attempt++)
            {
                await Task.Delay(50);
                Dispatcher.UIThread.RunJobs();
                if (view.GetVisualDescendants().OfType<ProgressBar>().Any() &&
                    !view.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == "Calculating file sizes…")) break;
            }
            window.UpdateLayout();
            Assert.NotEmpty(view.GetVisualDescendants().OfType<ProgressBar>());
            var categories = view.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is DaisyOS.System.Storage.StorageUsageCategory).ToArray();
            Assert.NotEmpty(categories);
            Assert.All(categories, button =>
            {
                Assert.Contains("Subtle", button.Classes);
                Assert.Equal(actionPadding, button.Padding);
                Assert.Equal(actionMinHeight, button.MinHeight);
                Assert.Equal(actionRadius, button.CornerRadius);
                Assert.Equal(actionHeight, button.Bounds.Height);
            });
            Assert.DoesNotContain(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Calculating file sizes…");
            await Task.Delay(300);
            using var frame = window.CaptureRenderedFrame();
            frame!.Save($"/tmp/daisy-storage-complete-{dark}.png");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task StorageActionsNavigateFoldersAndReturnWithoutLosingTheirCategory()
    {
        var root = Directory.CreateTempSubdirectory("daisy-storage-ui-").FullName;
        var folder = Directory.CreateDirectory(Path.Combine(root, "Largest folder")).FullName;
        await File.WriteAllBytesAsync(Path.Combine(folder, "large.dat"), new byte[262144]);
        await File.WriteAllBytesAsync(Path.Combine(root, "small.dat"), new byte[8192]);
        var window = new SettingsWindow { Width = 1000, Height = 800 };
        window.Show();
        var view = window.FindControl<SettingsView>("SettingsContent")!;
        async Task<Button[]> Entries()
        {
            for (var attempt = 0; attempt < 400; attempt++)
            {
                await Task.Delay(25);
                Dispatcher.UIThread.RunJobs();
                var buttons = view.GetVisualDescendants().OfType<Button>().Where(button => button.Tag is DaisyOS.System.Storage.StorageFileUsage).ToArray();
                if (buttons.Length > 0) { window.UpdateLayout(); return buttons; }
            }
            throw new TimeoutException("Storage detail did not load");
        }
        try
        {
            window.SelectCategory("Storage");
            view.ShowStorageCategory(new("Documents", 300000, [root]));
            var entries = await Entries();
            Assert.Equal(2, entries.Length);
            Assert.Equal(folder, ((DaisyOS.System.Storage.StorageFileUsage)entries[0].Tag!).Path);
            Assert.All(entries, button => Assert.Contains("Subtle", button.Classes));
            Assert.Contains(entries[1].GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "8.2 KB");
            entries[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            var child = Assert.Single(await Entries());
            Assert.Equal(Path.Combine(folder, "large.dat"), ((DaisyOS.System.Storage.StorageFileUsage)child.Tag!).Path);
            var back = view.GetVisualDescendants().OfType<Button>().Single(button => button.Classes.Contains("IconButton") && button.Content is DaisyOS.Shell.Controls.DaisyIcon icon && icon.Icon == "caret-left");
            back.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(2, (await Entries()).Length);
            await Task.Delay(300);
            using (var frame = window.CaptureRenderedFrame()) frame!.Save("/tmp/daisy-storage-browser.png");
            view.ShowStorageCategory(new("Installed apps", 0));
            Assert.Contains(view.GetVisualDescendants().OfType<TextBlock>(), text => text.Text == "Installed apps");
        }
        finally { window.Close(); Directory.Delete(root, true); }
    }

    [AvaloniaFact]
    public void LongDeviceNamesSizeTheirPickerAndStayInsideNarrowRows()
    {
        const string device = "Scarlett Solo (3rd Gen.) Analog Stereo";
        var window = new SettingsWindow { Width = 1280, Height = 720 };
        window.Show();
        try
        {
            window.SelectCategory("Notifications");
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            var builder = typeof(SettingsView).GetMethods(global::System.Reflection.BindingFlags.NonPublic | global::System.Reflection.BindingFlags.Instance)
                .Single(method => method.Name == "Picker" && method.GetParameters().Length == 5);
            var row = (Border)builder.Invoke(view, new object?[] { "Output device", "sound.output-device", device, null, new[] { device } })!;
            var page = view.FindControl<StackPanel>("CategoryContent")!;
            page.Children.Add(row);
            var picker = row.GetVisualDescendants().OfType<ComboBox>().Single();
            foreach (var width in new[] { 1280, 760, 1280 })
            {
                window.Width = width;
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Dispatcher.UIThread.RunJobs();
                window.UpdateLayout();
                Assert.True(picker.Bounds.Width > 200);
                Assert.Equal(device, ToolTip.GetTip(picker));
                var point = picker.TranslatePoint(default, row)!.Value;
                Assert.True(point.X + picker.Bounds.Width <= row.Bounds.Width - row.Padding.Right + 1);
            }
        }
        finally { window.Close(); }
    }

}
