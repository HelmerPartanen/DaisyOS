using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace DaisyOS.Shell.Controls;

/// <summary>Renders decorative chrome with native vector antialiasing.</summary>
public sealed class ShellFrameBorder : Border
{
    public ShellFrameBorder()
    {
        RenderOptions.SetEdgeMode(this, EdgeMode.Antialias);
    }

    protected override Type StyleKeyOverride => typeof(Border);

    /// <summary>Compatibility hook for reveal animations.</summary>
    internal void PrepareFrame()
    {
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
    }
}
