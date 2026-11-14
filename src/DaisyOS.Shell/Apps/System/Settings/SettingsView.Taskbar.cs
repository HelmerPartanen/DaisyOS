using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private void BuildTaskbar()
    {
        var app = Application.Current as App;
        var preferences = app?.CurrentTaskbarPreferences ?? new TaskbarPreferences();
        async Task Apply(TaskbarPreferences value)
        {
            preferences = value.Normalize();
            if (app is not null) await app.SetTaskbarPreferencesAsync(preferences);
        }
        Border Switch(string title, string description, bool value, Func<bool, Task> apply, string? name = null)
        {
            var toggle = new ToggleSwitch { Name = name, IsChecked = value, VerticalAlignment = VerticalAlignment.Center };
            Avalonia.Automation.AutomationProperties.SetName(toggle, Loc.Get(title));
            toggle.Click += async (_, _) => await apply(toggle.IsChecked == true);
            return PreferenceRow(Loc.Get(title), Loc.Get(description), toggle);
        }
        var size = new QuickSettingsSlider
        {
            Name = "TaskbarSizeSlider", Minimum = 0, Maximum = 2, StepFrequency = 1,
            Value = app?.CurrentTaskbarSize ?? 1, Width = 220, ShowTicks = true, ShowIcon = false
        };
        Avalonia.Automation.AutomationProperties.SetName(size, Loc.Get("Taskbar size"));
        size.ValueChanged += async (_, _) =>
        {
            if (app is not null && (int)Math.Round(size.Value) != app.CurrentTaskbarSize)
                await app.SetTaskbarSizeAsync((int)Math.Round(size.Value));
        };
        AddSection(Loc.Get("LAYOUT"), Loc.Get("All three components follow the selected screen edge. Side panels keep icons and text upright."),
            Picker("Taskbar position", "taskbar.edge", preferences.Edge.ToString(), value => _ = Apply(preferences with { Edge = Enum.Parse<DockEdge>(value) }), "Bottom", "Left", "Top", "Right"),
            Picker("App dock alignment", "taskbar.alignment", preferences.Alignment.ToString(), value => _ = Apply(preferences with { Alignment = Enum.Parse<DockAlignment>(value) }), "Start", "Center", "End"),
            PreferenceRow(Loc.Get("Taskbar size"), Loc.Get("Small, default or large app icons."), size));
        AddSection(Loc.Get("AUTO-HIDE"), null,
            Switch("Auto-hide taskbar components", "Reveal each component by hovering over its position on the selected edge.", app?.TaskbarAutoHideEnabled ?? false,
                async enabled => { if (app is not null) await app.SetBottomComponentsAutoHideAsync(enabled); }, "TaskbarAutoHideToggle"),
            Picker("Hide delay", "taskbar.hide-delay", preferences.HideDelayMilliseconds switch { 500 => "Short", 2000 => "Long", 4000 => "Very long", _ => "Normal" },
                value => _ = Apply(preferences with { HideDelayMilliseconds = value switch { "Short" => 500, "Long" => 2000, "Very long" => 4000, _ => 1100 } }), "Short", "Normal", "Long", "Very long"));
        AddSection(Loc.Get("COMPONENTS"), null,
            Switch("Show system bar", "Keep quick settings, notifications and the clock on the taskbar edge.", preferences.ShowSystemBar, value => Apply(preferences with { ShowSystemBar = value })),
            Switch("Show media controls", "Show playback controls alongside the app dock.", app?.TaskbarMediaVisible ?? true,
                async value => { if (app is not null) await app.SetTaskbarMediaVisibleAsync(value); }),
            Switch("Show launcher button", "The launcher is always available with the Super key.", preferences.ShowLauncherButton, value => Apply(preferences with { ShowLauncherButton = value })),
            Switch("Show date with clock", "Place the date beside the clock or above it on side panels.", app?.SessionState.ShowDateInSystemBar ?? false,
                value => { if (app is not null) app.SessionState.ShowDateInSystemBar = value; return Task.CompletedTask; }));
        AddSection(Loc.Get("APPLICATIONS"), Loc.Get("Drag app icons to reorder them. Right-click an app to pin or unpin it."),
            Switch("Lock app icon order", "Prevent accidental rearranging while keeping app buttons usable.", preferences.LockIconOrder, value => Apply(preferences with { LockIconOrder = value })),
            Switch("Show running indicators", "Mark open, active and minimized applications.", preferences.ShowWindowIndicators, value => Apply(preferences with { ShowWindowIndicators = value })),
            Switch("Show window previews", "Preview open windows when hovering over an app.", preferences.ShowWindowPreviews, value => Apply(preferences with { ShowWindowPreviews = value })),
            Switch("Show tooltips", "Show labels for taskbar, system bar and media controls.", preferences.ShowTooltips, value => Apply(preferences with { ShowTooltips = value })));
        var reset = new Button { Content = Loc.Get("Reset taskbar"), Classes = { "Secondary", "Small" } };
        reset.Click += async (_, _) =>
        {
            if (app is null) return;
            reset.IsEnabled = false;
            try
            {
                await app.SetTaskbarPreferencesAsync(new());
                await app.SetTaskbarSizeAsync(1);
                await app.SetBottomComponentsAutoHideAsync(false);
                await app.SetTaskbarMediaVisibleAsync(true);
                SelectCategory("Taskbar");
            }
            finally { reset.IsEnabled = true; }
        };
        AddSection(Loc.Get("RESET"), null,
            PreferenceRow(Loc.Get("Restore taskbar defaults"), Loc.Get("Restore the bottom position, default size and visible components. Pinned apps and their order are kept."), reset));
    }
}
