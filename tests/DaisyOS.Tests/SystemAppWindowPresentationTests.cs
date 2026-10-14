using Avalonia.Controls;
using DaisyOS.Shell.Apps;
using Xunit;

namespace DaisyOS.Tests;

public sealed class SystemAppWindowPresentationTests
{
    private const string NormalRows = "44,*";
    private const string FullscreenRows = "0,*";

    [Theory]
    [InlineData(WindowState.Normal, false, true, true, NormalRows)]
    [InlineData(WindowState.Minimized, false, true, true, NormalRows)]
    [InlineData(WindowState.Maximized, true, true, false, NormalRows)]
    [InlineData(WindowState.FullScreen, true, false, false, FullscreenRows)]
    public void Presentation_MapsEveryNativeWindowState(
        WindowState state,
        bool fillsDisplay,
        bool titleBarVisible,
        bool resizeHandlesVisible,
        string rowDefinitions)
    {
        var presentation = SystemAppWindowPresentation.For(state, NormalRows, FullscreenRows);

        Assert.Equal(fillsDisplay, presentation.FillsDisplay);
        Assert.Equal(titleBarVisible, presentation.TitleBarVisible);
        Assert.Equal(resizeHandlesVisible, presentation.ResizeHandlesVisible);
        Assert.Equal(rowDefinitions, presentation.RowDefinitions);
    }

    [Theory]
    [InlineData(WindowState.Normal, WindowState.Normal)]
    [InlineData(WindowState.Maximized, WindowState.Maximized)]
    [InlineData(WindowState.Minimized, WindowState.Normal)]
    public void FullscreenExit_RestoresThePriorUsableWindowState(WindowState prior, WindowState expected)
    {
        Assert.Equal(expected, SystemAppWindowPresentation.RestoreStateForFullscreen(prior));
    }
    [Theory]
    [InlineData(1, 1, Avalonia.Controls.WindowEdge.NorthWest)]
    [InlineData(799, 1, Avalonia.Controls.WindowEdge.NorthEast)]
    [InlineData(1, 599, Avalonia.Controls.WindowEdge.SouthWest)]
    [InlineData(799, 599, Avalonia.Controls.WindowEdge.SouthEast)]
    [InlineData(400, 1, Avalonia.Controls.WindowEdge.North)]
    [InlineData(400, 599, Avalonia.Controls.WindowEdge.South)]
    [InlineData(1, 300, Avalonia.Controls.WindowEdge.West)]
    [InlineData(799, 300, Avalonia.Controls.WindowEdge.East)]
    public void ResizeTargetsUseVisibleWindowEdges(double x, double y, Avalonia.Controls.WindowEdge expected) =>
        Assert.Equal(expected, SystemAppWindowChrome.ResizeEdge(new Avalonia.Point(x, y), new Avalonia.Size(800, 600)));

    [Theory]
    [InlineData(400, 300)]
    [InlineData(6, 6)]
    [InlineData(-1, 300)]
    [InlineData(800, 300)]
    public void ResizeTargetsDoNotConsumeContentOrInvisibleGutters(double x, double y) =>
        Assert.Null(SystemAppWindowChrome.ResizeEdge(new Avalonia.Point(x, y), new Avalonia.Size(800, 600)));
}
