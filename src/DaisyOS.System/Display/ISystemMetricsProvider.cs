using System;
using DaisyOS.Core.Desktop;

namespace DaisyOS.System.Display;

/// <summary>
/// Provides system metrics and monitor information required for the desktop grid.
/// </summary>
public interface ISystemMetricsProvider
{
    /// <summary>
    /// Gets the standard cell width for desktop icons in logical layout units.
    /// The compositor converts these units to output pixels; implementations must
    /// not multiply by the render scale a second time.
    /// (Matches Windows SM_CXICONSPACING)
    /// </summary>
    double GetCellWidth(double dpi);

    /// <summary>
    /// Gets the standard cell height for desktop icons in logical layout units.
    /// (Matches Windows SM_CYICONSPACING)
    /// </summary>
    double GetCellHeight(double dpi);
}
