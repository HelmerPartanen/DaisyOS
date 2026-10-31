import QtQuick

Item {
    visible: false
    property var configuration: ({ AnimationDuration: 280, ReduceMotion: false })
    property var activeView: null
    property Component delegate
}
