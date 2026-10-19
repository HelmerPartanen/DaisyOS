using System.Runtime.CompilerServices;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Animation.Easings;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

namespace DaisyOS.Shell.Services.Motion;

/// <summary>Interruptible interior motion. Native geometry and material/blur frames stay fixed.</summary>
public static class GuiMotion
{
    public const double ShellSlideDistance = 82;
    public static TimeSpan ShellSlideDuration(Control content, bool entering) =>
        Application.Current is App { ReduceMotionEnabled: true } ? TimeSpan.Zero :
        content.TryFindResource(entering ? "MotionDurationShellEnter" : "MotionDurationShellExit", out var resource) && resource is TimeSpan duration
            ? duration : TimeSpan.FromMilliseconds(entering ? 220 : 140);

    /// <summary>Moves a bottom-anchored card completely beyond its clipping viewport.</summary>
    public static double BottomViewportSlideDistance(Control content) => Math.Max(
        ShellSlideDistance,
        Math.Ceiling(Math.Max(content.Bounds.Height, content.DesiredSize.Height)));

    private sealed class State
    {
        public CancellationTokenSource? Animation;
        public CancellationTokenSource? Visibility;
        public bool? RequestedVisible;
    }
    private static readonly ConditionalWeakTable<Control, State> States = new();

    private static State GetState(Control control) => States.GetValue(control, static target =>
    {
        var state = new State();
        target.DetachedFromVisualTree += (_, _) =>
        {
            state.Visibility?.Cancel();
            state.Visibility = null;
            state.RequestedVisible = null;
            Cancel(target);
        };
        return state;
    });

    public static bool IsOpen(Control host) => GetState(host).RequestedVisible ?? host.IsVisible;

    public static TimeSpan Duration(Control target, bool entering) =>
        Application.Current is App { ReduceMotionEnabled: true } ? TimeSpan.Zero :
        target.TryFindResource(entering ? "MotionDurationEnter" : "MotionDurationExit", out var resource) && resource is TimeSpan duration
            ? duration : TimeSpan.FromMilliseconds(entering ? 280 : 140);

    public static Task ExitAsync(Control content) => AnimateAsync(content, false, new Point(0, 8));

    public static Task ExitAsync(Control content, Point offset, TimeSpan? durationOverride = null) =>
        AnimateAsync(content, false, offset, durationOverride: durationOverride, preserveExitPosition: true);

    public static Task RevealAsync(Control content, double offset = 8) => AnimateAsync(content, true, new Point(0, offset));

    public static Task RevealAsync(Control content, Point offset, TimeSpan? durationOverride = null) =>
        AnimateAsync(content, true, offset, durationOverride: durationOverride);

    public static Task SetVisibleAsync(
        Control host,
        Control content,
        bool visible,
        Action? changed = null,
        bool immediate = false,
        double offset = 8,
        TimeSpan? durationOverride = null,
        bool fadeOnExit = false,
        bool fadeOnEnter = false,
        Easing? easingOverride = null)
        => SetVisibleAsync(host, content, visible, changed, immediate, new Point(0, offset), durationOverride, fadeOnExit, fadeOnEnter, easingOverride);

    public static async Task SetVisibleAsync(
        Control host,
        Control content,
        bool visible,
        Action? changed,
        bool immediate,
        Point offset,
        TimeSpan? durationOverride = null,
        bool fadeOnExit = false,
        bool fadeOnEnter = false,
        Easing? easingOverride = null)
    {
        var state = GetState(host);
        if (!visible && !host.IsVisible && state.Visibility is null)
        {
            state.RequestedVisible = false;
            host.IsHitTestVisible = false;
            return;
        }
        if (state.RequestedVisible == visible && !immediate) return;
        using var request = new CancellationTokenSource();
        state.Visibility = request;
        state.RequestedVisible = visible;
        host.IsHitTestVisible = visible;
        if (visible) host.IsVisible = true;
        changed?.Invoke(); // Release native input immediately on dismissal.
        // AnimateAsync samples current values before cancelling the old animation.
        // Request identity below protects visibility from stale completions.
        var animation = AnimateAsync(content, visible, offset, immediate, request.Token, durationOverride,
            preserveExitPosition: !visible, fadeOnExit: fadeOnExit, fadeOnEnter: fadeOnEnter, easingOverride: easingOverride);
        await animation;
        if (request.IsCancellationRequested || !ReferenceEquals(state.Visibility, request)) return;
        if (!visible)
        {
            // Keep the content at its off-screen endpoint until the host is
            // hidden. Clearing the transform first can paint one final frame
            // back at the bar's resting position.
            host.IsVisible = false;
            content.Opacity = 1;
            content.RenderTransform = null;
        }
        state.Visibility = null;
        changed?.Invoke();
    }

