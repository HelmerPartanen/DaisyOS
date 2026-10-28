using System;

namespace DaisyOS.Shell.Services;

/// <summary>Where a launcher request originated.</summary>
public enum LauncherRequestSource
{
    /// <summary>The bare Meta shortcut, which may also be used by a running game.</summary>
    Meta,

    /// <summary>A deliberate request such as Alt+Space, the taskbar, or the session menu.</summary>
    Explicit
}

/// <summary>How the shell should react to a launcher request.</summary>
public enum LauncherGestureDecision
{
    Open,
    Close,
    Arm
}

/// <summary>
/// Keeps the bare Meta shortcut from tearing down a fullscreen game on its
/// first press. The first Meta press over a fullscreen client only arms a
/// short confirmation window and shows a passive hint; a second press within
/// that window opens the launcher. Deliberate entry points always open.
/// </summary>
public sealed class LauncherGesturePolicy
{
    /// <summary>How long the confirmation hint remains valid.</summary>
    public static readonly TimeSpan ConfirmationWindow = TimeSpan.FromMilliseconds(900);

    private DateTime? _armedAt;

    public bool IsArmed => _armedAt is not null;

    public LauncherGestureDecision Decide(
        LauncherRequestSource source,
        bool isLauncherOpen,
        bool isFullscreenAppActive,
        DateTime now)
    {
        if (isLauncherOpen)
        {
            Reset();
            return LauncherGestureDecision.Close;
        }

        if (source == LauncherRequestSource.Explicit || !isFullscreenAppActive)
        {
            Reset();
            return LauncherGestureDecision.Open;
        }

        if (_armedAt is { } armed)
        {
            var elapsed = now - armed;
            if (elapsed >= TimeSpan.Zero && elapsed <= ConfirmationWindow)
            {
                Reset();
                return LauncherGestureDecision.Open;
            }
        }

        _armedAt = now;
        return LauncherGestureDecision.Arm;
    }

    public void Reset() => _armedAt = null;
}
