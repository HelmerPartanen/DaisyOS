using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class DropdownSearchTests
{
    [AvaloniaFact]
    public void FontMenuFiltersWithoutChangingSelectionAndSupportsKeyboardChoice()
    {
        var window = new DaisyOS.Shell.Apps.System.Settings.SettingsWindow { Width = 1000, Height = 800 };
        window.Show();
        try
        {
            window.SelectCategory("Appearance");
            var view = window.FindControl<DaisyOS.Shell.Apps.System.Settings.SettingsView>("SettingsContent")!;
            var picker = view.FindControl<ComboBox>("SystemFontPicker")!;
            var search = view.FindControl<SearchBar>("FontSearchBar")!;
            picker.Items.Clear();
            foreach (var family in new[] { "Inter", "Adwaita Sans", "Adwaita Mono" })
                picker.Items.Add(new ComboBoxItem { Content = family, Tag = family });
            picker.SelectedIndex = 0;
            picker.IsDropDownOpen = true;
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            search.Text = "mono";
            Assert.Equal(0, picker.SelectedIndex);
            Assert.Single(picker.Items.OfType<ComboBoxItem>(), item => item.IsVisible);
            search.FocusInput();
            window.KeyPress(Avalonia.Input.Key.Enter, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Enter, null);
            Assert.Equal(2, picker.SelectedIndex);
            Assert.False(picker.IsDropDownOpen);
            picker.IsDropDownOpen = true;
            window.UpdateLayout();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Assert.Equal(string.Empty, search.Text);
            Assert.All(picker.Items.OfType<ComboBoxItem>(), item => Assert.True(item.IsVisible));
            search.FocusInput();
            window.KeyPress(Avalonia.Input.Key.Escape, Avalonia.Input.RawInputModifiers.None, Avalonia.Input.PhysicalKey.Escape, null);
            Assert.False(picker.IsDropDownOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void SearchHeaderIsInsidePopupAndKeepsSelectedFont(bool dark)
    {
        var search = new SearchBar { PlaceholderText = "Search installed fonts", Margin = new Avalonia.Thickness(6) };
        var picker = new ComboBox { Width = 320, ItemsSource = new[] { "Inter", "Adwaita Sans", "Adwaita Mono" }, SelectedIndex = 0 };
        DropdownSearch.SetHeader(picker, search);
        var window = new Window { Content = picker, Width = 400, Height = 200, RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            picker.IsDropDownOpen = true;
            window.UpdateLayout();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.True(search.Bounds.Width > 0);
            Assert.Contains(search.GetVisualAncestors(), ancestor => ancestor is Border { Name: "MenuBlurHost" });
            Directory.CreateDirectory("/tmp/font-dropdown");
            using (var frame = window.CaptureRenderedFrame()) frame!.Save($"/tmp/font-dropdown/{dark}.png");
            search.FocusInput();
            Assert.True(search.FindControl<TextBox>("SearchInput")!.IsFocused);
            search.Text = "Adwaita";
            Assert.Equal("Inter", picker.SelectedItem);
            picker.IsDropDownOpen = false;
            Assert.False(picker.IsDropDownOpen);
        }
        finally { window.Close(); }
    }
}
