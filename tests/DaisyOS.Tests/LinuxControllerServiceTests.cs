using DaisyOS.Core.Models;
using DaisyOS.System.Controllers;
using Xunit;

namespace DaisyOS.Tests;

public sealed class LinuxControllerServiceTests
{
    [Fact]
    public void ParseInputDevices_RecognizesKnownControllerOnEventDevice()
    {
        const string devices = """
            I: Bus=0005 Vendor=054c Product=0ce6 Version=0111
            N: Name="Sony Interactive Entertainment DualSense Wireless Controller"
            H: Handlers=event17 
            B: EV=20000b
            """;

        var status = LinuxControllerService.ParseInputDevices(devices);

        Assert.True(status.IsConnected);
        Assert.Equal("Sony Interactive Entertainment DualSense Wireless Controller", status.Name);
        Assert.Equal("/dev/input/event17", status.DevicePath);
    }

    [Fact]
    public void ParseInputDevices_RecognizesUnknownJoystickHandler()
    {
        const string devices = """
            I: Bus=0003 Vendor=1234 Product=5678 Version=0110
            N: Name="Arcade input"
            H: Handlers=js0 event4 
            B: EV=20000b
            """;

        var status = LinuxControllerService.ParseInputDevices(devices);

        Assert.True(status.IsConnected);
        Assert.Equal("/dev/input/event4", status.DevicePath);
        Assert.Equal("/dev/input/js0", status.JoystickPath);
    }

    [Fact]
    public void ParseInputDevices_IgnoresKeyboardAndMouse()
    {
        const string devices = """
            I: Bus=0003 Vendor=046d Product=c52b Version=0111
            N: Name="USB Receiver"
            H: Handlers=sysrq kbd event3 leds 
            B: EV=120013

            I: Bus=0003 Vendor=046d Product=c077 Version=0111
            N: Name="USB Optical Mouse"
            H: Handlers=mouse0 event5 
            B: EV=17
            """;

        var status = LinuxControllerService.ParseInputDevices(devices);

        Assert.False(status.IsConnected);
        Assert.Null(status.Name);
    }

}
