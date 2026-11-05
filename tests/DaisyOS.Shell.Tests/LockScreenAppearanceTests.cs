using DaisyOS.Shell.Services.Appearance;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class LockScreenAppearanceTests
{
    [Fact]
    public void InvalidValuesFallBackAndSizesStayWithinSafeLimits()
    {
        var style = new LockScreenAppearance { ClockSize = 999, WallpaperDimming = -50,
            FontFamily = new string('x', 201), Position = "Anywhere", Alignment = "Unknown", Weight = "Invalid",
            DateStyle = "invalid", TimeFormat = "invalid", Color = "#garbage" }.Normalize();
        Assert.Equal(140, style.ClockSize);
        Assert.Equal(0, style.WallpaperDimming);
        Assert.Equal("", style.FontFamily);
        Assert.Equal("Balanced", style.Position);
        Assert.Equal("Center", style.Alignment);
        Assert.Equal("Bold", style.Weight);
        Assert.Equal("Full", style.DateStyle);
        Assert.Equal("System", style.TimeFormat);
        Assert.Equal("Default", style.Color);
    }

    [Theory]
    [InlineData("System", true, "1:07")]
    [InlineData("System", false, "13:07")]
    [InlineData("12-hour", false, "1:07")]
    [InlineData("24-hour", true, "13:07")]
    public void ClockFormatCanFollowOrOverrideTheSystem(string format, bool systemTwelveHour, string expected)
    {
        Assert.Equal(expected, new LockScreenAppearance { TimeFormat = format }.FormatTime(new DateTime(2026, 11, 5, 13, 7, 0), systemTwelveHour));
    }

    [Fact]
    public async Task LockScreenPreferencesPersistIndependentlyOfDesktopTypography()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        try
        {
            var store = new AppearanceSettingsStore(Path.Combine(directory, "appearance.json"));
            var settings = new AppearanceSettings { SystemFontFamily = "Inter", DeptEffectEnabled = false,
                LockScreen = new() { FontFamily = "Noto Serif", Weight = "Light", ClockSize = 120, Color = "Warm",
                    DateStyle = "Compact", DateBelowClock = true, WallpaperDimming = 30, Alignment = "Right",
                    Position = "Lower", ShowBattery = false, ShowMedia = false } };
            await store.SaveAsync(settings);
            var loaded = store.Load();
            Assert.Equal(settings.LockScreen, loaded.LockScreen);
            Assert.Equal("Inter", loaded.SystemFontFamily);
            Assert.False(loaded.DeptEffectEnabled);
        }
        finally { Directory.Delete(directory, true); }
    }
}
