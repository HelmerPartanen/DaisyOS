import QtQuick

// Paint above the opaque live thumbnail, rather than behind its children.
Rectangle {
    property color strokeColor: "#ff444444"
    anchors.fill: parent
    z: 1
    color: "transparent"
    radius: 16
    border.width: 1
    border.color: strokeColor
    antialiasing: true
}
