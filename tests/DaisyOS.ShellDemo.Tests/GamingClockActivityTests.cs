using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DaisyOS.Shell.DesktopWidgets;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public sealed class GamingClockActivityTests
{
    [AvaloniaFact]
    public void Occluded_clock_stops_render_ticks_and_resumes_when_desktop_returns()
    {
        var clock = new ClockFace();
        Assert.False(clock.IsAnimationRunning);
        var window = new Window { Content = clock };
        try
        {
            window.Show();
            Assert.True(clock.IsAnimationRunning);
            clock.SetDesktopActivitySuspended(true);
            Assert.False(clock.IsAnimationRunning);
            clock.SetDesktopActivitySuspended(false);
            Assert.True(clock.IsAnimationRunning);
            window.Content = null;
            Assert.False(clock.IsAnimationRunning);
            clock.SetDesktopActivitySuspended(false);
            Assert.False(clock.IsAnimationRunning);
        }
        finally { window.Close(); }
    }
}
