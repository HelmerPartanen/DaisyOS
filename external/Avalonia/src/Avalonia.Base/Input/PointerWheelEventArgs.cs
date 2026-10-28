namespace Avalonia.Input
{
    public class PointerWheelEventArgs : PointerEventArgs
    {
        /// <summary>Continuous scrolling in surface-local units, such as a trackpad.</summary>
        public bool IsPrecise { get; init; }
        /// <summary>The continuous scroll gesture has ended.</summary>
        public bool IsScrollEnd { get; init; }

        public Vector Delta { get; }

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
