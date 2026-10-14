using System;

namespace DaisyOS.Shell.Services.Desktop;

public sealed class DesktopMotionSettings
{
    public double MouseDragThreshold { get; init; } = 5.0;
    public double PenDragThreshold { get; init; } = 7.0;
    public double TouchDragThreshold { get; init; } = 10.0;

    public double ReorderHysteresis { get; init; } = 0.10; // Schmitt trigger hysteresis

    public double FolderCoreScale { get; init; } = 0.60; // central ~60% of icon rect is folder core
    public double FolderExitInflation { get; init; } = 10.0; // DIPs inflation for candidate exit region
    public double FolderArmedExitInflation { get; init; } = 18.0; // DIPs inflation for armed exit region

    public TimeSpan FolderDwell { get; init; } = TimeSpan.FromMilliseconds(520);
    public double FolderCandidateVelocityThreshold { get; init; } = 700.0; // DIPs/sec max speed to start dwell timer

    public TimeSpan ReorderAnimationDuration { get; init; } = TimeSpan.FromMilliseconds(145);
    public TimeSpan LiftAnimationDuration { get; init; } = TimeSpan.FromMilliseconds(100);
    public TimeSpan FolderCandidateDuration { get; init; } = TimeSpan.FromMilliseconds(110);
    public TimeSpan FolderArmedDuration { get; init; } = TimeSpan.FromMilliseconds(150);
    public TimeSpan DropDuration { get; init; } = TimeSpan.FromMilliseconds(120);

    public double DraggedItemScale { get; init; } = 1.035;
    public double DraggedItemOpacity { get; init; } = 0.98;
    public double FolderCandidateScale { get; init; } = 1.035;
    public double FolderArmedScale { get; init; } = 1.075;

    public static DesktopMotionSettings Default { get; } = new();
}
