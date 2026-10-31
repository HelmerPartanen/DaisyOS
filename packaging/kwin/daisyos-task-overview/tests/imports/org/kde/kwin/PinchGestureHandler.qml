import QtQuick

// Test double for KWin's native recognition; tests supply its progress/signals.
QtObject {
    enum Direction { Contracting, Expanding }
    property int direction: PinchGestureHandler.Contracting
    property int fingerCount: 0
    property real progress: 0
    signal activated()
    signal cancelled()
}
