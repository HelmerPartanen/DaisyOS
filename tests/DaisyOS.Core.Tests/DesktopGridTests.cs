using System;
using System.Linq;
using DaisyOS.Core.Desktop;
using Xunit;

namespace DaisyOS.Core.Tests;

public class DesktopGridTests
{
    [Fact]
    public void GridMetrics_CalculatesCorrectColumnCount()
    {
        var metrics = new DesktopGridMetrics("mon1", new Rect(0, 0, 1920, 1080), 96, 74, 88);
        Assert.Equal((int)Math.Floor(1920.0 / 74.0), metrics.ColumnCount);
        Assert.Equal((int)Math.Floor(1080.0 / 88.0), metrics.RowCount);
    }

    [Fact]
    public void GridMetrics_ValidatesRemainderInvariant()
    {
        var metrics = new DesktopGridMetrics("mon1", new Rect(0, 0, 1920, 1080), 96, 74, 88);
        
        double remainderX = 1920 - (metrics.ColumnCount * 74.0);
        double remainderY = 1080 - (metrics.RowCount * 88.0);
        
        Assert.True(remainderX >= 0 && remainderX < 74.0);
        Assert.True(remainderY >= 0 && remainderY < 88.0);
    }

    [Fact]
    public void OccupancyMap_PlacesItemInNearestCell()
    {
        var metrics = new DesktopGridMetrics("mon1", new Rect(0, 0, 1000, 1000), 96, 100, 100);
        var map = new DesktopOccupancyMap(metrics);

        // Occupy (5, 5)
        var target = new GridCell(5, 5);
        map.PlaceItem("item1", target);
        
        // Attempt to place another at (5, 5). 
        // Manhattan distances from (5,5):
        // (4,5), (6,5), (5,4), (5,6) are distance 1.
        // The tie-breaker rule says: lowest column first, then lowest row.
        // Candidates with distance 1:
        // (4,5) col 4
        // (5,4) col 5
        // (5,6) col 5
        // (6,5) col 6
        // So (4,5) should win.
        var resolved = map.PlaceItem("item2", target);
        Assert.Equal(4, resolved.Column);
        Assert.Equal(5, resolved.Row);
        
        // Place another at (5, 5). Next lowest column is 5. Rows are 4 and 6. Row 4 wins.
        var resolved2 = map.PlaceItem("item3", target);
        Assert.Equal(5, resolved2.Column);
        Assert.Equal(4, resolved2.Row);
    }
}
