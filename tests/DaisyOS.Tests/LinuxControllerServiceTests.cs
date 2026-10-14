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

    [Theory]
    [InlineData((byte)0x02, (short)-32767, (byte)6, ControllerNavigationAction.Left)]
    [InlineData((byte)0x02, (short)32767, (byte)7, ControllerNavigationAction.Down)]
    [InlineData((byte)0x01, (short)1, (byte)0, ControllerNavigationAction.Confirm)]
    [InlineData((byte)0x01, (short)1, (byte)1, ControllerNavigationAction.Back)]
    [InlineData((byte)0x01, (short)1, (byte)2, ControllerNavigationAction.QuickSettings)]
    [InlineData((byte)0x01, (short)1, (byte)3, ControllerNavigationAction.Details)]
    [InlineData((byte)0x01, (short)1, (byte)4, ControllerNavigationAction.PreviousSection)]
    [InlineData((byte)0x01, (short)1, (byte)5, ControllerNavigationAction.NextSection)]
    [InlineData((byte)0x01, (short)1, (byte)10, ControllerNavigationAction.OpenConsole)]
    public void DecodeNavigation_MapsCommonControllerEvents(byte type, short value, byte number, ControllerNavigationAction expected)
    {
        var input = new LinuxControllerInputService(loadUserConfig: false);

        Assert.Equal(expected, input.DecodeNavigation(type, value, number));
    }

    [Theory]
    [InlineData((byte)0, ControllerNavigationAction.Confirm)]
    [InlineData((byte)1, ControllerNavigationAction.Back)]
    [InlineData((byte)3, ControllerNavigationAction.QuickSettings)]
    [InlineData((byte)2, ControllerNavigationAction.Details)]
    [InlineData((byte)10, ControllerNavigationAction.OpenConsole)]
    public void DecodeNavigation_MapsDualShockJoystickLayout(byte button, ControllerNavigationAction expected)
    {
        var input = new LinuxControllerInputService(loadUserConfig: false);

        Assert.Equal(expected, input.DecodeNavigation((byte)0x01, 1, button, playStationLayout: true));
    }

    [Theory]
    [InlineData((ushort)0x01, (ushort)0x130, 1, ControllerNavigationAction.Confirm)]
    [InlineData((ushort)0x01, (ushort)0x134, 1, ControllerNavigationAction.Details)]
    [InlineData((ushort)0x03, (ushort)16, -1, ControllerNavigationAction.Left)]
    [InlineData((ushort)0x03, (ushort)17, 1, ControllerNavigationAction.Down)]
    public void DecodeEvdevNavigation_MapsEventOnlyControllerInput(ushort type, ushort code, int value, ControllerNavigationAction expected)
    {
        var input = new LinuxControllerInputService(loadUserConfig: false);

        Assert.Equal(expected, input.DecodeEvdevNavigation(type, code, value));
    }
}
