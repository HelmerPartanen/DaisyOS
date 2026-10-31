import QtQuick

QtObject {
    enum Direction { Up, Down, Left, Right }
    enum Device { Touchpad, Touchscreen }
    property int direction: SwipeGestureHandler.Up
    property int deviceType: SwipeGestureHandler.Touchpad
    property int fingerCount: 0
    property real progress: 0
    signal activated()
    signal cancelled()
}
