using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Shell.Controls;
using DaisyOS.Shell.Services.Appearance;
using DaisyOS.Shell.Services.Shell;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class CartoucheDirectionTests
{
    [AvaloniaTheory]
    [InlineData(DockEdge.Bottom, CartoucheTailEdge.Bottom)]
    [InlineData(DockEdge.Top, CartoucheTailEdge.Top)]
    [InlineData(DockEdge.Left, CartoucheTailEdge.Left)]
    [InlineData(DockEdge.Right, CartoucheTailEdge.Right)]
    public void PointerFacesTheDockAndLeavesMenuContentUpright(DockEdge edge, CartoucheTailEdge tailEdge)
    {
        var anchor = new Border { Width = 40, Height = 40,
            HorizontalAlignment = edge == DockEdge.Left ? HorizontalAlignment.Left : edge == DockEdge.Right ? HorizontalAlignment.Right : HorizontalAlignment.Center,
            VerticalAlignment = edge == DockEdge.Top ? VerticalAlignment.Top : edge == DockEdge.Bottom ? VerticalAlignment.Bottom : VerticalAlignment.Center };
        var content = new TextBlock { Text = "Open\n\nNew Empty Window\n\nUnpin from Taskbar\n\nApp Info", FontSize = 14 };
        var popup = new CartoucheBorder { Width = 240, Padding = new Thickness(12), Child = content, IsVisible = false };
        var root = new Grid { Children = { anchor, popup } };
        var window = new Window { Width = 640, Height = 480, Background = Brushes.DarkSlateBlue, Content = root };
        window.Show();
        try
        {
            window.UpdateLayout();
            DockPopupPlacement.Position(popup, anchor, edge, nativePanel: false);
            window.UpdateLayout();
            Assert.Equal(tailEdge, popup.TailEdge);
            Assert.Equal(8, popup.TailHeight);
            Assert.Equal(tailEdge == CartoucheTailEdge.Left ? 22 : 14, content.Bounds.Left);
            Assert.Equal(tailEdge == CartoucheTailEdge.Top ? 22 : 14, content.Bounds.Top);
            Assert.Null(content.RenderTransform);
            if (DockGeometry.IsVertical(edge))
                Assert.Equal(anchor.Bounds.Center.Y - popup.Bounds.Y, popup.TailY);
            else Assert.Equal(anchor.Bounds.Center.X - popup.Bounds.X, popup.TailX);
            using var rendered = window.CaptureRenderedFrame();
            Assert.NotNull(rendered);
            rendered.Save($"/tmp/daisy-cartouche-{edge}.png", Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(CartoucheTailEdge.Bottom)]
    [InlineData(CartoucheTailEdge.Top)]
    [InlineData(CartoucheTailEdge.Left)]
    [InlineData(CartoucheTailEdge.Right)]
    public void ContourHasAPointerOnlyOnTheSelectedEdge(CartoucheTailEdge edge)
    {
        var vertical = edge is CartoucheTailEdge.Left or CartoucheTailEdge.Right;
        var geometry = CartoucheBorder.BuildGeometry(240, 160, vertical ? 80 : 120, 18, 8, 14, 0, edge);
        var tip = edge switch
        {
            CartoucheTailEdge.Left => new Point(4, 80), CartoucheTailEdge.Right => new Point(236, 80),
            CartoucheTailEdge.Top => new Point(120, 4), _ => new Point(120, 156)
        };
        var besideTip = vertical ? new Point(tip.X, 40) : new Point(70, tip.Y);
        Assert.True(geometry.FillContains(tip));
        Assert.False(geometry.FillContains(besideTip));
        Assert.True(geometry.FillContains(new Point(120, 80)));
        Assert.True(geometry.Bounds.Left >= 0 && geometry.Bounds.Top >= 0);
        Assert.True(geometry.Bounds.Right <= 240 && geometry.Bounds.Bottom <= 160);
        // A clamped pointer near a screen corner must remain inside the frame.
        foreach (var position in new[] { -100d, 1000d })
        {
            var clamped = CartoucheBorder.BuildGeometry(240, 160, position, 18, 8, 14, 0, edge);
            Assert.True(clamped.Bounds.Left >= 0 && clamped.Bounds.Top >= 0);
            Assert.True(clamped.Bounds.Right <= 240 && clamped.Bounds.Bottom <= 160);
        }
    }
}
