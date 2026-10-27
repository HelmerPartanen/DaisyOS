using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Motion;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class GuiMotionTests
{
    [AvaloniaFact]
    public void LauncherTransformedCardKeepsTheWholeCardAndBlurVisible()
    {
        var launcher = new DaisyOS.Shell.Views.Components.Launcher.LauncherView(
            new DaisyOS.Shell.ViewModels.LauncherViewModel(null, []))
        { IsVisible = false, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom };
        launcher.Resources["MotionDurationEnter"] = TimeSpan.Zero;
        var overlay = new DaisyOS.Shell.Services.Shell.BottomChromeOverlayHost();
        overlay.Viewport.Children.Add(launcher);
        var window = new Window { Width = 1280, Height = 900, Content = overlay };
        window.Show();
        try
        {
            launcher.AnimateIn();
            window.UpdateLayout();
            var root = launcher.FindControl<Border>("LauncherRootBorder")!;
            root.RenderTransform = new Avalonia.Media.TranslateTransform(0, -30);
            using var blur = new DaisyOS.Shell.Services.Compositor.KWinBlur(window, launcher, () => [root]);
            var regions = blur.BuildRegions();
            var cardTop = root.TranslatePoint(default, window)!.Value.Y;
            Assert.False(launcher.ClipToBounds);
            Assert.True(root.ClipToBounds);
            Assert.True(overlay.Viewport.ClipToBounds);
            // Blur masks deliberately round fractional edges outward to cover the
            // antialiased edge, so the first scanline is at floor(cardTop), not
            // inside the card.
            Assert.Equal((int)Math.Floor(cardTop), regions.Min(region => region.Y));
            using var frame = window.CaptureRenderedFrame();
            frame!.Save("/tmp/daisy-launcher-overshoot.png");
        }
        finally { launcher.CloseImmediate(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("MotionEaseEnter")]
    [InlineData("MotionEaseExit")]
    [InlineData("MotionEaseMove")]
    [InlineData("MotionEasePress")]
    public void SharedEasingIsMonotonicAndNeverOvershoots(string key)
    {
        Assert.True(Application.Current!.Resources.TryGetResource(key, ThemeVariant.Default, out var resource));
        var easing = Assert.IsAssignableFrom<Avalonia.Animation.Easings.Easing>(resource);
        var previous = 0d;
        for (var frame = 0; frame <= 120; frame++)
        {
            var progress = easing.Ease(frame / 120d);
            Assert.InRange(progress, previous, 1);
            previous = progress;
        }
        Assert.Equal(1, previous);
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
    public async Task LauncherIsCenteredAndUsesApplicationStyleScaleAndFade()
    {
        var launcher = new DaisyOS.Shell.Views.Components.Launcher.LauncherView(
            new DaisyOS.Shell.ViewModels.LauncherViewModel(null, []))
        {
            IsVisible = false,
            HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center
        };
        launcher.Resources["MotionDurationEnter"] = TimeSpan.FromSeconds(1);
        launcher.Resources["MotionDurationExit"] = TimeSpan.FromSeconds(1);
        var overlay = new Grid();
        overlay.Children.Add(launcher);
        var window = new Window { Width = 1280, Height = 900, Content = overlay };
        window.Show();
        try
        {
            using (window.CaptureRenderedFrame()) { }
            launcher.AnimateIn();
            var root = launcher.FindControl<Border>("LauncherRootBorder")!;
            Assert.Equal(680, launcher.Bounds.Width);
            Assert.Equal(560, launcher.Bounds.Height);
            Assert.Equal((window.Bounds.Width - launcher.Bounds.Width) / 2,
                launcher.TranslatePoint(default, window)!.Value.X, precision: 1);
            Assert.Equal((window.Bounds.Height - launcher.Bounds.Height) / 2,
                launcher.TranslatePoint(default, window)!.Value.Y, precision: 1);
            Assert.InRange(root.RenderTransform!.Value.M11, 0.939, 0.941);
            Assert.Equal(0, root.RenderTransform.Value.M31);
            Assert.Equal(0, root.RenderTransform.Value.M32);
            Assert.Equal(0, root.Opacity);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            await Task.Delay(30);
            Dispatcher.UIThread.RunJobs(DispatcherPriority.Render);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.InRange(root.RenderTransform!.Value.M11, 0.941, 0.999);
            Assert.InRange(root.Opacity, 0.001, 0.999);
            var intermediateScale = root.RenderTransform.Value.M11;
            var intermediateOpacity = root.Opacity;
            var closed = false;
            launcher.AnimateOut(() => closed = true);
            launcher.AnimateIn();
            Assert.InRange(root.RenderTransform!.Value.M11, intermediateScale - 0.001, intermediateScale + 0.001);
            Assert.InRange(root.Opacity, intermediateOpacity - 0.001, intermediateOpacity + 0.001);
            Assert.Equal(1, launcher.Opacity);
            Assert.False(closed);
            launcher.CloseImmediate();
            launcher.Resources["MotionDurationEnter"] = TimeSpan.Zero;
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
    public async Task EntranceSettlesUntransformedOnItsFinalFrame()
    {
        var content = new Border { Width = 120, Height = 80 };
        var host = new Border { Child = content, IsVisible = false };
        var window = new Window { Content = host };
        window.Show();
        try
        {
            var sawCrispFinalFrame = false;
            content.PropertyChanged += (_, change) =>
            {
                // Easing legitimately reports fractional opacities above 0.999
                // while the transform is still a fraction of a pixel away, so
                // only the exact endpoint tells us whether the final frame is
                // crisp. The regression being guarded against mounted an
                // identity transform until the awaited continuation ran, which
                // rendered one soft sampled frame at full opacity.
                if (change.Property == Visual.OpacityProperty &&
                    change.GetNewValue<double>() == 1 &&
                    change.GetOldValue<double>() < 1)
                {
                    sawCrispFinalFrame = true;
                    Assert.Null(content.RenderTransform);
                }
            };

            var reveal = GuiMotion.SetVisibleAsync(host, content, true, offset: 82,
                durationOverride: TimeSpan.FromMilliseconds(80), fadeOnEnter: true);
            for (var tick = 0; tick < 40 && !reveal.IsCompleted; tick++)
            {
                AvaloniaHeadlessPlatform.ForceRenderTimerTick();
                await Task.Delay(10);
            }
            await reveal;

            Assert.True(sawCrispFinalFrame);
            Assert.Null(content.RenderTransform);
            Assert.Equal(1, content.Opacity);
            Assert.True(host.IsVisible);
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
