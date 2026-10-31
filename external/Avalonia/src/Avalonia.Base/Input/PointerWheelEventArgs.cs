namespace Avalonia.Input
{
    public class PointerWheelEventArgs : PointerEventArgs
    {
        /// <summary>Continuous scrolling in surface-local units, such as a trackpad.</summary>
        public bool IsPrecise { get; init; }
        /// <summary>The continuous scroll gesture has ended.</summary>
        public bool IsScrollEnd { get; init; }

        public Vector Delta { get; }

        /// <summary>
        /// Converts scrolling to display-independent content pixels. Precise
        /// Wayland input uses the native GTK/Firefox pan baseline (GTK divides
        /// axis units by 10, Firefox's pixel-mode pan multiplier is 40).
        /// Wheel input retains the control's discrete step size.
        /// </summary>
        public Vector GetScrollDelta(double horizontalWheelStep = 50, double verticalWheelStep = 50) =>
            GetScrollDelta(Delta, horizontalWheelStep, verticalWheelStep);

        /// <summary>Converts a delta after the control has adjusted its direction or axes.</summary>
        public Vector GetScrollDelta(Vector delta, double horizontalWheelStep = 50, double verticalWheelStep = 50) =>
            IsPrecise ? delta * 4 : new Vector(delta.X * horizontalWheelStep, delta.Y * verticalWheelStep);

        public PointerWheelEventArgs(object? source, IPointer pointer, Visual rootVisual,
            Point rootVisualPosition, ulong timestamp,
            PointerPointProperties properties, KeyModifiers modifiers, Vector delta)
            : base(InputElement.PointerWheelChangedEvent, source, pointer, rootVisual, rootVisualPosition,
                timestamp, properties, modifiers)
        {
            Delta = delta;
        }
    }
}
