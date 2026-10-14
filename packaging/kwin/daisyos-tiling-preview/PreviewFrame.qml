// SPDX-License-Identifier: GPL-3.0-or-later
import QtQuick

Item {
    id: frame
    property color accent: "#0067c0"
    property color surface: "#18181b"
    property color outerEdge: "#99000000"
    property color innerEdge: "#26ffffff"
    property real cornerRadius: 16
    // The inset is visual only: the geometry remains KWin's exact snap target.
    Rectangle {
        anchors.fill: parent
        anchors.margins: 6
        radius: Math.min(frame.cornerRadius, width / 2, height / 2)
        color: Qt.rgba(frame.surface.r, frame.surface.g, frame.surface.b, 0.16)
        border.width: 1
        border.color: frame.outerEdge
        antialiasing: true
        Rectangle {
            anchors.fill: parent
            anchors.margins: 1
            radius: Math.max(0, parent.radius - 1)
            color: Qt.rgba(frame.accent.r, frame.accent.g, frame.accent.b, 0.10)
            border.width: 1
            border.color: Qt.rgba(frame.accent.r, frame.accent.g, frame.accent.b, 0.70)
            antialiasing: true
            Rectangle {
                anchors.fill: parent
                anchors.margins: 1
                radius: Math.max(0, parent.radius - 1)
                color: "transparent"
                border.width: 1
                border.color: frame.innerEdge
                antialiasing: true
            }
        }
    }
}
