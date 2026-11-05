using System.Text.Json.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using DaisyOS.Shell.Services.Theming;

namespace DaisyOS.Shell.Services.Appearance;

/// <summary>Independent lock-screen styling; authentication controls always remain available.</summary>
public sealed record LockScreenAppearance
{
    public string FontFamily { get; set; } = "";
    public string Weight { get; set; } = "Bold";
    public int ClockSize { get; set; } = 100;
    public string Color { get; set; } = "Default";
    public string Position { get; set; } = "Balanced";
    public string Alignment { get; set; } = "Center";
    public string TimeFormat { get; set; } = "System";
    public string DateStyle { get; set; } = "Full";
    public bool DateBelowClock { get; set; }
    public int WallpaperDimming { get; set; }
    public bool ShowBattery { get; set; } = true;
    public bool ShowMedia { get; set; } = true;

    public LockScreenAppearance Normalize() => this with
    {
        FontFamily = (FontFamily ?? "").Trim() is { Length: <= 200 } font ? font : "",
        Weight = OneOf(Weight, "Bold", "Light", "Regular", "Medium", "Black"),
        ClockSize = Math.Clamp(ClockSize, 70, 140),
        Color = OneOf(Color, "Default", "White", "Warm", "Rose", "Mint", "Blue"),
        Position = OneOf(Position, "Balanced", "Higher", "Lower"),
        Alignment = OneOf(Alignment, "Center", "Left", "Right"),
        TimeFormat = OneOf(TimeFormat, "System", "12-hour", "24-hour"),
        DateStyle = OneOf(DateStyle, "Full", "Compact", "Numeric", "Hidden"),
        WallpaperDimming = Math.Clamp(WallpaperDimming, 0, 70)
    };

    private static string OneOf(string? value, string fallback, params string[] others) =>
        value == fallback || others.Contains(value) ? value! : fallback;

    [JsonIgnore]
    public double TopFraction => Position switch { "Higher" => 0.06, "Lower" => 0.16, _ => 0.10 };
    [JsonIgnore]
    public FontWeight ClockWeight => Weight switch
    {
        "Light" => FontWeight.Light, "Regular" => FontWeight.Normal, "Medium" => FontWeight.Medium,
        "Black" => FontWeight.Black, _ => FontWeight.Bold
    };
    [JsonIgnore]
    public HorizontalAlignment ClockAlignment => Alignment switch
    {
        "Left" => HorizontalAlignment.Left, "Right" => HorizontalAlignment.Right, _ => HorizontalAlignment.Center
    };
    public IBrush ClockBrush(IBrush fallback) => Color switch
    {
        "White" => Brushes.White,
        "Warm" => new SolidColorBrush(Avalonia.Media.Color.Parse("#FFE7BA")),
        "Rose" => new SolidColorBrush(Avalonia.Media.Color.Parse("#FFD1DC")),
        "Mint" => new SolidColorBrush(Avalonia.Media.Color.Parse("#CCF5DF")),
        "Blue" => new SolidColorBrush(Avalonia.Media.Color.Parse("#CFE7FF")),
        _ => fallback
    };

    public string FormatTime(DateTime now, bool systemTwelveHour) => now.ToString(
        (TimeFormat == "12-hour" || TimeFormat == "System" && systemTwelveHour) ? "h:mm" : "H:mm",
        global::System.Globalization.CultureInfo.InvariantCulture);
    public string FormatDate(DateTime now) => now.ToString(DateStyle switch
    {
        "Compact" => "ddd, MMM d", "Numeric" => "d", _ => "dddd, MMMM d"
    });

    public void ApplyClock(TextBlock clock, TextBlock date, StackPanel area, IBrush defaultBrush)
    {
        if (string.IsNullOrEmpty(FontFamily)) clock.ClearValue(TextBlock.FontFamilyProperty);
        else clock.FontFamily = TypographyPolicy.ResolveFontFamily(FontFamily);
        clock.FontWeight = ClockWeight;
        clock.Foreground = ClockBrush(defaultBrush);
        date.IsVisible = DateStyle != "Hidden";
        area.HorizontalAlignment = ClockAlignment;
        if (area.Children.Contains(date))
        {
            var desired = DateBelowClock ? area.Children.Count - 1 : 0;
            if (area.Children.IndexOf(date) != desired)
            {
                area.Children.Remove(date);
                area.Children.Insert(desired, date);
            }
        }
    }
}
