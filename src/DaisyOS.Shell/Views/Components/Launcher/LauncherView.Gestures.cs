using Avalonia;
using Avalonia.Controls;
using DaisyOS.Shell.Services.Motion;

namespace DaisyOS.Shell.Views.Components.Launcher;

public partial class LauncherView
{
    private int _revealGeneration;
    private bool _revealInteractive;
    private bool _revealSettling;
    private bool _revealTarget;
    public bool? RevealSettlingTarget => _revealSettling ? _revealTarget : null;
    internal bool IsRevealInProgress => _revealInteractive || _revealSettling;
    private double _revealProgress = 1;

    public double BeginInteractiveReveal(bool opening)
    {
        var start = _revealInteractive || _revealSettling ? _revealProgress : opening ? 0 : 1;
        _revealSettling = false;
        ++_revealGeneration;
        _revealInteractive = true;
        _stageAnimationCts?.Cancel();
        CancelPageDrag();
        ClosePowerMenu();
        CloseAppContextMenu();
        GuiMotion.Cancel(_launcherRoot);
        IsHitTestVisible = false;
        SetRevealProgress(start);
        return start;
    }

    public void UpdateInteractiveReveal(double progress)
    {
        if (_revealInteractive && double.IsFinite(progress)) SetRevealProgress(progress);
    }

    private void SetRevealProgress(double progress)
    {
        _revealProgress = Math.Clamp(progress, 0, 1);
        var content = _launcherRoot;
        content.Opacity = _revealProgress;
        // Only transform the content. The full-output material never changes size.
        var stage = _launcherStage;
        var reduced = Application.Current is App { ReduceMotionEnabled: true };
        ApplyStageTransform(stage, reduced ? 1 : 0.88 + 0.12 * _revealProgress, 0);
    }

    public void EndInteractiveReveal(bool visible, Action completed)
    {
        if (!_revealInteractive) return;
        _revealInteractive = false;
        _revealSettling = true;
        _revealTarget = visible;
        var generation = ++_revealGeneration;
        var from = _revealProgress;
        var target = visible ? 1d : 0d;
        var duration = Math.Clamp(220 * Math.Abs(target - from), 80, 220);
        void Finish()
        {
            if (generation != _revealGeneration) return;
            SetRevealProgress(target);
            _revealSettling = false;
            IsHitTestVisible = visible;
            completed();
        }
        if (ResolveLauncherDuration(visible) == TimeSpan.Zero || TopLevel.GetTopLevel(this) is not { } host)
        {
            Finish();
            return;
        }
        TimeSpan? start = null;
        void Frame(TimeSpan now)
        {
            if (generation != _revealGeneration) return;
            start ??= now;
            var t = Math.Clamp((now - start.Value).TotalMilliseconds / duration, 0, 1);
            SetRevealProgress(from + (target - from) * (1 - Math.Pow(1 - t, 3)));
            if (t >= 1) Finish();
            else host.RequestAnimationFrame(Frame);
        }
        host.RequestAnimationFrame(Frame);
    }

    private void CancelInteractiveReveal()
    {
        ++_revealGeneration;
        _revealInteractive = false;
        _revealSettling = false;
        IsHitTestVisible = true;
    }
}
