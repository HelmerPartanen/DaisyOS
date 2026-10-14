using System;
using System.IO;
using SkiaSharp;
using Avalonia.Media.Imaging;
using System.Runtime.InteropServices;
using Avalonia.Platform;
using Xunit;

namespace Avalonia.Skia.UnitTests.Media
{
    public class ImmutableBitmapTests
    {
        [Fact]
        public void Resizing_And_Scaled_Decoding_Preserve_Image_Color_Profile()
        {
            using var source = new SKBitmap(new SKImageInfo(8, 8, SKColorType.Rgba8888, SKAlphaType.Premul));
            source.Erase(new SKColor(128, 128, 128));
            using var image = SKImage.FromBitmap(source);
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            // Add PNG gAMA=1.0 after IHDR: the pixels are linear, not sRGB.
            var bytes = png.ToArray();
            byte[] gamma = { 0, 0, 0, 4, 103, 65, 77, 65, 0, 1, 134, 160, 49, 232, 150, 95 };
            using var tagged = new MemoryStream();
            tagged.Write(bytes, 0, 33);
            tagged.Write(gamma);
            tagged.Write(bytes, 33, bytes.Length - 33);
            var encodedSource = tagged.ToArray();
            using var original = new ImmutableBitmap(new MemoryStream(encodedSource));
            using var resized = new ImmutableBitmap(original, new PixelSize(4, 4), BitmapInterpolationMode.HighQuality);
            using var decoded = new ImmutableBitmap(new MemoryStream(encodedSource), 4, true, BitmapInterpolationMode.HighQuality);
            using var target = new RenderTargetBitmapImpl(new PixelSize(4, 4), new Vector(96, 96));
            foreach (var bitmap in new[] { original, resized, decoded })
            {
                using (var context = target.CreateDrawingContext())
                    context.DrawBitmap(bitmap, 1, new Rect(0, 0, bitmap.PixelSize.Width, bitmap.PixelSize.Height), new Rect(0, 0, 4, 4));
                using var encoded = new MemoryStream();
                target.Save(encoded, PngBitmapEncoderOptions.Default);
                using var result = SKBitmap.Decode(encoded.ToArray());
                // Linear 0.5 must become about 0.735 in an sRGB GUI surface.
                Assert.InRange(result.GetPixel(2, 2).Red, (byte)186, (byte)190);
            }
        }

        [Theory]
        [InlineData(1, 1, false)]
        [InlineData(3, 5, false)]
        [InlineData(64, 64, false)]
        [InlineData(1, 1, true)]
        [InlineData(3, 5, true)]
        [InlineData(64, 64, true)]
        public unsafe void Constructor_From_Pixels_Copies_Source_Data(int width, int height, bool negativeStride)
        {
            var size = new PixelSize(width, height);
            var rowBytes = width * 4;
            var absStride = rowBytes;
            var byteSize = absStride * height;

            // Logical pixel byte: deterministic function of (row, byteIndexWithinRow).
            byte Expected(int row, int x) => (byte)((row * rowBytes + x) * 7 + 1);

            var source = Marshal.AllocHGlobal(byteSize);
            try
            {
                // Lay the logical rows out in physical memory. For a negative stride the rows are stored
                // bottom-up and the data pointer addresses the first (top) logical row, which sits at the
                // highest address.
                var buffer = new Span<byte>((void*)source, byteSize);
                for (var row = 0; row < height; row++)
                {
                    var physicalRow = negativeStride ? height - 1 - row : row;
                    for (var x = 0; x < rowBytes; x++)
                        buffer[physicalRow * absStride + x] = Expected(row, x);
                }

                var stride = negativeStride ? -absStride : absStride;
                var data = negativeStride ? source + absStride * (height - 1) : source;

                using var bitmap = new ImmutableBitmap(
                    size, new Vector(96, 96), stride,
                    PixelFormat.Bgra8888, AlphaFormat.Premul, data);

                // The constructor must take its own copy: corrupting (and freeing) the source
                // afterwards must not affect the bitmap's pixels.
                buffer.Fill(0xCD);

                Assert.Equal(size, bitmap.PixelSize);

                using var locked = bitmap.Lock();
                Assert.Equal(size, locked.Size);

                var dst = new ReadOnlySpan<byte>((void*)locked.Address, locked.RowBytes * height);
                for (var row = 0; row < height; row++)
                    for (var x = 0; x < rowBytes; x++)
                        Assert.Equal(Expected(row, x), dst[row * locked.RowBytes + x]);
            }
            finally
            {
                Marshal.FreeHGlobal(source);
            }
        }
    }
}
