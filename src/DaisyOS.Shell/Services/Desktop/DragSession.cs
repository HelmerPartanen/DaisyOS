using System;
using Avalonia;
using DaisyOS.Core.Desktop;

namespace DaisyOS.Shell.Services.Desktop;

public sealed class DragSession
{
    public required DesktopItemId SourceId { get; init; }
    public required int OriginalIndex { get; init; }
    public required Avalonia.Point PressPoint { get; init; }
    public required Avalonia.Point GrabOffset { get; init; }

    public Avalonia.Point CurrentPointer { get; set; }
    public Avalonia.Point CurrentDragCenter { get; set; }

    public Avalonia.Point LastPointer { get; set; }
    public DateTimeOffset LastPointerTime { get; set; }
    public Vector SmoothedVelocity { get; set; }

    public int PreviewIndex { get; set; }
    public DragState State { get; set; } = DragState.Idle;

    public long Generation { get; init; }
    public Size ItemSize { get; init; }
}
