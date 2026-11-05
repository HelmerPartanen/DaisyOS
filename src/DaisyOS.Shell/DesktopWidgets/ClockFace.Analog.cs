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
        var ring = svg.Elements().SingleOrDefault(element => element.Name.LocalName == "circle");
        var pivot = ring is not null
            ? new Point(
                double.Parse(ring.Attribute("cx")!.Value, CultureInfo.InvariantCulture),
                double.Parse(ring.Attribute("cy")!.Value, CultureInfo.InvariantCulture))
            : Geometry.Parse(svg.Elements().First(element =>
                    element.Name.LocalName == "path" && element.Attribute("stroke") != null)
                .Attribute("d")!.Value).Bounds.Center;
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

        var hourPen = new Pen(primary, TickThickness) { LineCap = PenLineCap.Round };
        var minutePen = new Pen(tertiary, TickThickness) { LineCap = PenLineCap.Round };
        for (int i = 0; i < 60; i++)
        {
            var (start, end) = AnalogMarker(width, height, i);
            context.DrawLine(i % 5 == 0 ? hourPen : minutePen, start, end);
        }

        if (ShowDayDate) DrawDayDate(context, width, height, now, primary);

        // A shared scale preserves the supplied hands' proportions at every widget size.
        double longestHand = Math.Max(HourHand.Value.ForwardLength,
            Math.Max(MinuteHand.Value.ForwardLength, SecondHand.Value.ForwardLength));
        var reach = ShowDayDate
            ? AnalogHandReach(width, height, _dayDateLabels[1].Position.X,
                _dayDateLabels[3].Position.X + _dayDateLabels[3].Text.Width)
            : AnalogHandReach(width, height);
        double scale = Math.Max(0, reach / longestHand);
        double seconds = now.Second + now.Millisecond / 1000d;
        double minutes = now.Minute + seconds / 60;
        double hours = now.Hour % 12 + minutes / 60;
        DrawHand(context, HourHand.Value, primary, center, scale, hours * Math.PI / 6);
        DrawHand(context, MinuteHand.Value, primary, center, scale, minutes * Math.PI / 30);
        DrawHand(context, SecondHand.Value, primary, center, scale, seconds * Math.PI / 30);
    }

    private (double Width, double Height, DateTime Date, string Culture, IBrush Primary, IBrush Red)? _dayDateKey;
    private (FormattedText Text, Point Position)[] _dayDateLabels = [];

    private void DrawDayDate(DrawingContext context, double width, double height, DateTime now, IBrush primary)
    {
        var culture = CultureInfo.CurrentCulture;
        var red = ThemeBrush("VibrantRedBrush", Brushes.Red);
        var key = (width, height, now.Date, culture.Name, primary, red);
        if (_dayDateKey != key)
        {
            _dayDateKey = key;
            var family = this.TryFindResource("ShellFontFamily", out var font) && font is FontFamily f ? f : FontFamily.Default;
            var typeface = new Typeface(family, FontStyle.Normal, FontWeight.SemiBold);
            var size = Math.Min(width, height);
            FormattedText Text(string value, double fontSize, IBrush brush) =>
                new(value, culture, FlowDirection.LeftToRight, typeface, fontSize, brush);
            (FormattedText, Point) Centered(FormattedText text, double x, double y) =>
                (text, new Point(x - text.Width / 2, y - text.Height / 2));
            var weekday = Text(now.ToString("ddd", culture).ToUpper(culture), size * 0.085, red);
            var day = Text(now.Day.ToString(culture) + ".", size * 0.085, primary);
            var gap = size * 0.025;
            var dateLeft = width * 0.68 - (weekday.Width + gap + day.Width) / 2;
            (FormattedText, Point) HourLabel(string value, int index)
            {
                var text = Text(value, size * 0.14, primary);
                var (inner, _) = AnalogMarker(width, height, index);
                var labelGap = size * 0.025;
                return index switch
                {
                    0 => Centered(text, width / 2, inner.Y + labelGap + text.Height / 2),
                    15 => Centered(text, inner.X - labelGap - text.Width / 2, height / 2),
                    30 => Centered(text, width / 2, inner.Y - labelGap - text.Height / 2),
                    _ => Centered(text, inner.X + labelGap + text.Width / 2, height / 2)
                };
            }
            _dayDateLabels =
            [
                HourLabel("12", 0),
                HourLabel("3", 15),
                HourLabel("6", 30),
                HourLabel("9", 45),
                (weekday, new Point(dateLeft, (height - weekday.Height) / 2)),
                (day, new Point(dateLeft + weekday.Width + gap, (height - day.Height) / 2))
            ];
        }
        foreach (var (text, position) in _dayDateLabels) context.DrawText(text, position);
    }

    internal static (Point Start, Point End) AnalogMarker(double width, double height, int index)
    {
        var center = new Point(width / 2, height / 2);
        var angle = index * Math.PI / 30;
        var direction = new Vector(Math.Sin(angle), -Math.Cos(angle));
        var halfWidth = Math.Max(0, width / 2 - TickInset - TickLength / 2);
        var halfHeight = Math.Max(0, height / 2 - TickInset - TickLength / 2);
        var radius = Math.Min(TickCornerRadius, Math.Min(halfWidth, halfHeight));
        var distance = Math.Min(
            Math.Abs(direction.X) < 1e-9 ? double.PositiveInfinity : halfWidth / Math.Abs(direction.X),
            Math.Abs(direction.Y) < 1e-9 ? double.PositiveInfinity : halfHeight / Math.Abs(direction.Y));
        var hit = direction * distance;
        // Intersect the rounded corner when the ray misses the straight edges.
        if (Math.Abs(hit.X) > halfWidth - radius && Math.Abs(hit.Y) > halfHeight - radius)
        {
            var corner = new Vector(Math.Sign(direction.X) * (halfWidth - radius),
                Math.Sign(direction.Y) * (halfHeight - radius));
            var projection = direction.X * corner.X + direction.Y * corner.Y;
            var discriminant = projection * projection - corner.X * corner.X - corner.Y * corner.Y + radius * radius;
            distance = projection + Math.Sqrt(Math.Max(0, discriminant));
        }
        var length = Math.Min(distance * 0.65, Math.Min(width, height) * (index % 15 == 0 ? 0.18 : index % 5 == 0 ? 0.10 : 0.055));
        return (center + direction * (distance - length), center + direction * distance);
    }

    internal static double AnalogHandReach(double width, double height, double? rightLabelLeft = null, double? leftLabelRight = null)
    {
        var (rightMarker, _) = AnalogMarker(width, height, 15);
        var horizontalReach = rightLabelLeft is { } right && leftLabelRight is { } left
            ? Math.Min(right - width / 2, width / 2 - left) - 6
            : rightMarker.X - width / 2 - 6;
        // Large faces measure the actual inner text edges of 3 and 9; medium
        // faces retain marker clearance. Keep rotating tips inside the face.
        return Math.Max(0, Math.Min(horizontalReach, height / 2 - TickInset));
    }

    internal static Point HandPivot(string file) => LoadHand(file).Pivot;

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
