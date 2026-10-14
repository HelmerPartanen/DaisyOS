using System;
using System.IO;
using DaisyOS.Shell.Apps.Files.Services;
using Xunit;

namespace DaisyOS.Shell.Tests;

public class FilesPreferencesTests
{
    [Fact]
    public void FilesPreferences_InitializesWithSensibleDefaults()
    {
        var prefs = new FilesPreferences();
        Assert.False(prefs.IsGridView);
        Assert.Equal("Name", prefs.SortColumn);
        Assert.True(prefs.SortAscending);
        Assert.False(prefs.ShowHiddenFiles);
        Assert.True(prefs.ShowDateColumn);
        Assert.True(prefs.ShowTypeColumn);
        Assert.True(prefs.ShowSizeColumn);
        Assert.False(prefs.IsPreviewPaneVisible);
    }

    [Fact]
    public void FilesPreferences_SupportsCustomValues()
    {
        var prefs = new FilesPreferences(
            IsGridView: true,
            SortColumn: "Date",
            SortAscending: false,
            ShowHiddenFiles: true,
            ShowDateColumn: false,
            ShowTypeColumn: true,
            ShowSizeColumn: false,
            IsPreviewPaneVisible: true,
            StarredPaths: ["/tmp/test"]
        );

        Assert.True(prefs.IsGridView);
        Assert.Equal("Date", prefs.SortColumn);
        Assert.False(prefs.SortAscending);
        Assert.True(prefs.ShowHiddenFiles);
        Assert.False(prefs.ShowDateColumn);
        Assert.True(prefs.ShowTypeColumn);
        Assert.False(prefs.ShowSizeColumn);
        Assert.True(prefs.IsPreviewPaneVisible);
        Assert.Contains("/tmp/test", prefs.StarredPaths!);
    }
}

