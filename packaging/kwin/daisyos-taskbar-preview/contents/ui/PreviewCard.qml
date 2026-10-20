import QtQuick
import org.kde.kwin as KWinComponents
import org.kde.kirigami as Kirigami
import "WindowRoles.js" as WindowRoles

RoundedPreviewFrame {
    id: card
    required property QtObject client
    required property var theme
    signal activated()
    signal closed()
    radius: 16
    color: theme.raised
    border.width: 0
    border.color: theme.stroke
    clip: true
    Accessible.name: client ? client.caption : ""
    Accessible.role: Accessible.Button
    Accessible.onPressAction: card.activate()
    function activate() {
        if (!client) return;
        client.minimized = false;
        KWinComponents.Workspace.activeWindow = client;
        activated();
    }
    MouseArea {
        id: pointer
        anchors.fill: parent
        hoverEnabled: true
        acceptedButtons: Qt.LeftButton | Qt.MiddleButton
        cursorShape: Qt.PointingHandCursor
        onClicked: event => {
            if (event.button === Qt.MiddleButton && client && client.closeable) {
                client.closeWindow();
                card.closed();
            } else card.activate();
        }
    }
    Rectangle { width: parent.width; height: 32; color: pointer.containsMouse ? theme.hover : "transparent" }
    Kirigami.Icon {
        x: 10; y: 8; width: 16; height: 16
        source: client ? client.icon : ""
    }
    Text {
        x: 34; y: 0; height: 32
        width: parent.width - x - 36
        text: client ? client.caption : ""
        font.family: "Atkinson Hyperlegible"; font.pixelSize: 13
        verticalAlignment: Text.AlignVCenter
        elide: Text.ElideRight
        color: theme.foreground
    }
    Rectangle {
        id: close
        anchors.right: parent.right
        width: 32; height: 32
        radius: 0
        color: closePointer.containsMouse || closePointer.pressed ? theme.danger : "transparent"
        visible: client && client.closeable
        Text { anchors.centerIn: parent; text: "×"; font.pixelSize: 20; color: closePointer.containsMouse || closePointer.pressed ? (theme.onAction || "white") : theme.foreground }
        MouseArea {
            id: closePointer
            anchors.fill: parent
            hoverEnabled: true
            cursorShape: Qt.PointingHandCursor
            onClicked: {
                client.closeWindow();
                card.closed();
            }
        }
    }
    Item {
        id: viewport
        anchors.left: parent.left; anchors.right: parent.right
        anchors.top: parent.top; anchors.topMargin: 32
        anchors.bottom: parent.bottom
        clip: true
        readonly property bool files: client && WindowRoles.isFilesWindow(client.desktopFileName, client.resourceClass, client.resourceName, client.caption)
        readonly property real inset: files && !client.fullScreen ? 12 : 0
        readonly property real titleCrop: client && !files && !client.noBorder && !client.fullScreen ? 32 : 0
        readonly property real contentWidth: client ? Math.max(1, client.width - inset * 2) : 1
        readonly property real contentHeight: client ? Math.max(1, client.height - titleCrop - inset * 2) : 1
        readonly property real fit: Math.max(width / contentWidth, height / contentHeight)
        KWinComponents.WindowThumbnail {
            wId: card.client ? card.client.internalId : null
            width: card.client ? card.client.width * viewport.fit : 0
            height: card.client ? card.client.height * viewport.fit : 0
            x: (viewport.width - viewport.contentWidth * viewport.fit) / 2 - viewport.inset * viewport.fit
            y: (viewport.height - viewport.contentHeight * viewport.fit) / 2 - (viewport.inset + viewport.titleCrop) * viewport.fit
            smooth: true
        }
    }
}
