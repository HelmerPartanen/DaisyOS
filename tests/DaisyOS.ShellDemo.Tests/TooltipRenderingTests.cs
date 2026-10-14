using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using DaisyOS.Shell.Services.Theming;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class TooltipRenderingTests
{
    [AvaloniaFact]
    public void NativePopupHostBackgroundIsImmediateWhileCardColorsStillAnimate()
    {
        var window = new Window { Width = 300, Height = 160 };
        window.Show();
        var card = new Border { Width = 120, Height = 40, Background = Avalonia.Media.Brushes.DimGray };
        // The regular headless popup factory uses overlays. Construct its
        // IPopupImpl directly so this exercises the native PopupRoot theme.
        var implementation = typeof(AvaloniaHeadlessPlatform).Assembly.GetType("Avalonia.Headless.HeadlessWindowImpl")!;
        var platform = (Avalonia.Platform.IPopupImpl)Activator.CreateInstance(implementation,
            [true, Avalonia.Platform.PixelFormats.Bgra8888])!;
        var root = new PopupRoot(window, platform) { Content = card, Width = 120, Height = 40 };
        ((Avalonia.Controls.ISetLogicalParent)root).SetParent(window);
        root.Resources["MotionDurationBase"] = TimeSpan.FromMilliseconds(200);
        ThemeTransitionPolicy.SetEnabled(root, true);
        ThemeTransitionPolicy.SetEnabled(card, true);
        try
        {
            root.Show();
            Assert.Equal(Avalonia.Media.TextRenderingMode.Antialias, Avalonia.Media.TextOptions.GetTextRenderingMode(root));
            Assert.Equal(Avalonia.Media.TextHintingMode.Strong, Avalonia.Media.TextOptions.GetTextHintingMode(root));
            Assert.Equal(Avalonia.Media.BaselinePixelAlignment.Aligned, Avalonia.Media.TextOptions.GetBaselinePixelAlignment(root));
            root.Background = Avalonia.Media.Brushes.White;
            root.Background = null;
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Assert.Null(root.Background);
            Assert.DoesNotContain(root.Transitions!, transition => transition.Property == TemplatedControl.BackgroundProperty);
            Assert.Contains(card.Transitions!, transition => transition.Property == Border.BackgroundProperty);
            using var rendered = new RenderTargetBitmap(new PixelSize(120, 40));
            rendered.Render(root);
            using var encoded = new MemoryStream();
            rendered.Save(encoded);
            using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
            Assert.NotEqual(SkiaSharp.SKColors.White, pixels.GetPixel(0, 0));
        }
        finally { root.Dispose(); window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(false, 1d)]
    [InlineData(true, 1d)]
    [InlineData(false, 1.25d)]
    [InlineData(true, 1.25d)]
    public async Task TooltipCornersAreTransparentAndWaylandPolicyExemptsTheirMaterial(bool dark, double scale)
    {
        var button = new Button { Content = "Up" };
        var tooltip = new ToolTip { Content = "Up one level" };
        ToolTip.SetTip(button, tooltip);
        var window = new Window { Content = button, Width = 300, Height = 160,
            RequestedThemeVariant = dark ? ThemeVariant.Dark : ThemeVariant.Light };
        window.Show();
        try
        {
            ToolTip.SetIsOpen(button, true);
            await Task.Delay(300);
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            var policy = typeof(Avalonia.Wayland.WaylandWindow).Assembly.GetType("Avalonia.Wayland.PopupImpl")!
                .GetMethod("UsesMaterialBlur", BindingFlags.Static | BindingFlags.NonPublic)!;
            Assert.False((bool)policy.Invoke(null, [tooltip])!);
            Assert.True((bool)policy.Invoke(null, [new ContextMenu()])!);

            var size = new PixelSize((int)Math.Ceiling(tooltip.Bounds.Width * scale), (int)Math.Ceiling(tooltip.Bounds.Height * scale));
            using var rendered = new RenderTargetBitmap(size, new Vector(96 * scale, 96 * scale));
            rendered.Render(tooltip);
            using var encoded = new MemoryStream();
            rendered.Save(encoded);
            using var pixels = SkiaSharp.SKBitmap.Decode(encoded.ToArray());
            Assert.Equal(0, pixels.GetPixel(0, 0).Alpha);
            Assert.Equal(0, pixels.GetPixel(size.Width - 1, 0).Alpha);
            Assert.Equal(0, pixels.GetPixel(0, size.Height - 1).Alpha);
            Assert.Equal(0, pixels.GetPixel(size.Width - 1, size.Height - 1).Alpha);
            Assert.Equal(255, pixels.GetPixel(size.Width / 2, size.Height / 2).Alpha);
        }
        finally { ToolTip.SetIsOpen(button, false); window.Close(); }
    }
}
