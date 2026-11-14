using System;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Compositor;

namespace DaisyOS.Shell.Views.Components.Taskbar;

public partial class TaskbarView
{
    internal bool IsNativePanelHosted { get; set; }
    private CompositorWindowRecord[] _previewWindows = [];
    private Button? _previewButton;
    private DispatcherTimer? _previewDelay;
    private bool _previewShown;
    private string _previewToken = "";

    private void PreviewPointerEntered(object? sender, PointerEventArgs e)
    {
        if (sender is not Button button || _pointerDown || IsContextMenuVisible) return;
        _previewDelay?.Stop();
        _previewButton = button;
        _previewToken = Guid.NewGuid().ToString("N");
        if (!PreviewsEnabled || !PreviewWindows(button).Any() || !TaskbarPreviewState.IsAvailable) return;
        ToolTip.SetIsOpen(button, false);
        ToolTip.SetShowDelay(button, int.MaxValue);
        ToolTip.SetBetweenShowDelay(button, -1);
        if (_previewShown || TaskbarPreviewState.IsShowing) { PublishHoverPreview(); return; }
        _previewDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _previewDelay.Tick -= PreviewDelayElapsed;
        _previewDelay.Tick += PreviewDelayElapsed;
        _previewDelay.Start();
    }

    private void PreviewDelayElapsed(object? sender, EventArgs e)
    {
        _previewDelay?.Stop();
        if (_previewButton?.IsPointerOver == true) PublishHoverPreview();
    }

    private void PreviewPointerExited(object? sender, PointerEventArgs e)
    {
        if (sender != _previewButton) return;
        _previewDelay?.Stop();
        TaskbarPreviewState.Hide();
        if (sender is Button button) RestorePreviewTooltip(button);
        _previewButton = null;
        // Keep quick switching for the short journey between adjacent icons.
        DispatcherTimer.RunOnce(() => { if (_previewButton is null) _previewShown = false; }, TimeSpan.FromMilliseconds(250));
    }

    private CompositorWindowRecord[] PreviewWindows(Button button)
    {
        if (button.Tag is not string tag || !_compositorWindowIds.TryGetValue(tag, out var id)) return [];
        var selected = _previewWindows.FirstOrDefault(w => w.Id == id);
        if (selected is null) return [];
        var app = ResolveEffectiveAppIdForWindow(selected);
        return _previewWindows.Where(w => w.IsOnCurrentWorkspace &&
            string.Equals(ResolveEffectiveAppIdForWindow(w), app, StringComparison.OrdinalIgnoreCase)).ToArray();
    }

    private void RefreshHoverPreview()
    {
        if (_previewShown && _previewButton is not null) PublishHoverPreview();
    }

    private void PublishHoverPreview()
    {
        var button = _previewButton;
        if (!PreviewsEnabled || button is null || !button.IsPointerOver || !TaskbarPreviewState.IsAvailable) { HideHoverPreview(); return; }
        var windows = PreviewWindows(button);
        if (windows.Length == 0) { HideHoverPreview(); return; }
        var top = TopLevel.GetTopLevel(button);
        if (top?.Screens is null) return;
        var origin = button.PointToScreen(default);
        var screen = top.Screens.ScreenFromPoint(origin);
        if (screen is null || screen.Bounds.Width == 0 || screen.Bounds.Height == 0) return;
        var scaling = top!.RenderScaling;
        // Wayland has no global client coordinates. The native panel is anchored
        // to the output bottom; KWin resolves these surface-relative bounds.
        var local = button.TranslatePoint(default, top) ?? default;
        var far = button.TranslatePoint(new Point(button.Bounds.Width, button.Bounds.Height), top) ?? local;
        var renderedWidth = far.X - local.X;
        var renderedHeight = far.Y - local.Y;
        if (IsNativePanelHosted && (top.Bounds.Width <= 0 || top.Bounds.Height <= 0)) return;
        string Color(string key, string fallback) => this.TryFindResource(key, ActualThemeVariant, out var value)
            && value is ISolidColorBrush brush ? brush.Color.ToString() : fallback;
        TaskbarPreviewState.Show(new TaskbarPreviewRequest(_previewToken,
            windows.Select(w => w.Id).ToArray(),
            IsNativePanelHosted ? local.X / top.Bounds.Width : (origin.X - screen.Bounds.X) / (double)screen.Bounds.Width,
            IsNativePanelHosted ? local.Y / top.Bounds.Height : (origin.Y - screen.Bounds.Y) / (double)screen.Bounds.Height,
            IsNativePanelHosted ? renderedWidth / top.Bounds.Width : renderedWidth * scaling / screen.Bounds.Width,
            IsNativePanelHosted ? renderedHeight / top.Bounds.Height : renderedHeight * scaling / screen.Bounds.Height,
            Color("SurfaceBaseSolidBrush", "#ff181818"), Color("SurfaceRaisedSubtleBrush", "#ff252525"),
            Color("ContentPrimaryBrush", "#fff5f5f7"), Color("StrokeDefaultBrush", "#ff444444"),
            Color("StateHoverBrush", "#20ffffff"), Color("ActionDangerHoverBrush", "#ffb8212c"),
            Application.Current is App { ReduceMotionEnabled: true }, IsNativePanelHosted ? top.Bounds.Height : 0,
            Color("ContentOnActionBrush", "#ffffffff"), DockEdge.ToString(), IsNativePanelHosted ? top.Bounds.Width : 0));
        _previewShown = true;
    }

    internal bool HasHoverPreview(Control owner) => owner is Button button &&
        PreviewsEnabled && TaskbarPreviewState.IsAvailable && PreviewWindows(button).Length > 0;

    private static void RestorePreviewTooltip(Button button)
    {
        ToolTip.SetShowDelay(button, int.MaxValue);
        ToolTip.SetBetweenShowDelay(button, -1);
    }

    private void HideHoverPreview()
    {
        _previewDelay?.Stop();
        if (_previewButton is { } button) RestorePreviewTooltip(button);
        _previewButton = null;
        _previewShown = false;
        TaskbarPreviewState.Dismiss();
    }
}
