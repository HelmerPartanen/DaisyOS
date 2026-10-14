using System;
using DaisyOS.Core.Desktop;

namespace DaisyOS.System.Display;

/// <summary>
/// Provides system metrics and monitor information required for the desktop grid.
/// </summary>
public interface ISystemMetricsProvider
{
    /// <summary>
    /// Gets the standard cell width for desktop icons, scaled appropriately for the given DPI.
    /// (Matches Windows SM_CXICONSPACING)
    /// </summary>
    double GetCellWidth(double dpi);

    /// <summary>
    /// Gets the standard cell height for desktop icons, scaled appropriately for the given DPI.
    /// (Matches Windows SM_CYICONSPACING)
    /// </summary>
    double GetCellHeight(double dpi);
}