    public static void SettleActiveAnimations()
    {
        foreach (var entry in States) entry.Value.Animation?.Cancel();
    }

    public static void Cancel(Control content)
    {
        var state = GetState(content);
        state.Animation?.Cancel();
        state.Animation?.Dispose();
        state.Animation = null;
        content.Opacity = 1;
        content.RenderTransform = null;
    }

    private static async Task AnimateAsync(
        Control content,
        bool entering,
        Point offset,
        bool immediate = false,
        CancellationToken cancellation = default,
        TimeSpan? durationOverride = null,
        bool preserveExitPosition = false,
        bool fadeOnExit = true,
        bool fadeOnEnter = true,
        Easing? easingOverride = null)
    {
        var state = GetState(content);
        var interrupted = state.Animation is not null;
        var startOpacity = interrupted || !entering ? content.Opacity : fadeOnEnter ? 0 : 1;
        var startX = interrupted ? content.RenderTransform?.Value.M31 ?? 0 : entering ? offset.X : 0;
        var startY = interrupted ? content.RenderTransform?.Value.M32 ?? 0 : entering ? offset.Y : 0;
        state.Animation?.Cancel();
        state.Animation?.Dispose();
        using var current = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        state.Animation = current;
        content.Opacity = startOpacity;
        content.RenderTransform = new TranslateTransform(startX, startY);
        var duration = immediate || Application.Current is App { ReduceMotionEnabled: true }
            ? TimeSpan.Zero
            : durationOverride ?? Duration(content, entering);
        if (duration > TimeSpan.Zero && TopLevel.GetTopLevel(content) is { } topLevel)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = current.Token.Register(() => completion.TrySetResult());
            TimeSpan? lastFrame = null;
            var x = startX;
            var y = startY;
            var opacity = startOpacity;
            var targetX = entering ? 0 : offset.X;
            var targetY = entering ? 0 : offset.Y;
            var targetOpacity = entering || !fadeOnExit ? 1d : 0d;
            var easing = easingOverride ??
                (content.TryFindResource(entering ? "MotionEaseEnter" : "MotionEaseExit", out var resource) && resource is Easing configured
                    ? configured : entering ? new CubicEaseOut() : new CubicEaseIn());
            var animationElapsed = 0d;
            var transform = (TranslateTransform)content.RenderTransform!;
            void Frame(TimeSpan timestamp)
            {
                if (current.IsCancellationRequested || !ReferenceEquals(state.Animation, current)) return;
                var elapsed = lastFrame is { } previous
                    ? Math.Max(0, (timestamp - previous).TotalSeconds) : 0;
                lastFrame = timestamp;
                animationElapsed += elapsed;
                var progress = Math.Clamp(animationElapsed / duration.TotalSeconds, 0, 1);
                var eased = easing.Ease(progress);
                x = startX + (targetX - startX) * eased;
                y = startY + (targetY - startY) * eased;
                opacity = startOpacity + (targetOpacity - startOpacity) * eased;
                transform.X = x;
                transform.Y = y;
                content.Opacity = Math.Clamp(opacity, 0, 1);
                if (animationElapsed >= duration.TotalSeconds)
                    completion.TrySetResult();
                else topLevel.RequestAnimationFrame(Frame);
            }
            topLevel.RequestAnimationFrame(Frame);
            await completion.Task;
        }
        if (!ReferenceEquals(state.Animation, current)) return;
        state.Animation = null;
        content.Opacity = entering || !fadeOnExit ? 1 : 0;
        content.RenderTransform = !entering && preserveExitPosition
            ? new TranslateTransform(offset.X, offset.Y)
            : null;
    }
}
