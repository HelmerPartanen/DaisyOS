using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DaisyOS.Shell.Services.Motion;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class GuiMotionTests
{
    [Theory]
    [InlineData(0, 82, -1000)]
    [InlineData(20, 82, 500)]
    [InlineData(-4, 82, -10)]
    [InlineData(0, -82, 1000)]
    public void ExitNeverReversesOrOvershootsEvenWhenInterruptingEntrance(double start, double target, double velocity)
    {
        var previous = start;
        for (var frame = 0; frame <= 30; frame++)
        {
            var sample = GuiMotion.ExitStep(start, target, velocity, 0.14, frame / 30d * 0.14);
            Assert.InRange(sample.Position, Math.Min(start, target), Math.Max(start, target));
            Assert.True((sample.Position - previous) * (target - start) >= 0);
            previous = sample.Position;
        }
        Assert.Equal(target, previous, 8);
    }

    [AvaloniaFact]
    public void ParentFadeUpdatesMaskWhenCombinedMaterialOpacityCrossesThreshold()
    {
        var material = new Border { Width = 120, Height = 80, Opacity = 0.003 };
        var parent = new Border { Child = material };
        var window = new Window { Content = parent };
        window.Show();
        using var blur = new DaisyOS.Shell.Services.Compositor.KWinBlur(window, parent, () => [material]);
        try
        {
            blur.RefreshImmediately();
            var updates = 0;
            blur.GeometryChanged += () => updates++;
            parent.Opacity = 0.8;
            Assert.Equal(0, updates);
            parent.Opacity = 0.2;
            Assert.Equal(1, updates);
            parent.Opacity = 0.8;
            Assert.Equal(2, updates);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void BlurMaskOnlyRebuildsWhenFadeCrossesVisibilityThreshold()
    {
        var material = new Border { Width = 120, Height = 80 };
        var window = new Window { Content = material };
        window.Show();
        using var blur = new DaisyOS.Shell.Services.Compositor.KWinBlur(window, material);
        try
        {
            blur.RefreshImmediately();
            var updates = 0;
            blur.GeometryChanged += () => updates++;
            material.Opacity = 0.8;
            material.Opacity = 0.4;
            Assert.Equal(0, updates);
            material.Opacity = 0;
            Assert.Equal(1, updates);
            material.Opacity = 0.5;
            Assert.Equal(2, updates);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void NativePreviewHoldsTaskbarUntilPreviewCloses()
    {
        var shell = new DaisyOS.Shell.Views.ShellView();
        shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        shell.SetBottomBarAutoHide(true, true);
        shell.RevealBottomBarAt(640, 1280);
        var state = typeof(DaisyOS.Shell.Services.Compositor.KWinWindowBridge).Assembly.GetType("DaisyOS.Shell.Services.Compositor.TaskbarPreviewState")!;
        var read = state.GetMethod("Read", global::System.Reflection.BindingFlags.Static | global::System.Reflection.BindingFlags.NonPublic)!;
        var hide = shell.GetType().GetMethod("HideTaskbarIfUnhovered", global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!;
        try
        {
            read.Invoke(null, [true]);
            hide.Invoke(shell, null);
            Assert.True(shell.Taskbar.IsVisible);
            Assert.False(shell.SystemBar.IsVisible);
            read.Invoke(null, [false]);
            hide.Invoke(shell, null);
            Assert.False(shell.Taskbar.IsVisible);
        }
        finally
        {
            read.Invoke(null, [false]);
            shell.SetBottomBarAutoHide(false, false);
        }
    }

    [AvaloniaTheory]
    [InlineData(0.5)]
    [InlineData(0.9)]
    public void PanelEdgeRevealsEachBarIndependentlyWhileTheOtherIsVisible(double position)
    {
        var shell = new DaisyOS.Shell.Views.ShellView();
        shell.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        shell.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        shell.SetBottomBarAutoHide(true, true);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        var panel = new Window { Width = 1280, Height = 82,
            Background = null, Content = shell.DetachDesktopChromeForNativePanel() };
        DaisyOS.Shell.App.BindBottomEdgeReveal(panel, shell);
        panel.Show();
        try
        {
            using (panel.CaptureRenderedFrame()) { }
            panel.MouseMove(new Point(position * panel.Bounds.Width, panel.Bounds.Height - 1));
            Assert.Equal(position == 0.5, shell.Taskbar.IsVisible);
            Assert.Equal(position == 0.9, shell.SystemBar.IsVisible);
            var otherPosition = position == 0.5 ? 0.9 : 0.5;
            panel.MouseMove(new Point(otherPosition * panel.Bounds.Width, panel.Bounds.Height - 1));
            Assert.True(shell.Taskbar.IsVisible);
            Assert.True(shell.SystemBar.IsVisible);
        }
        finally { panel.Close(); }
        shell.SetExternalFullscreenActive(true);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        Assert.True(shell.IsBottomEdgeSensorEnabled);
        shell.RevealBottomBarAt(position * 1280, 1280);
        Assert.Equal(position == 0.5, shell.Taskbar.IsVisible);
        Assert.Equal(position == 0.9, shell.SystemBar.IsVisible);
        // Repeated compositor snapshots must not cancel an active reveal.
        shell.SetExternalFullscreenActive(true);
        Assert.Equal(position == 0.5, shell.Taskbar.IsVisible);
        Assert.Equal(position == 0.9, shell.SystemBar.IsVisible);
        var hideMethod = position == 0.5 ? "HideTaskbarIfUnhovered" : "HideSystemBarIfUnhovered";
        typeof(DaisyOS.Shell.Views.ShellView).GetMethod(hideMethod,
            global::System.Reflection.BindingFlags.Instance | global::System.Reflection.BindingFlags.NonPublic)!.Invoke(shell, null);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        Assert.True(shell.IsBottomPanelInputPassthrough);
        shell.SetExternalFullscreenActive(false);
        shell.SetBottomBarAutoHide(true, true);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
        shell.RevealBottomBarAt(0, 1280);
        Assert.False(shell.Taskbar.IsVisible);
        Assert.False(shell.SystemBar.IsVisible);
    }

    [AvaloniaFact]
    public async Task LauncherFirstOpenStartsFullyBelowViewportAndSlidesUpOpaque()
    {
        var launcher = new DaisyOS.Shell.Views.Components.Launcher.LauncherView(
            new DaisyOS.Shell.ViewModels.LauncherViewModel(null, []))
        {
            IsVisible = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom
        };
        Assert.Equal(TimeSpan.FromMilliseconds(220), GuiMotion.ShellSlideDuration(launcher, true));
        // Headless render work can consume most of a short entrance. Slow the
        // test clock so its intermediate geometry can be sampled reliably.
        launcher.Resources["MotionDurationShellEnter"] = TimeSpan.FromSeconds(1);
        var overlay = new DaisyOS.Shell.Services.Shell.BottomChromeOverlayHost();
        overlay.Viewport.Children.Add(launcher);
        var window = new Window { Width = 1280, Height = 900, Content = overlay };
        window.Show();
        try
        {
            // Native overlays are already mapped before the first launcher open.
            using (window.CaptureRenderedFrame()) { }
            launcher.AnimateIn();
            var root = launcher.FindControl<Border>("LauncherRootBorder")!;
            Assert.True(root.Bounds.Height > 500, "First-open motion must use laid-out launcher geometry.");
            var initialOffset = root.RenderTransform!.Value.M32;
            Assert.True(initialOffset >= root.Bounds.Height, "Launcher must begin fully below the clip edge.");
            Assert.Equal(1, root.Opacity);
            var initialBounds = new Rect(root.Bounds.Size).TransformToAABB(root.TransformToVisual(overlay.Viewport)!.Value);
            Assert.True(initialBounds.Top >= overlay.Viewport.Bounds.Height - 1,
                "The entire card must begin below the actual viewport clip edge.");
            // Pump one UI render pass per timer tick. A timer tick alone does
            // not pulse newly subscribed UI animations on an idle overlay.
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.InRange(root.RenderTransform!.Value.M32, 1, initialOffset - 1);
            Assert.Equal(1, root.Opacity);
            var intermediate = root.RenderTransform.Value.M32;
            var closed = false;
            launcher.AnimateOut(() => closed = true);
            launcher.AnimateIn();
            Assert.InRange(root.RenderTransform!.Value.M32, intermediate - 1, intermediate + 1);
            Assert.Equal(1, launcher.Opacity);
            Assert.Equal(1, root.Opacity);
            Assert.False(closed);
            launcher.CloseImmediate();
            launcher.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
            launcher.AnimateIn();
            Assert.True(launcher.IsVisible);
            Assert.Equal(1, root.Opacity);
            Assert.Null(root.RenderTransform);
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaFact]
    public async Task DelayedFirstFrameDoesNotConsumeTheEntrance()
    {
        var content = new Border { Width = 120, Height = 80 };
        var host = new Border { Child = content, IsVisible = false };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            var reveal = GuiMotion.SetVisibleAsync(host, content, true, offset: 82,
                durationOverride: TimeSpan.FromMilliseconds(220), fadeOnEnter: false);
            // Simulate mapping/layout blocking the UI before the first native frame.
            Thread.Sleep(300);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.InRange(content.RenderTransform!.Value.M32, 80, 82);
            await Task.Delay(65);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.InRange(content.RenderTransform!.Value.M32, 1, 79);
            await reveal;
            Assert.Null(content.RenderTransform);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReversalPreservesIntermediateSlideAndOpaqueExit()
    {
        var content = new Border { Width = 120, Height = 80 };
        var host = new Border { Child = content, IsVisible = false };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            await GuiMotion.SetVisibleAsync(host, content, true, immediate: true);
            var hide = GuiMotion.SetVisibleAsync(host, content, false, offset: 82,
                durationOverride: TimeSpan.FromMilliseconds(400), fadeOnExit: false);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(60);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var position = content.RenderTransform!.Value.M32;
            Assert.InRange(position, 0.01, 81.99);
            Assert.Equal(1, content.Opacity);
            var reopen = GuiMotion.SetVisibleAsync(host, content, true, offset: 82,
                durationOverride: TimeSpan.FromMilliseconds(220));
            Assert.InRange(content.RenderTransform!.Value.M32, position - 1, position + 1);
            Assert.Equal(1, content.Opacity);
            await Task.WhenAll(hide, reopen);
            Assert.True(host.IsVisible);
            Assert.True(host.IsHitTestVisible);
            Assert.Null(content.RenderTransform);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ShellTimingsUseSeparateResourceValuesIncludingReducedMotion()
    {
        var content = new Border();
        content.Resources["MotionDurationShellEnter"] = TimeSpan.FromMilliseconds(220);
        content.Resources["MotionDurationShellExit"] = TimeSpan.FromMilliseconds(140);
        Assert.Equal(TimeSpan.FromMilliseconds(220), GuiMotion.ShellSlideDuration(content, true));
        Assert.Equal(TimeSpan.FromMilliseconds(140), GuiMotion.ShellSlideDuration(content, false));
        content.Resources["MotionDurationShellEnter"] = TimeSpan.Zero;
        content.Resources["MotionDurationShellExit"] = TimeSpan.Zero;
        Assert.Equal(TimeSpan.Zero, GuiMotion.ShellSlideDuration(content, true));
        Assert.Equal(TimeSpan.Zero, GuiMotion.ShellSlideDuration(content, false));
    }

    [AvaloniaFact]
    public async Task InterruptedDismissalCannotHideReopenedPanelAndReleasesInputImmediately()
    {
        var content = new Border { Width = 120, Height = 80 };
        var host = new Border { Child = content, IsVisible = false };
        var window = new Window { Width = 400, Height = 300, Content = host };
        window.Show();
        try
        {
            await GuiMotion.SetVisibleAsync(host, content, true, immediate: true);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var originalBounds = host.Bounds;
            var hide = GuiMotion.SetVisibleAsync(host, content, false);
            Assert.False(GuiMotion.IsOpen(host));
            Assert.False(host.IsHitTestVisible);
            Assert.True(host.IsVisible);
            var reopen = GuiMotion.SetVisibleAsync(host, content, true);
            await Task.WhenAll(hide, reopen);
            Dispatcher.UIThread.RunJobs();
            Assert.True(GuiMotion.IsOpen(host));
            Assert.True(host.IsVisible);
            Assert.True(host.IsHitTestVisible);
            Assert.Equal(1, content.Opacity);
            Assert.Equal(originalBounds, host.Bounds);
            Assert.Null(host.RenderTransform);
            await GuiMotion.SetVisibleAsync(host, content, false);
            Assert.False(host.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ReducedMotionResourcesSettleSynchronouslyWithoutTransformOrFade()
    {
        var content = new Border();
        content.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        content.Resources["MotionDurationExit"] = TimeSpan.Zero;
        var host = new Border { Child = content, IsVisible = false };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            Assert.True(GuiMotion.SetVisibleAsync(host, content, true).IsCompletedSuccessfully);
            Assert.Equal(1, content.Opacity);
            Assert.True(GuiMotion.SetVisibleAsync(host, content, false).IsCompletedSuccessfully);
            Assert.False(host.IsVisible);
            await GuiMotion.RevealAsync(content);
            Assert.Equal(1, content.Opacity);
        }
        finally { window.Close(); }
    }
}
