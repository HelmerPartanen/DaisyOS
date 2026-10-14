using System;
using System.Globalization;
using Avalonia;
using Avalonia.Input;
using DaisyOS.Shell.Controls;
using System.IO;
using System.Text;
using Avalonia.Platform;
using Avalonia.Svg.Skia;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace DaisyOS.Shell.Apps.Files.Services;

/// <summary>A native drag badge; no extra window or pointer capture is needed.</summary>
internal static class FileDragPreview
{
    public static readonly DataFormat<Bitmap> Format =
        DataFormat.CreateInProcessFormat<Bitmap>("daisy-file-drag-preview");

    public static RenderTargetBitmap Create(string[] names, double scale, IImage? preview = null, string icon = "file", FontFamily? fontFamily = null)
    {
        scale = Math.Max(1, Math.Ceiling(scale));
        var label = names.Length == 1 ? names[0] : Loc.Format("{0} files", names.Length);
        var text = new FormattedText(label, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            new Typeface(fontFamily ?? FontFamily.Default), 13, Brushes.White)
        {
            MaxTextWidth = 220,
            MaxLineCount = 1,
            Trimming = TextTrimming.CharacterEllipsis
        };
        var width = Math.Ceiling(text.Width) + 46;
        var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling((width + 12) * scale),
            (int)Math.Ceiling(42 * scale)), new Vector(96 * scale, 96 * scale));
        using var context = bitmap.CreateDrawingContext();
        // Leave room beside the real cursor; the compositor moves this surface.
        context.DrawRectangle(new SolidColorBrush(Color.Parse("#168BE8")), null,
            new Rect(12, 8, width, 34), 8, 8);
        if (preview is not null && preview.Size.Width > 0 && preview.Size.Height > 0)
        {
            var ratio = Math.Min(26 / preview.Size.Width, 26 / preview.Size.Height);
            var size = new Size(preview.Size.Width * ratio, preview.Size.Height * ratio);
            context.DrawImage(preview, new Rect(new Point(18 + (26 - size.Width) / 2,
                12 + (26 - size.Height) / 2), size));
        }
        else
        {
            // Match Files' semantic fallback only when no real preview exists.
            var name = UIIcons.Parse(icon).Glyph.ToLowerInvariant();
            using var asset = AssetLoader.Open(new Uri($"avares://DaisyOS.Shell/Assets/Icons/HugeIcons/{name}.svg"));
            using var reader = new StreamReader(asset);
            using var stream = new MemoryStream(Encoding.UTF8.GetBytes(reader.ReadToEnd().Replace("currentColor", "#FFFFFF")));
            using var source = SvgSource.LoadFromStream(stream);
            var glyph = new SvgImage { Source = source };
            context.DrawImage(glyph, new Rect(21, 15, 20, 20));
        }
        context.DrawText(text, new Point(52, 8 + (34 - text.Height) / 2));
        return bitmap;
    }
}
