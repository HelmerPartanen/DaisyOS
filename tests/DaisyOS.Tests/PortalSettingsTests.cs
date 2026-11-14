using DaisyOS.Shell.Services.DesktopIntegration;
using Xunit;

namespace DaisyOS.Tests;

public sealed class PortalSettingsTests
{
    [Fact]
    public void GtkSettingsSupplyPositiveDoubleTextScaleAndNativeFontName()
    {
        var settings = PortalSettings.Values();
        var gtk = settings["org.gnome.desktop.interface"];
        Assert.Equal(1.0, gtk["text-scaling-factor"].GetDouble());
        Assert.Equal("Noto Sans 10", gtk["font-name"].GetString());
        Assert.Equal(":minimize,maximize,close", settings["org.gnome.desktop.wm.preferences"]["button-layout"].GetString());
        Assert.InRange(settings["org.freedesktop.appearance"]["color-scheme"].GetUInt32(), 1u, 2u);
    }

    [Theory]
    [InlineData("org.gnome.desktop.interface", true)]
    [InlineData("org.gnome.*", true)]
    [InlineData("*", true)]
    [InlineData("org.freedesktop.*", false)]
    [InlineData("org.gnome.desktop", false)]
    public void NamespaceFiltersIncludeGtkOnlyWhenRequested(string filter, bool expected)
    {
        Assert.Equal(expected, PortalSettings.IncludesNamespace([filter], "org.gnome.desktop.interface"));
        Assert.True(PortalSettings.IncludesNamespace([], "org.gnome.desktop.interface"));
    }
}
