using System;
using DaisyOS.Core.Desktop;

namespace DaisyOS.System.Display;

public class AvaloniaMetricsProvider : ISystemMetricsProvider
{
    // Avalonia layout is expressed in device-independent units. Bounds already
    // shrink when an output uses fractional scaling, so multiplying these values
    // by RenderScaling would scale desktop tiles twice (1.25 * 1.25 at 125%).
    private const double BaseCellWidth = 74.0;
    private const double BaseCellHeight = 88.0;

    public double GetCellWidth(double dpi) => BaseCellWidth;

    public double GetCellHeight(double dpi) => BaseCellHeight;
}
