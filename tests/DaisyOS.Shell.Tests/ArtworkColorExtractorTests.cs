using System;
using System.IO;
using Avalonia.Media;
using DaisyOS.Shell.Services.Media;
using SkiaSharp;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class ArtworkColorExtractorTests
{
    private static byte[] CreateTestBitmapBytes(int width, int height, Action<SKCanvas> draw)
    {
        using var bitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            draw(canvas);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void ExtractAccentColor_SelectsRepresentativeChromaticColor()
    {
        // Image with dark background and vibrant blue/cyan subject
        var bytes = CreateTestBitmapBytes(64, 64, canvas =>
        {
            canvas.Clear(new SKColor(20, 20, 20)); // dark background
            using var paint = new SKPaint { Color = new SKColor(0, 150, 255) }; // vibrant blue
            canvas.DrawRect(new SKRect(16, 16, 48, 48), paint);
        });

        var color = ArtworkColorExtractor.ExtractAccentColor(bytes);

        Assert.NotNull(color);
        // Should select a blue-ish hue
        var (h, s, l, chroma) = ArtworkColorExtractor.RgbToHsl(color.Value.R, color.Value.G, color.Value.B);
        Assert.True(chroma > 0.15, $"Expected significant chroma, got {chroma}");
        Assert.InRange(h, 180, 240); // Blue spectrum
    }

    [Fact]
    public void ExtractAccentColor_NormalizesOverlyBrightColors()
    {
        // Image with pure neon/glaring white-ish elements
        var bytes = CreateTestBitmapBytes(32, 32, canvas =>
        {
            canvas.Clear(new SKColor(255, 240, 240));
            using var paint = new SKPaint { Color = new SKColor(255, 200, 200) };
            canvas.DrawRect(new SKRect(8, 8, 24, 24), paint);
        });

        var color = ArtworkColorExtractor.ExtractAccentColor(bytes);

        Assert.NotNull(color);
        var (_, _, l, _) = ArtworkColorExtractor.RgbToHsl(color.Value.R, color.Value.G, color.Value.B);
        // Lightness must not exceed 0.65 to prevent glaring surface
        Assert.True(l <= 0.66, $"Expected lightness <= 0.65, got {l}");
    }

    [Fact]
    public void ExtractAccentColor_NormalizesOverlyDarkColors()
    {
        // Image with near-black colors
        var bytes = CreateTestBitmapBytes(32, 32, canvas =>
        {
            canvas.Clear(new SKColor(8, 8, 12));
            using var paint = new SKPaint { Color = new SKColor(12, 12, 25) };
            canvas.DrawRect(new SKRect(8, 8, 24, 24), paint);
        });

        var color = ArtworkColorExtractor.ExtractAccentColor(bytes);

        Assert.NotNull(color);
        var (_, _, l, _) = ArtworkColorExtractor.RgbToHsl(color.Value.R, color.Value.G, color.Value.B);
        // Lightness must be lifted to at least 0.28 to keep widget readable
        Assert.True(l >= 0.27, $"Expected lightness >= 0.28, got {l}");
    }

    [Fact]
    public void ExtractAccentColor_NormalizesExcessiveNeonSaturation()
    {
        // Image with 100% neon green
        var bytes = CreateTestBitmapBytes(32, 32, canvas =>
        {
            canvas.Clear(new SKColor(0, 255, 0));
        });

        var color = ArtworkColorExtractor.ExtractAccentColor(bytes);

        Assert.NotNull(color);
        var (_, s, _, _) = ArtworkColorExtractor.RgbToHsl(color.Value.R, color.Value.G, color.Value.B);
        // Saturation must be clamped to <= 0.70
        Assert.True(s <= 0.71, $"Expected saturation <= 0.70, got {s}");
    }

    [Fact]
    public void ExtractAccentColor_HandlesNeutralGrayscaleWithoutFakeSaturation()
    {
        // Pure grayscale image
        var bytes = CreateTestBitmapBytes(32, 32, canvas =>
        {
            canvas.Clear(new SKColor(50, 50, 50));
            using var paint = new SKPaint { Color = new SKColor(180, 180, 180) };
            canvas.DrawCircle(16, 16, 10, paint);
        });

        var color = ArtworkColorExtractor.ExtractAccentColor(bytes);

        Assert.NotNull(color);
        var (_, s, l, chroma) = ArtworkColorExtractor.RgbToHsl(color.Value.R, color.Value.G, color.Value.B);
        // Must stay neutral
        Assert.True(chroma < 0.05, $"Expected near-zero chroma for grayscale image, got {chroma}");
        Assert.True(s < 0.05, $"Expected near-zero saturation, got {s}");
        Assert.InRange(l, 0.28, 0.65);
    }

    [Fact]
    public void GetOrExtract_UsesCacheForSameArtworkKey()
    {
        var key = $"test-art-{Guid.NewGuid():N}";
        var bytes = CreateTestBitmapBytes(32, 32, canvas => canvas.Clear(new SKColor(200, 50, 50)));

        var first = ArtworkColorExtractor.GetOrExtract(key, bytes);
        Assert.NotNull(first);

        // Call again with dummy corrupted bytes - should return cached color
        var second = ArtworkColorExtractor.GetOrExtract(key, [1, 2, 3]);
        Assert.Equal(first, second);
    }

    [Fact]
    public void ApplyArtworkTint_CorrectlyBlendsColorsAtSpecifiedRatio()
    {
        var baseColor = Color.FromArgb(255, 100, 100, 100);
        var accent = Color.FromArgb(255, 200, 50, 0);

        // 60% accent + 40% base
        // R: 200 * 0.6 + 100 * 0.4 = 120 + 40 = 160
        // G: 50 * 0.6 + 100 * 0.4 = 30 + 40 = 70
        // B: 0 * 0.6 + 100 * 0.4 = 0 + 40 = 40
        var blended = ArtworkColorExtractor.ApplyArtworkTint(baseColor, accent, 0.60f);

        Assert.Equal(255, blended.A);
        Assert.Equal(160, blended.R);
        Assert.Equal(70, blended.G);
        Assert.Equal(40, blended.B);
    }

    [Fact]
    public void ApplyArtworkTint_DefaultTintOpacity_BlendsAtDefaultRatio()
    {
        Assert.Equal(0.35f, ArtworkColorExtractor.DefaultTintOpacity);
        Assert.Equal(89, ArtworkColorExtractor.DefaultTintAlpha);

        var baseColor = Color.FromArgb(255, 100, 100, 100);
        var accent = Color.FromArgb(255, 200, 50, 0);

        var blended = ArtworkColorExtractor.ApplyArtworkTint(baseColor, accent);

        Assert.Equal(255, blended.A);
        Assert.Equal(135, blended.R);
        Assert.Equal(82, blended.G);
        Assert.Equal(65, blended.B);
    }

    [Fact]
    public void ExtractAccentColor_InvalidInput_ReturnsNull()
    {
        Assert.Null(ArtworkColorExtractor.ExtractAccentColor([]));
        Assert.Null(ArtworkColorExtractor.ExtractAccentColor([1, 2, 3, 4]));
    }
}

