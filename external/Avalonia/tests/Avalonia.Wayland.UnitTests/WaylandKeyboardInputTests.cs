using System.Collections.Generic;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Moq;
using Xunit;

namespace Avalonia.Wayland.UnitTests;

public class WaylandKeyboardInputTests
{
    private static RawKeyEventArgs Press(string? symbol, RawKeyEventType type = RawKeyEventType.KeyDown) =>
        new(Mock.Of<IKeyboardDevice>(), 123, Mock.Of<IInputRoot>(), type,
            Key.A, RawInputModifiers.None, PhysicalKey.A, symbol);

    [Theory]
    [InlineData("a")]
    [InlineData("A")]
    [InlineData("ä")]
    [InlineData("🌍")]
    public void InitialPressAndRepeatTicksInsertText(string symbol)
    {
        var events = new List<RawInputEventArgs>();
        for (var i = 0; i < 5; i++)
        {
            var press = Press(symbol);
            WaylandKeyboardInput.DispatchText(press, events.Add);
        }
        Assert.Equal(5, events.Count);
        foreach (var input in events)
        {
            var text = Assert.IsType<RawTextInputEventArgs>(input);
            Assert.Equal(symbol, text.Text);
            Assert.Equal((ulong)123, text.Timestamp);
        }
    }

    [Theory]
    [InlineData("\u001b")]
    [InlineData("\b")]
    [InlineData("\r")]
    [InlineData("")]
    [InlineData(null)]
    public void ControlKeysDoNotInsertText(string? symbol)
    {
        var events = new List<RawInputEventArgs>();
        WaylandKeyboardInput.DispatchText(Press(symbol), events.Add);
        Assert.Empty(events);
    }

    [Fact]
    public void HandledShortcutsAndReleasesDoNotInsertText()
    {
        var events = new List<RawInputEventArgs>();
        var handled = Press("a");
        handled.Handled = true;
        WaylandKeyboardInput.DispatchText(handled, events.Add);
        WaylandKeyboardInput.DispatchText(Press("a", RawKeyEventType.KeyUp), events.Add);
        Assert.Empty(events);
    }
}
