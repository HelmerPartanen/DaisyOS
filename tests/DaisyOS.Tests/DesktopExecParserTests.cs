using DaisyOS.System.Processes;
using Xunit;

namespace DaisyOS.Tests;

public sealed class DesktopExecParserTests
{
    [Theory]
    [InlineData("app --name 'Daisy OS' %U", new[] { "app", "--name", "Daisy OS" })]
    [InlineData("app --literal=%% --flag", new[] { "app", "--literal=%", "--flag" })]
    [InlineData("app \\\"quoted\\\"", new[] { "app", "\"quoted\"" })]
    public void ParseHandlesDesktopFieldsQuotesAndEscapes(string command, string[] expected)
    {
        Assert.Equal(expected, DesktopExecParser.Parse(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("app 'unterminated")]
    public void ParseRejectsEmptyOrMalformedCommands(string command)
    {
        Assert.Null(DesktopExecParser.Parse(command));
    }
}
