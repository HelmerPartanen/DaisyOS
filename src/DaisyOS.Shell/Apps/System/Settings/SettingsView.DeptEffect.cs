using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Media;
using DaisyOS.Shell.Views.Windows;

namespace DaisyOS.Shell.Apps.System.Settings;

public partial class SettingsView
{
    private bool _deptPreviewInitialized;
    private CancellationTokenSource? _deptSaveCancellation;
    private readonly Avalonia.Threading.DispatcherTimer _deptPreviewTimer = new() { Interval = TimeSpan.FromSeconds(1) };

    private void RefreshDeptPreviewClock()
    {
        var twelveHour = string.Equals((Application.Current as App)?.GetPreference("time.clock-format", "24-hour"), "12-hour", StringComparison.OrdinalIgnoreCase);
        DeptPreviewClock.Text = _lockPreferences.FormatTime(DateTime.Now, twelveHour);
        DeptPreviewDate.Text = _lockPreferences.FormatDate(DateTime.Now);
    }

    private void InitializeDeptPreview()
    {
        if (!_deptPreviewInitialized)
        {
            _deptPreviewInitialized = true;
            RefreshDeptPreviewClock();
            _deptPreviewTimer.Tick += (_, _) => RefreshDeptPreviewClock();
            AttachedToVisualTree += (_, _) => _deptPreviewTimer.Start();
            DetachedFromVisualTree += (_, _) => _deptPreviewTimer.Stop();
            _deptPreviewTimer.Start();
            DeptPreviewForeground.Configure(DeptPreviewWallpaper, DeptPreviewClock, DeptPreviewClockArea,
                () => LockScreenWindow.RasterizeClockGlyphBands(DeptPreviewClock.Text ?? "9:41",
                    new Typeface(DeptPreviewClock.FontFamily, DeptPreviewClock.FontStyle, DeptPreviewClock.FontWeight), DeptPreviewClock.FontSize));
            WallpaperPreviewImage.PropertyChanged += (_, e) =>
            {
                if (e.Property == Image.SourceProperty) RefreshDeptPreviewSource();
            };
        }
        RefreshDeptPreviewSource();
        DeptPreviewForeground.SetPreferences(DeptEffectToggle.IsChecked == true && _wallpaperSelection == WallpaperSelection.Picture && (Application.Current as App)?.HighContrastEnabled != true,
            DeptAutomaticToggle.IsChecked == true);
    }

    private void RefreshDeptPreviewSource()
    {
        DeptPreviewWallpaper.Source = _wallpaperSelection == WallpaperSelection.Solid ? null : WallpaperPreviewImage.Source;
        DeptPreviewGrid.Background = _wallpaperSelection == WallpaperSelection.Solid ? SolidWallpaperPreview.Background : Brushes.Black;
        DeptPreviewForeground.SetPreferences(DeptEffectToggle.IsChecked == true && _wallpaperSelection == WallpaperSelection.Picture && (Application.Current as App)?.HighContrastEnabled != true,
            DeptAutomaticToggle.IsChecked == true);
    }

    private async void OnDeptEffectChanged(object? sender, RoutedEventArgs e) => await SaveDeptEffectAsync();

    private async Task SaveDeptEffectAsync()
    {
        if (_isRefreshingAppearanceControls || !_deptPreviewInitialized) return;
        DeptPreviewForeground.SetPreferences(DeptEffectToggle.IsChecked == true && _wallpaperSelection == WallpaperSelection.Picture && (Application.Current as App)?.HighContrastEnabled != true,
            DeptAutomaticToggle.IsChecked == true);
        DeptEffectOptions.IsEnabled = DeptEffectToggle.IsChecked == true;
        if (Application.Current is not App app) return;
        _deptSaveCancellation?.Cancel();
        _deptSaveCancellation?.Dispose();
        _deptSaveCancellation = new CancellationTokenSource();
        var token = _deptSaveCancellation.Token;
        try
        {
            await Task.Delay(150, token);
            _lockWritesInProgress++;
            try
            {
                await app.SetDeptEffectAsync(DeptEffectToggle.IsChecked == true,
                    DeptAutomaticToggle.IsChecked == true);
            }
            finally { _lockWritesInProgress--; }
        }
        catch (OperationCanceledException) { }

    }
}
