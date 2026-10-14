using System.Text;
using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class GameArtworkValidatorTests
{
    [Fact]
    public void IsValidImageResponse_AcceptsJpegMagicBytes()
    {
        var jpegHeader = new byte[1024];
        jpegHeader[0] = 0xFF;
        jpegHeader[1] = 0xD8;
        jpegHeader[2] = 0xFF;
        jpegHeader[3] = 0xE0;

        var isValid = GameArtworkValidator.IsValidImageResponse("image/jpeg", jpegHeader);
        Assert.True(isValid);
    }

    [Fact]
    public void IsValidImageResponse_AcceptsPngMagicBytes()
    {
        var pngHeader = new byte[1024];
        pngHeader[0] = 0x89;
        pngHeader[1] = 0x50;
        pngHeader[2] = 0x4E;
        pngHeader[3] = 0x47;

        var isValid = GameArtworkValidator.IsValidImageResponse("image/png", pngHeader);
        Assert.True(isValid);
    }

    [Fact]
    public void IsValidImageResponse_RejectsHtmlErrorPage()
    {
        var htmlBytes = Encoding.UTF8.GetBytes("<!DOCTYPE html><html><head><title>404 Not Found</title></head><body>Error</body></html>");
        var isValid = GameArtworkValidator.IsValidImageResponse("text/html", htmlBytes);
        Assert.False(isValid);
    }

    [Fact]
    public void IsValidImageResponse_RejectsTinyData()
    {
        var tinyData = new byte[100];
        var isValid = GameArtworkValidator.IsValidImageResponse("image/jpeg", tinyData);
        Assert.False(isValid);
    }
}
