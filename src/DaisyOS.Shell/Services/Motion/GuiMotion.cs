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

    public static Task SlideInAsync(Control content, Point offset, TimeSpan duration) =>
        AnimateAsync(content, true, offset, durationOverride: duration, fadeOnEnter: false);

    public static Task SlideOutAsync(Control content, Point offset, TimeSpan duration) =>
        AnimateAsync(content, false, offset, durationOverride: duration, preserveExitPosition: true, fadeOnExit: false);

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
            preserveExitPosition: !visible, fadeOnExit: fadeOnExit, fadeOnEnter: fadeOnEnter, easingOverride: easingOverride,
            topLevel: TopLevel.GetTopLevel(host) ?? TopLevel.GetTopLevel(content));
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

    /// <summary>
    /// Shows a centered surface with the scale-and-fade treatment used for an
    /// application opening, without changing its layout position.
    /// </summary>
    public static async Task SetScaleVisibleAsync(
        Control host,
        Control content,
        bool visible,
        Action? changed = null,
        bool immediate = false,
        double closedScale = 0.94,
        TimeSpan? durationOverride = null,
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
        changed?.Invoke();
        await AnimateScaleAsync(content, visible, closedScale, immediate, request.Token,
            durationOverride, easingOverride,
            topLevel: TopLevel.GetTopLevel(host) ?? TopLevel.GetTopLevel(content));
        if (request.IsCancellationRequested || !ReferenceEquals(state.Visibility, request)) return;
        if (!visible)
        {
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
        // Clear the transform before restoring opacity so observers never see an
        // opaque frame that is still going through a render transform.
        content.RenderTransform = null;
        content.Opacity = 1;
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
        Easing? easingOverride = null,
        TopLevel? topLevel = null)
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
        if (duration > TimeSpan.Zero && (topLevel ?? TopLevel.GetTopLevel(content)) is { } frameHost)
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
            void Settle()
            {
                // Write the exact endpoint on the last animated frame instead of
                // leaving a transform mounted until the await resumes. An identity
                // transform forces Avalonia through a render layer, which softens
                // text and can flash one extra sampled frame. Transform first so
                // observers never see the final opacity with a stale transform.
                content.RenderTransform = !entering && preserveExitPosition
                    ? new TranslateTransform(offset.X, offset.Y)
                    : null;
                content.Opacity = entering || !fadeOnExit ? 1 : 0;
            }
            void Frame(TimeSpan timestamp)
            {
                if (current.IsCancellationRequested || !ReferenceEquals(state.Animation, current)) return;
                var elapsed = lastFrame is { } previous
                    ? Math.Max(0, (timestamp - previous).TotalSeconds) : 0;
                lastFrame = timestamp;
                animationElapsed += elapsed;
                if (animationElapsed >= duration.TotalSeconds)
                {
                    Settle();
                    completion.TrySetResult();
                    return;
                }
                var progress = Math.Clamp(animationElapsed / duration.TotalSeconds, 0, 1);
                var eased = easing.Ease(progress);
                x = startX + (targetX - startX) * eased;
                y = startY + (targetY - startY) * eased;
                opacity = startOpacity + (targetOpacity - startOpacity) * eased;
                transform.X = x;
                transform.Y = y;
                content.Opacity = Math.Clamp(opacity, 0, 1);
                frameHost.RequestAnimationFrame(Frame);
            }
            frameHost.RequestAnimationFrame(Frame);
            await completion.Task;
        }
        if (!ReferenceEquals(state.Animation, current)) return;
        state.Animation = null;
        content.RenderTransform = !entering && preserveExitPosition
            ? new TranslateTransform(offset.X, offset.Y)
            : null;
        content.Opacity = entering || !fadeOnExit ? 1 : 0;
    }

    private static async Task AnimateScaleAsync(
        Control content,
        bool entering,
        double closedScale,
        bool immediate,
        CancellationToken cancellation,
        TimeSpan? durationOverride,
        Easing? easingOverride,
        TopLevel? topLevel = null)
    {
        var state = GetState(content);
        var interrupted = state.Animation is not null;
        var startScale = interrupted ? content.RenderTransform?.Value.M11 ?? 1 : entering ? closedScale : 1;
        var startOpacity = interrupted || !entering ? content.Opacity : 0;
        state.Animation?.Cancel();
        state.Animation?.Dispose();
        using var current = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        state.Animation = current;
        content.Opacity = startOpacity;
        content.RenderTransform = new ScaleTransform(startScale, startScale);
        var duration = immediate || Application.Current is App { ReduceMotionEnabled: true }
            ? TimeSpan.Zero
            : durationOverride ?? Duration(content, entering);
        if (duration > TimeSpan.Zero && (topLevel ?? TopLevel.GetTopLevel(content)) is { } frameHost)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = current.Token.Register(() => completion.TrySetResult());
            TimeSpan? lastFrame = null;
            var animationElapsed = 0d;
            var targetScale = entering ? 1 : closedScale;
            var targetOpacity = entering ? 1 : 0;
            var easing = easingOverride ??
                (content.TryFindResource(entering ? "MotionEaseEnter" : "MotionEaseExit", out var resource) && resource is Easing configured
                    ? configured : entering ? new CubicEaseOut() : new CubicEaseIn());
            var transform = (ScaleTransform)content.RenderTransform;
            void Settle()
            {
                content.RenderTransform = entering ? null : new ScaleTransform(closedScale, closedScale);
                content.Opacity = entering ? 1 : 0;
            }
            void Frame(TimeSpan timestamp)
            {
                if (current.IsCancellationRequested || !ReferenceEquals(state.Animation, current)) return;
                var elapsed = lastFrame is { } previous
                    ? Math.Max(0, (timestamp - previous).TotalSeconds) : 0;
                lastFrame = timestamp;
                animationElapsed += elapsed;
                if (animationElapsed >= duration.TotalSeconds)
                {
                    Settle();
                    completion.TrySetResult();
                    return;
                }
                var progress = Math.Clamp(animationElapsed / duration.TotalSeconds, 0, 1);
                var eased = easing.Ease(progress);
                var scale = startScale + (targetScale - startScale) * eased;
                transform.ScaleX = scale;
                transform.ScaleY = scale;
                content.Opacity = Math.Clamp(startOpacity + (targetOpacity - startOpacity) * eased, 0, 1);
                frameHost.RequestAnimationFrame(Frame);
            }
            frameHost.RequestAnimationFrame(Frame);
            await completion.Task;
        }
        if (!ReferenceEquals(state.Animation, current)) return;
        state.Animation = null;
        content.RenderTransform = entering ? null : new ScaleTransform(closedScale, closedScale);
        content.Opacity = entering ? 1 : 0;
    }
}
