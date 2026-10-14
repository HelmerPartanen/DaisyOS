import QtQuick
import QtQuick.Effects

Rectangle {
    id: frame

    // Rectangle.clip is rectangular; mask the composed titlebar and thumbnail.
    antialiasing: true
    layer.enabled: true
    layer.smooth: true
    layer.effect: MultiEffect {
        maskEnabled: true
        // Retain fractional coverage from the antialiased mask edge.
        maskThresholdMin: 0.5
        maskSpreadAtMin: 1.0
        maskSource: ShaderEffectSource {
            visible: false
            smooth: true
            sourceItem: Rectangle {
                width: frame.width
                height: frame.height
                radius: frame.radius
                antialiasing: true
                color: "white"
            }
            hideSource: true
        }
    }
}
