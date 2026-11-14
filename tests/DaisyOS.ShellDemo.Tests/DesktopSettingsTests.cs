using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Apps.System.Settings;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Desktop;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DesktopSettingsTests
{
    [Fact]
    public void InvalidDesktopPreferencesUseSafeDefaults()
    {
        var preferences = DesktopPreferences.Read((_, _) => "invalid");
        Assert.False(preferences.AutoArrange);
        Assert.True(preferences.FillGaps);
        Assert.True(preferences.ShowItems);
        Assert.False(preferences.LockLayout);
        Assert.Equal(48, preferences.IconSize);
        Assert.Equal(DaisyOS.Core.Desktop.DesktopAlignment.Left, preferences.Alignment);
    }

    [AvaloniaTheory]
    [InlineData(false, 760)]
    [InlineData(true, 760)]
    [InlineData(false, 1000)]
    [InlineData(true, 1000)]
    public void DesktopPageAndBottomVisibilityUseSharedSettingsControls(bool dark, int width)
    {
        var window = new SettingsWindow { Width = width, Height = 900, RequestedThemeVariant = dark ? Avalonia.Styling.ThemeVariant.Dark : Avalonia.Styling.ThemeVariant.Light };
        window.Show();
        try
        {
            window.SelectCategory("desktop");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            var view = window.FindControl<SettingsView>("SettingsContent")!;
            Assert.True(view.FindControl<Button>("NavDesktop")!.Classes.Contains("Active"));
            var content = view.FindControl<StackPanel>("CategoryContent")!;
            Assert.Equal(7, content.GetVisualDescendants().OfType<ToggleSwitch>().Count());
            var pickers = content.GetVisualDescendants().OfType<ComboBox>().ToArray();
            Assert.Equal(3, pickers.Length);
            foreach (var picker in pickers)
            {
                var point = picker.TranslatePoint(default, content);
                Assert.NotNull(point);
                Assert.True(point.Value.X >= 0 && point.Value.X + picker.Bounds.Width <= content.Bounds.Width + 1);
            }
            Assert.Contains(pickers, picker => picker.Items.OfType<ComboBoxItem>().Select(item => item.Tag).SequenceEqual(new[] { "Left", "Right", "Top", "Bottom" }));
            Assert.Contains(content.GetVisualDescendants().OfType<Button>(), button => button.Content as string == "Arrange now");
            using var frame = window.CaptureRenderedFrame();
            frame!.Save("/tmp/daisy-desktop-settings.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            frame.Save($"/tmp/daisy-desktop-settings-{dark}-{width}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            var battery = view.FindControl<Button>("NavBattery")!;
            Assert.Single(battery.GetVisualDescendants().OfType<DaisyIcon>());
            Assert.Empty(battery.GetVisualDescendants().OfType<BatteryLevelIcon>());
            window.SelectCategory("Appearance");
            Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.Null(view.FindControl<ToggleSwitch>("BottomComponentsAutoHideToggle"));
            window.SelectCategory("Taskbar"); Dispatcher.UIThread.RunJobs(); window.UpdateLayout();
            Assert.True(view.GetVisualDescendants().OfType<ToggleSwitch>().Single(x => x.Name == "TaskbarAutoHideToggle").IsEffectivelyVisible);
            Assert.Null(view.FindControl<ComboBox>("TaskbarVisibilityPicker"));
            Assert.Null(view.FindControl<ComboBox>("SystemBarVisibilityPicker"));
            Assert.Null(view.FindControl<ComboBox>("MediaWidgetVisibilityPicker"));
        }
        finally { window.Close(); }
    }
}
