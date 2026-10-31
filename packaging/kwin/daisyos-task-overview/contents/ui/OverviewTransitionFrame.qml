import QtQuick

// Keep thumbnail/mask render targets at their final size throughout a gesture.
// Moving and scaling the scene node avoids reallocating those textures and
// laying out titlebars on every input sample.
Rectangle {
    id: frame
    required property real presentationWidth
    required property real presentationHeight
    transform: Scale {
        xScale: frame.presentationWidth / Math.max(1, frame.width)
        yScale: frame.presentationHeight / Math.max(1, frame.height)
    }
}
