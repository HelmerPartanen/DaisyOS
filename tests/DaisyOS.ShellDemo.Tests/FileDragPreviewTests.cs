using Avalonia.Headless.XUnit;
using DaisyOS.Shell.Apps.Files.Services;
using Xunit;

namespace DaisyOS.ShellDemo.Tests;

public class FileDragPreviewTests
{
    [AvaloniaFact]
    public void DragBadgeRendersTheSuppliedFilePreview()
    {
        using var thumbnail = new Avalonia.Media.Imaging.RenderTargetBitmap(new Avalonia.PixelSize(40, 20));
        using (var drawing = thumbnail.CreateDrawingContext())
            drawing.DrawRectangle(Avalonia.Media.Brushes.Red, null, new Avalonia.Rect(0, 0, 40, 20));
        using var badge = FileDragPreview.Create(["Wallpaper.jpg"], 1, thumbnail);
        var pixels = global::System.Runtime.InteropServices.Marshal.AllocHGlobal(badge.PixelSize.Width * badge.PixelSize.Height * 4);
        try
        {
            badge.CopyPixels(new Avalonia.PixelRect(badge.PixelSize), pixels,
                badge.PixelSize.Width * badge.PixelSize.Height * 4, badge.PixelSize.Width * 4);
            // The image center must contain the actual red thumbnail.
            int center = (25 * badge.PixelSize.Width + 31) * 4;
            Assert.Equal(255, global::System.Runtime.InteropServices.Marshal.ReadByte(pixels, center + 2));
            Assert.Equal(0, global::System.Runtime.InteropServices.Marshal.ReadByte(pixels, center + 1));
        }
        finally { global::System.Runtime.InteropServices.Marshal.FreeHGlobal(pixels); }
        badge.Save("/tmp/daisy-file-drag-thumbnail.png");
    }

    [AvaloniaFact]
    public void PointerSpeedIconIsIncludedInTheRuntimeBundle()
    {
        Assert.True(Avalonia.Platform.AssetLoader.Exists(new Uri(
            "avares://DaisyOS.Shell/Assets/Icons/HugeIcons/pointer_speed.svg")));
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(1.5)]
    [InlineData(2)]
    public void DragBadgeHasBoundedSizeAndUsesWholePixelDensity(double scale)
    {
        using var shortName = FileDragPreview.Create(["File_Name"], scale);
        using var longName = FileDragPreview.Create([new string('W', 300)], scale);
        using var group = FileDragPreview.Create(["One", "Two"], scale);
        Assert.Equal(42 * (int)Math.Ceiling(scale), shortName.PixelSize.Height);
        Assert.True(longName.Size.Width <= 278);
        Assert.True(group.Size.Width < longName.Size.Width);
        shortName.Save($"/tmp/daisy-file-drag-{scale}.png");
    }
}
