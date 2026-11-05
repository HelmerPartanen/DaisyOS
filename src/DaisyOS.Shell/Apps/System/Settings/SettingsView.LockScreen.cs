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
    private string[]? _cachedLockFontFamilies;
    private ComboBox? _lockFontPicker;
    private ComboBox? _lockWeightPicker;
    private QuickSettingsSlider? _lockClockSizeSlider;
    private TextBlock? _lockClockSizeReadout;
    private ComboBox? _lockColorPicker;
    private ComboBox? _lockPositionPicker;
    private ComboBox? _lockTimeFormatPicker;
    private ComboBox? _lockDateStylePicker;
    private ToggleSwitch? _lockDateBelowClockToggle;
    private ComboBox? _lockAlignmentPicker;
    private QuickSettingsSlider? _lockDimmingSlider;
    private TextBlock? _lockDimmingReadout;
    private ToggleSwitch? _lockShowBatteryToggle;
    private ToggleSwitch? _lockShowMediaToggle;

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
        if (LockClockOptions.Children.Count == 0 || LockMoreOptions.Children.Count == 0)
            BuildLockScreenOptions();
        else
            UpdateLockScreenOptionValues();
        _isRefreshingAppearanceControls = false;
        if (app is not null && !app.UsesSolidWallpaper) RefreshWallpaperPreview(app.CurrentWallpaperUri);
        ApplyLockPreviewStyle();
        if (_pendingSettingTitle is { } target)
        {
            _pendingSettingTitle = null;
            Dispatcher.UIThread.Post(() =>
            {
                Control control = target == "Depth effect" ? DeptEffectToggle : target == "Date & time" ? LockMoreOptions : LockClockOptions;
                control.BringIntoView();
                (control.GetVisualDescendants().OfType<Control>().FirstOrDefault(c => c.Focusable) ?? control).Focus(NavigationMethod.Directional);
            }, DispatcherPriority.Render);
        }
    }

    private void UpdateLockScreenOptionValues()
    {
        var fontChoice = string.IsNullOrEmpty(_lockPreferences.FontFamily) ? "System font" : _lockPreferences.FontFamily;
        if (_lockFontPicker is not null && !string.IsNullOrEmpty(_lockPreferences.FontFamily) &&
            !_lockFontPicker.Items.OfType<ComboBoxItem>().Any(item => Equals(item.Tag, fontChoice)))
        {
            var customItem = new ComboBoxItem
            {
                Content = Loc.Get(fontChoice),
                Tag = fontChoice,
                FontFamily = Services.Theming.TypographyPolicy.ResolveFontFamily(fontChoice)
            };
            customItem.Classes.Add("ThemeModeOption");
            _lockFontPicker.Items.Add(customItem);
        }

        if (_lockFontPicker is not null) SelectComboByTag(_lockFontPicker, fontChoice);
        if (_lockWeightPicker is not null) SelectComboByTag(_lockWeightPicker, _lockPreferences.Weight);
        if (_lockClockSizeSlider is { IsDragging: false } clockSizeSlider)
        {
            clockSizeSlider.Value = _lockPreferences.ClockSize;
            var text = _lockPreferences.ClockSize + "%";
            if (_lockClockSizeReadout is not null) _lockClockSizeReadout.Text = text;
            ToolTip.SetTip(clockSizeSlider, text);
        }
        if (_lockColorPicker is not null) SelectComboByTag(_lockColorPicker, _lockPreferences.Color);
        if (_lockPositionPicker is not null) SelectComboByTag(_lockPositionPicker, _lockPreferences.Position);
        if (_lockTimeFormatPicker is not null) SelectComboByTag(_lockTimeFormatPicker, _lockPreferences.TimeFormat);
        if (_lockDateStylePicker is not null) SelectComboByTag(_lockDateStylePicker, _lockPreferences.DateStyle);
        if (_lockDateBelowClockToggle is not null) _lockDateBelowClockToggle.IsChecked = _lockPreferences.DateBelowClock;
        if (_lockAlignmentPicker is not null) SelectComboByTag(_lockAlignmentPicker, _lockPreferences.Alignment);
        if (_lockDimmingSlider is { IsDragging: false } dimmingSlider)
        {
            dimmingSlider.Value = _lockPreferences.WallpaperDimming;
            var text = _lockPreferences.WallpaperDimming + "%";
            if (_lockDimmingReadout is not null) _lockDimmingReadout.Text = text;
            ToolTip.SetTip(dimmingSlider, text);
        }
        if (_lockShowBatteryToggle is not null) _lockShowBatteryToggle.IsChecked = _lockPreferences.ShowBattery;
        if (_lockShowMediaToggle is not null) _lockShowMediaToggle.IsChecked = _lockPreferences.ShowMedia;
    }

    private void BuildLockScreenOptions()
    {
        LockClockOptions.Children.Clear();
        LockMoreOptions.Children.Clear();
        Border Choice(string label, string value, Action<string> set, out ComboBox? createdPicker, params string[] choices)
        {
            var row = Picker(label, "lock-screen." + label, value,
                selected => { if (_isRefreshingAppearanceControls) return; set(selected); _ = SaveLockStyleAsync(); }, choices);
            var picker = row.GetVisualDescendants().OfType<ComboBox>().FirstOrDefault();
            createdPicker = picker;
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
        Border Switch(string label, bool value, Action<bool> set, out ToggleSwitch? createdToggle)
        {
            var row = Toggle(label, "lock-screen." + label, value,
                selected => { if (_isRefreshingAppearanceControls) return; set(selected); _ = SaveLockStyleAsync(); });
            createdToggle = row.GetVisualDescendants().OfType<ToggleSwitch>().FirstOrDefault();
            return row;
        }
        _cachedLockFontFamilies ??= new[] { "System font" }.Concat(new[] { "Inter", "Atkinson Hyperlegible", "Noto Sans", "Noto Serif" }
            .Concat(FontManager.Current.SystemFonts.Select(f => f.Name)).Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.CurrentCultureIgnoreCase).OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase)).ToArray();
        var families = string.IsNullOrWhiteSpace(_lockPreferences.FontFamily) ||
                       _cachedLockFontFamilies.Contains(_lockPreferences.FontFamily, StringComparer.CurrentCultureIgnoreCase)
            ? _cachedLockFontFamilies
            : [.. _cachedLockFontFamilies, _lockPreferences.FontFamily];
        var font = Choice("Clock font", string.IsNullOrEmpty(_lockPreferences.FontFamily) ? "System font" : _lockPreferences.FontFamily,
            value => _lockPreferences.FontFamily = value == "System font" ? "" : value, out _lockFontPicker, families);
        LockClockOptions.Children.Add(CreateSection(Loc.Get("Clock"), null,
            font,
            Choice("Font weight", _lockPreferences.Weight, v => _lockPreferences.Weight = v, out _lockWeightPicker, "Light", "Regular", "Medium", "Bold", "Black"),
            LockSlider("Clock size", _lockPreferences.ClockSize, 70, 140, value => _lockPreferences.ClockSize = value, out _lockClockSizeSlider, out _lockClockSizeReadout),
            Choice("Clock color", _lockPreferences.Color, v => _lockPreferences.Color = v, out _lockColorPicker, "Default", "White", "Warm", "Rose", "Mint", "Blue"),
            Choice("Clock position", _lockPreferences.Position, v => _lockPreferences.Position = v, out _lockPositionPicker, "Higher", "Balanced", "Lower")));
        LockMoreOptions.Children.Add(CreateSection(Loc.Get("Date & time"), null,
            Choice("Time format", _lockPreferences.TimeFormat, v => _lockPreferences.TimeFormat = v, out _lockTimeFormatPicker, "System", "12-hour", "24-hour"),
            Choice("Date style", _lockPreferences.DateStyle, v => _lockPreferences.DateStyle = v, out _lockDateStylePicker, "Full", "Compact", "Numeric", "Hidden"),
            Switch("Date below clock", _lockPreferences.DateBelowClock, v => _lockPreferences.DateBelowClock = v, out _lockDateBelowClockToggle)));
        LockMoreOptions.Children.Add(CreateSection(Loc.Get("Layout & visibility"), null,
            Choice("Clock alignment", _lockPreferences.Alignment, v => _lockPreferences.Alignment = v, out _lockAlignmentPicker, "Left", "Center", "Right"),
            LockSlider("Wallpaper dimming", _lockPreferences.WallpaperDimming, 0, 70, value => _lockPreferences.WallpaperDimming = value, out _lockDimmingSlider, out _lockDimmingReadout),
            Switch("Show battery status", _lockPreferences.ShowBattery, v => _lockPreferences.ShowBattery = v, out _lockShowBatteryToggle),
            Switch("Show media controls", _lockPreferences.ShowMedia, v => _lockPreferences.ShowMedia = v, out _lockShowMediaToggle)));
    }

    private Border LockSlider(string label, int value, int minimum, int maximum, Action<int> change, out QuickSettingsSlider createdSlider, out TextBlock createdReadout)
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
        createdSlider = slider;
        createdReadout = readout;
        int lastRounded = value;
        slider.ValueChanged += (_, _) =>
        {
            var rounded = (int)Math.Round(slider.Value);
            readout.Text = rounded + "%";
            ToolTip.SetTip(slider, readout.Text);
            if (_isRefreshingAppearanceControls || rounded == lastRounded) return;
            lastRounded = rounded;
            change(rounded);
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
