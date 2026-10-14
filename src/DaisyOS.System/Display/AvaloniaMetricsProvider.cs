using System;
using DaisyOS.Core.Desktop;

namespace DaisyOS.System.Display;

public class AvaloniaMetricsProvider : ISystemMetricsProvider
{
    // The standard Windows 96 DPI icon spacing metrics (approximate defaults when API is unavailable).
    // On Windows 11, SM_CXICONSPACING is typically around 74 at 100% scale,
    // and SM_CYICONSPACING is around 88.
    private const double BaseCellWidth = 74.0;
    private const double BaseCellHeight = 88.0;

    public double GetCellWidth(double dpi)
    {
        double scale = dpi / 96.0;
        return Math.Round(BaseCellWidth * scale);
    }

    public double GetCellHeight(double dpi)
    {
        double scale = dpi / 96.0;
        return Math.Round(BaseCellHeight * scale);
    }
}
