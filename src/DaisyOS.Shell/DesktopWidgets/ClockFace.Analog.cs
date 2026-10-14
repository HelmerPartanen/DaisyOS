using System;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Platform;

namespace DaisyOS.Shell.DesktopWidgets;

public sealed partial class ClockFace
{
    private sealed record Hand(Point Pivot, double ForwardLength, (Geometry Shape, bool Stroke, double Thickness)[] Parts);

    private static readonly Lazy<Hand> HourHand = new(() => LoadHand("Hour_Hand_daydate.svg"));
    private static readonly Lazy<Hand> MinuteHand = new(() => LoadHand("Minute_Hand_daydate.svg"));
    private static readonly Lazy<Hand> SecondHand = new(() => LoadHand("Second_Hand_daydate.svg"));

    // Preserve the supplied path outlines and ring strokes in their original SVG coordinates.
    private static Hand LoadHand(string file)
    {
        using var stream = AssetLoader.Open(new Uri($"avares://DaisyOS.Shell/Assets/ClockWidget/{file}"));
        var svg = XElement.Load(stream);
        // All daydate hands supply a circle; its center is the rotation origin.
        var ring = svg.Elements().Single(element => element.Name.LocalName == "circle");
        var pivot = new Point(
            double.Parse(ring.Attribute("cx")!.Value, CultureInfo.InvariantCulture),
            double.Parse(ring.Attribute("cy")!.Value, CultureInfo.InvariantCulture));
        double forwardLength = double.Parse(svg.Attribute("height")!.Value, CultureInfo.InvariantCulture) - pivot.Y;
        var parts = svg.Elements().Select(element =>
        {
            double Number(string name) => double.Parse(element.Attribute(name)!.Value, CultureInfo.InvariantCulture);
            Geometry shape = element.Name.LocalName == "circle"
                ? new EllipseGeometry(new Rect(Number("cx") - Number("r"), Number("cy") - Number("r"), Number("r") * 2, Number("r") * 2))
                : Geometry.Parse(element.Attribute("d")!.Value);
            bool stroke = element.Attribute("stroke") != null;
            return (shape, stroke, stroke ? Number("stroke-width") : 0d);
        }).ToArray();
        return new Hand(pivot, forwardLength, parts);
    }

    private IBrush ThemeBrush(string key, IBrush fallback) =>
        this.TryFindResource(key, ActualThemeVariant, out var resource) && resource is IBrush brush ? brush : fallback;

    private void DrawAnalog(DrawingContext context, Point center, double width, double height, DateTime now)
    {
        var primary = ThemeBrush("ContentPrimaryBrush", ActualThemeVariant == Avalonia.Styling.ThemeVariant.Light ? Brushes.Black : Brushes.White);
        var tertiary = ThemeBrush("ContentTertiaryBrush", primary);
        var accent = ThemeBrush("ActionPrimaryBrush", primary);
        var hourPen = new Pen(primary, TickThickness) { LineCap = PenLineCap.Round };
        var minutePen = new Pen(tertiary, TickThickness) { LineCap = PenLineCap.Round };
        for (int i = 0; i < 60; i++)
        {
            var (start, end) = _cachedTicks[i];
            bool hour = i % 5 == 0;
            if (hour)
            {
                var direction = (end - start) / TickLength;
                // Extend inward so the outer edge stays on the sweep clock's track.
                start -= direction * 10;
            }
            context.DrawLine(hour ? hourPen : minutePen, start, end);
        }

        // A shared scale preserves the supplied hands' proportions at every widget size.
        double longestHand = Math.Max(HourHand.Value.ForwardLength,
            Math.Max(MinuteHand.Value.ForwardLength, SecondHand.Value.ForwardLength));
        double scale = Math.Max(0, (Math.Min(width, height) * 0.5 - 19) / longestHand);
        double seconds = now.Second + now.Millisecond / 1000d;
        double minutes = now.Minute + seconds / 60;
        double hours = now.Hour % 12 + minutes / 60;
        DrawHand(context, HourHand.Value, primary, center, scale, hours * Math.PI / 6);
        DrawHand(context, MinuteHand.Value, primary, center, scale, minutes * Math.PI / 30);
        DrawHand(context, SecondHand.Value, accent, center, scale, seconds * Math.PI / 30);
    }

    public static Matrix HandTransform(Point pivot, Point center, double scale, double angle) =>
        Matrix.CreateTranslation(-pivot.X, -pivot.Y)
        * Matrix.CreateScale(scale, scale)
        // SVG hands point down; midnight must point up.
        * Matrix.CreateRotation(angle + Math.PI)
        * Matrix.CreateTranslation(center.X, center.Y);

    private static void DrawHand(DrawingContext context, Hand hand, IBrush brush, Point center, double scale, double angle)
    {
        // Draw the original vector curves at the final transform and display DPI.
        // Explicit antialiasing also overrides an aliased ancestor's render options.
        using (context.PushRenderOptions(new RenderOptions { EdgeMode = EdgeMode.Antialias }))
        using (context.PushTransform(HandTransform(hand.Pivot, center, scale, angle)))
            foreach (var (shape, stroke, thickness) in hand.Parts)
                context.DrawGeometry(stroke ? null : brush, stroke ? new Pen(brush, thickness) : null, shape);
    }
}
