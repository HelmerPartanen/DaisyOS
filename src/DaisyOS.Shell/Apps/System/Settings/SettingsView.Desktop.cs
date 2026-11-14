using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Desktop;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private void BuildDesktop()
    {
        var app = Application.Current as App;
        string Get(string key, string fallback) => app?.GetPreference(key, fallback) ?? fallback;
        var preferences = DesktopPreferences.Read(Get);
        void Save(string key, string value) => app?.SetPreference("desktop." + key, value);
        Border Switch(string title, string description, string key, bool value)
        {
            var toggle = new ToggleSwitch { IsChecked = value, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(toggle, Loc.Get(title));
            toggle.Click += (_, _) => Save(key, (toggle.IsChecked == true).ToString());
            return PreferenceRow(Loc.Get(title), Loc.Get(description), toggle);
        }
        AddSection(Loc.Get("LAYOUT"), Loc.Get("Your positions are saved automatically. Widgets only displace items they overlap."),
            Switch("Auto-arrange items", "Keep new items arranged in the next available cells.", "auto-arrange", preferences.AutoArrange),
            Picker("Item alignment", "desktop.alignment", preferences.Alignment.ToString(), value => Save("alignment", value), "Left", "Right", "Top", "Bottom"),
            Switch("Fill gaps after removal", "Move remaining icons forward when an item is removed. Widgets stay in place.", "fill-gaps", preferences.FillGaps),
            Switch("Lock desktop layout", "Prevent accidental dragging and resizing while keeping items usable.", "lock-layout", preferences.LockLayout));
        AddSection(Loc.Get("DESKTOP ITEMS"), null,
            Switch("Show desktop items", "Show application shortcuts, files and folders.", "show-items", preferences.ShowItems),
            Switch("Show Trash", "Show the Trash shortcut on the desktop.", "show-trash", preferences.ShowTrash),
            Switch("Show desktop widgets", "Hide widgets without removing them or forgetting their positions.", "show-widgets", preferences.ShowWidgets),
            Picker("Icon size", "desktop.icon-size", preferences.IconSize.ToString(), value => Save("icon-size", value), "32", "48", "64"));
        AddSection(Loc.Get("INTERACTION"), null,
            Switch("Single-click to open", "Open files and applications with one click instead of two. Dragging still moves items.", "single-click", preferences.SingleClick));
        var arrange = new Button { Content = Loc.Get("Arrange now"), Classes = { "Secondary", "Small" } };
        arrange.Click += (_, _) => Save("arrange-request", Guid.NewGuid().ToString("N"));
        var sort = new ComboBox { PlaceholderText = Loc.Get("Choose order"), MinWidth = 200, Classes = { "ThemeModePicker" } };
        foreach (var (mode, label) in new[] { ("Name", "Name"), ("Type", "Type"), ("Size", "Size"), ("DateModified", "Date modified") })
            sort.Items.Add(new ComboBoxItem { Tag = mode, Content = Loc.Get(label) });
        sort.SelectionChanged += (_, _) =>
        {
            if (sort.SelectedItem is not ComboBoxItem { Tag: string mode }) return;
            Save("sort-request", mode + ":" + Guid.NewGuid().ToString("N"));
            sort.SelectedIndex = -1;
        };
        AddSection(Loc.Get("ARRANGE"), Loc.Get("Rearranging uses your selected alignment and leaves widgets in their current cells."),
            PreferenceRow(Loc.Get("Arrange items now"), Loc.Get("Close gaps without changing the order of your items."), arrange),
            PreferenceRow(Loc.Get("Sort items"), Loc.Get("Arrange icons by name, type, size or modification date."), sort));
        var wallpaper = new Button { Content = Loc.Get("Change wallpaper"), Classes = { "Secondary", "Small" } };
        wallpaper.Click += (_, _) => Navigate(new("Appearance", Setting: "Wallpaper"));
        AddSection(Loc.Get("PERSONALIZATION"), null,
            PreferenceRow(Loc.Get("Wallpaper"), Loc.Get("Choose a desktop picture, colour or video."), wallpaper));
    }
}
