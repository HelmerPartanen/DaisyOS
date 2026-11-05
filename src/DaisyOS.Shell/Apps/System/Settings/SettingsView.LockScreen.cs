using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Localization;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private LockScreenAppearance _lockPreferences = new();
    private CancellationTokenSource? _lockStyleSave;
    private int _lockWritesInProgress;

    private void RefreshLockScreenControls()
    {
        if (_lockWritesInProgress > 0) return;
        var app = Application.Current as App;
        _lockPreferences = app?.CurrentLockScreenAppearance ?? new();
        _isRefreshingAppearanceControls = true;
        DeptEffectToggle.IsChecked = app?.DeptEffectEnabled ?? true;
        DeptAutomaticToggle.IsChecked = app?.DeptEffectAutomatic ?? true;
        if (app is not null)
        {
            _wallpaperSelection = app.UsesSolidWallpaper ? WallpaperSelection.Solid :
                Services.Wallpaper.WallpaperFileTypes.IsVideo(app.CurrentWallpaperUri) ? WallpaperSelection.Video : WallpaperSelection.Picture;
            SolidWallpaperPreview.Background = new SolidColorBrush(app.CurrentSolidWallpaperColor);
        }
        InitializeDeptPreview();
        BuildLockScreenOptions();
        _isRefreshingAppearanceControls = false;
        if (app is not null && !app.UsesSolidWallpaper) RefreshWallpaperPreview(app.CurrentWallpaperUri);
        ApplyLockPreviewStyle();
        if (_pendingSettingTitle is { } target)
        {
            _pendingSettingTitle = null;
            if (target == "More options") LockMoreExpander.IsExpanded = true;
            Dispatcher.UIThread.Post(() =>
            {
                Control control = target == "Depth effect" ? DeptEffectToggle : target == "More options" ? LockMoreExpander : LockClockOptions;
                control.BringIntoView();
                (control.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable) ?? control).Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Render);
        }
    }

    private void BuildLockScreenOptions()
    {
        LockClockOptions.Children.Clear();
        LockMoreOptions.Children.Clear();
        Border Choice(string label, string value, Action<string> set, params string[] choices)
        {
            var row = Picker(label, "lock-screen." + label, value,
                selected => { set(selected); _ = SaveLockStyleAsync(); }, choices);
            var picker = row.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
            if (picker is not null)
            {
                picker.Width = 220;
                picker.MinWidth = 0;
                picker.MaxWidth = 220;
                Avalonia.Automation.AutomationProperties.SetName(picker, Loc.Get(label));
                foreach (var item in picker.Items.OfType<ComboBoxItem>())
                {
                    if (item.Tag is not string option) continue;
                    item.Content = Loc.Get(option);
                    if (label == "Clock font" && option != "System font")
                        item.FontFamily = Services.Theming.TypographyPolicy.ResolveFontFamily(option);
                }
            }
            return row;
        }
        Border Switch(string label, bool value, Action<bool> set) =>
            Toggle(label, "lock-screen." + label, value, selected => { set(selected); _ = SaveLockStyleAsync(); });
        var families = new[] { "System font" }.Concat(new[] { _lockPreferences.FontFamily, "Inter", "Atkinson Hyperlegible", "Noto Sans", "Noto Serif" }
            .Concat(FontManager.Current.SystemFonts.Select(f => f.Name)).Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        var font = Choice("Clock font", string.IsNullOrEmpty(_lockPreferences.FontFamily) ? "System font" : _lockPreferences.FontFamily,
            value => _lockPreferences.FontFamily = value == "System font" ? "" : value, families);
        LockClockOptions.Children.Add(CreateSection(Loc.Get("Clock"), null,
            font,
            Choice("Font weight", _lockPreferences.Weight, v => _lockPreferences.Weight = v, "Light", "Regular", "Medium", "Bold", "Black"),
            LockSlider("Clock size", _lockPreferences.ClockSize, 70, 140, value => _lockPreferences.ClockSize = value),
            Choice("Clock color", _lockPreferences.Color, v => _lockPreferences.Color = v, "Default", "White", "Warm", "Rose", "Mint", "Blue"),
            Choice("Clock position", _lockPreferences.Position, v => _lockPreferences.Position = v, "Higher", "Balanced", "Lower")));
        LockMoreOptions.Children.Add(CreateSection(Loc.Get("Date & time"), null,
            Choice("Time format", _lockPreferences.TimeFormat, v => _lockPreferences.TimeFormat = v, "System", "12-hour", "24-hour"),
            Choice("Date style", _lockPreferences.DateStyle, v => _lockPreferences.DateStyle = v, "Full", "Compact", "Numeric", "Hidden"),
            Switch("Date below clock", _lockPreferences.DateBelowClock, v => _lockPreferences.DateBelowClock = v)));
        LockMoreOptions.Children.Add(CreateSection(Loc.Get("Layout & visibility"), null,
            Choice("Clock alignment", _lockPreferences.Alignment, v => _lockPreferences.Alignment = v, "Left", "Center", "Right"),
            LockSlider("Wallpaper dimming", _lockPreferences.WallpaperDimming, 0, 70, value => _lockPreferences.WallpaperDimming = value),
            Switch("Show battery status", _lockPreferences.ShowBattery, v => _lockPreferences.ShowBattery = v),
            Switch("Show media controls", _lockPreferences.ShowMedia, v => _lockPreferences.ShowMedia = v)));
    }

    private Border LockSlider(string label, int value, int minimum, int maximum, Action<int> change)
    {
        var slider = new QuickSettingsSlider
        {
            Minimum = minimum, Maximum = maximum, Value = value, Width = 220,
            StepFrequency = 5, ShowTicks = true, ShowIcon = false,
            VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right
        };
        Avalonia.Automation.AutomationProperties.SetName(slider, Loc.Get(label));
        ToolTip.SetTip(slider, value + "%");
        var row = PreferenceRow(Loc.Get(label), value + "%", slider);
        var copy = ((Grid)row.Child!).Children.OfType<StackPanel>().Single();
        var readout = (TextBlock)copy.Children[1];
        slider.ValueChanged += (_, _) =>
        {
            readout.Text = Math.Round(slider.Value) + "%";
            ToolTip.SetTip(slider, readout.Text);
            change((int)slider.Value);
            _ = SaveLockStyleAsync();
        };
        return row;
    }

    private void ApplyLockPreviewStyle()
    {
        _lockPreferences = _lockPreferences.Normalize();
        _lockPreferences.ApplyClock(DeptPreviewClock, DeptPreviewDate, DeptPreviewClockArea,
            this.TryFindResource("LockScreenMutedBrush", out var brush) && brush is IBrush found ? found : Brushes.White);
        DeptPreviewClock.FontSize = 104 * _lockPreferences.ClockSize / 100d;
        DeptPreviewForeground.ClockTopFraction = _lockPreferences.TopFraction;
        DeptPreviewForeground.ForegroundTintOpacity = _lockPreferences.WallpaperDimming / 100d;
        DeptPreviewTint.Opacity = DeptPreviewForeground.ForegroundTintOpacity;
        RefreshDeptPreviewClock();
        DeptPreviewForeground.RefreshLayout();
        DeptEffectOptions.IsEnabled = DeptEffectToggle.IsChecked == true;
    }

    private async Task SaveLockStyleAsync()
    {
        if (_isRefreshingAppearanceControls) return;
        ApplyLockPreviewStyle();
        _lockStyleSave?.Cancel();
        _lockStyleSave?.Dispose();
        _lockStyleSave = new CancellationTokenSource();
        var token = _lockStyleSave.Token;
        try
        {
            await Task.Delay(200, token);
            if (Application.Current is not App app) return;
            _lockWritesInProgress++;
            try { await app.SetLockScreenAppearanceAsync(_lockPreferences); }
            finally { _lockWritesInProgress--; }
        }
        catch (OperationCanceledException) { }
    }

    private void OnLockWallpaperClicked(object? sender, RoutedEventArgs e)
    {
        _pendingSettingTitle = "Wallpaper";
        SelectCategory("Appearance");
    }

    private async void OnResetLockScreenClicked(object? sender, RoutedEventArgs e)
    {
        _lockStyleSave?.Cancel();
        _deptSaveCancellation?.Cancel();
        _lockPreferences = new();
        if (Application.Current is App app)
        {
            _lockWritesInProgress++;
            try
            {
                await app.SetLockScreenAppearanceAsync(_lockPreferences);
                await app.SetDeptEffectAsync(true, true);
            }
            finally { _lockWritesInProgress--; }
        }
        RefreshLockScreenControls();
    }
}
