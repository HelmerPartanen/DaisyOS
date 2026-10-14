using DaisyOS.System.Gaming;
using Xunit;

namespace DaisyOS.Tests;

public sealed class GameTitleNormalizerTests
{
    [Theory]
    [InlineData("Cyberpunk 2077 (DX12)", "Cyberpunk 2077")]
    [InlineData("DOOM Eternal (Vulkan)", "DOOM Eternal")]
    [InlineData("The Witcher 3: Wild Hunt - Game of the Year Edition [GOG]", "The Witcher 3: Wild Hunt")]
    [InlineData("Hades™ (v1.38)", "Hades")]
    [InlineData("Portal 2 (Linux)", "Portal 2")]
    [InlineData(" Half-Life 2 ", "Half-Life 2")]
    public void Normalize_CleansLauncherSuffixesAndSymbols(string rawTitle, string expectedNormalized)
    {
        var result = GameTitleNormalizer.Normalize(rawTitle);
        Assert.Equal(expectedNormalized, result);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CalculateConfidence_ReturnsOneForExactMatch()
    {
        var confidence = GameTitleNormalizer.CalculateConfidence("Cyberpunk 2077 (DX12)", "Cyberpunk 2077");
        Assert.Equal(1.0, confidence);
    }

    [Fact]
    [Trait("Category", "Unit")]
    public void CalculateConfidence_ReturnsZeroForUnrelatedTitles()
    {
        var confidence = GameTitleNormalizer.CalculateConfidence("Cyberpunk 2077", "Stardew Valley");
        Assert.Equal(0.0, confidence);
    }
}
