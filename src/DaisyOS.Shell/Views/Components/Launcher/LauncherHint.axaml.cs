using System;
using Avalonia.Controls;
using Avalonia.Threading;

namespace DaisyOS.Shell.Views.Components.Launcher;

/// <summary>
/// Passive confirmation hint shown on the first Meta press over a fullscreen
/// client. It lives on a keyboard-less, input-transparent layer surface so the
/// game keeps both pointer and keyboard focus while the user decides.
/// </summary>
public partial class LauncherHint : UserControl
{
    private readonly DispatcherTimer _hideTimer = new() { Interval = TimeSpan.FromMilliseconds(160) };
    private int _generation;

    public LauncherHint()
    {
        InitializeComponent();
        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            if (Opacity <= 0.01) IsVisible = false;
        };
    }

    public void ShowHint()
    {
        _hideTimer.Stop();
        var generation = ++_generation;
        IsVisible = true;
        Opacity = 0;
        Dispatcher.UIThread.Post(() =>
        {
            if (IsVisible && generation == _generation) Opacity = 1;
        }, DispatcherPriority.Render);
    }

    public void Dismiss()
    {
        _hideTimer.Stop();
        _generation++;
        if (!IsVisible) return;
        Opacity = 0;
        _hideTimer.Start();
    }

    /// <summary>Removes the hint without waiting for the fade.</summary>
    public void HideImmediately()
    {
        _hideTimer.Stop();
        _generation++;
        Opacity = 0;
        IsVisible = false;
    }
}
