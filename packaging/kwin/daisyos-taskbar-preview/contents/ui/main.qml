import QtQuick
import QtQuick.Window
import org.kde.kwin as KWinComponents
import "PreviewGeometry.js" as Geometry
import "WindowRoles.js" as WindowRoles

Window {
    id: popup
    title: "DaisyOS Taskbar Preview"
    flags: Qt.ToolTip | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus
    color: "transparent"
    visible: false
    property var request: ({})
    property var clients: []
    property string dismissedToken: ""
    property bool callPending: false
    property double lastReply: 0
    property real reveal: 0
    property int cardWidth: 264
    property var anchorRect: ({x: 0, y: 0, width: 0, height: 0})
    property bool anchorRequested: false
    readonly property int duration: request.reduceMotion ? 0 : 120

    function dismiss() {
        if (!visible || reveal === 0) return;
        dismissedToken = request.token || "";
        reveal = 0;
        finishClose.restart();
    }
    function apply(value) {
        var next;
        try { next = JSON.parse(value); } catch (error) { dismiss(); return; }
        if (next.dismiss) { dismiss(); return; }
        lastReply = Date.now();
        anchorRequested = !!next.token;
        if (!next.token) {
            if (visible && !Geometry.contains({x:x, y:y, width:width, height:height}, KWinComponents.Workspace.cursorPos, 4))
                leaveGrace.start();
            return;
        }
        if (next.token === dismissedToken) return;
        var windows = [];
        var available = KWinComponents.Workspace.windows;
        for (var index = 0; index < available.length; ++index) {
            var window = available[index];
            if (next.windowIds.indexOf(String(window.internalId)) >= 0 && !window.internal) windows.push(window);
        }
        if (!windows.length) { dismiss(); return; }
        var output = null;
        var cursor = KWinComponents.Workspace.cursorPos;
        for (var candidate of KWinComponents.Workspace.screens) {
            if (Geometry.contains(candidate.geometry, cursor, 0)) { output = candidate; break; }
        }
        if (!output) return;
        var anchor = Geometry.resolveAnchor(output.geometry, next);
        var same = request.token === next.token;
        var aspect = Math.min.apply(null, windows.map(function(client) {
            var files = WindowRoles.isFilesWindow(client.desktopFileName, client.resourceClass, client.resourceName, client.caption);
            var inset = files && !client.fullScreen ? 12 : 0;
            var title = !files && !client.noBorder && !client.fullScreen ? 32 : 0;
            return Math.max(1, client.width - inset * 2) / Math.max(1, client.height - title - inset * 2);
        }));
        var bounds = Geometry.layout(output.geometry, anchor, windows.length, aspect);
        if (!same || next.anchorX !== request.anchorX || next.anchorY !== request.anchorY || next.panelHeight !== request.panelHeight || clients.length !== windows.length) {
            x = bounds.x; y = bounds.y; width = bounds.width; height = bounds.height;
            cardWidth = bounds.cardWidth;
            anchorRect = {x: output.geometry.x + anchor.anchorX * output.geometry.width,
                          y: output.geometry.y + anchor.anchorY * output.geometry.height,
                          width: anchor.anchorWidth * output.geometry.width, height: anchor.anchorHeight * output.geometry.height};
        }
        if (!same) strip.contentX = 0;
        if (!same || clients.map(window => String(window.internalId)).join(",") !== windows.map(window => String(window.internalId)).join(","))
            clients = windows;
        request = next;
        finishClose.stop(); leaveGrace.stop();
        visible = true;
        reveal = 1;
    }
    KWinComponents.DBusCall {
        id: query
        service: "org.daisyos.Shell.WindowBridge"
        path: "/org/daisyos/Shell/WindowBridge"
        dbusInterface: "org.daisyos.Shell.WindowBridge"
        method: "GetTaskbarPreview"
        arguments: [popup.visible]
        onFinished: reply => { popup.callPending = false; popup.apply(String(reply[0])); }
        onFailed: { popup.callPending = false; popup.dismiss(); }
    }
    Timer {
        interval: 150; running: true; repeat: true
        onTriggered: if (!popup.callPending) { popup.callPending = true; query.call(); }
    }
    Timer {
        id: leaveGrace; interval: 250
        onTriggered: popup.dismiss()
    }
    Timer {
        id: finishClose; interval: popup.duration
        onTriggered: { popup.visible = false; popup.clients = []; }
    }
    Timer {
        interval: 50; running: popup.visible; repeat: true
        onTriggered: {
            if (Date.now() - popup.lastReply > 1500) { popup.dismiss(); return; }
            var cursor = KWinComponents.Workspace.cursorPos;
            var inside = Geometry.contains({x:popup.x, y:popup.y, width:popup.width, height:popup.height}, cursor, 4)
                || (popup.anchorRequested && Geometry.contains(popup.anchorRect, cursor, 4));
            if (inside) leaveGrace.stop(); else leaveGrace.start();
        }
    }
    Connections {
        target: KWinComponents.Workspace
        function onWindowActivated() { popup.dismiss(); }
        function onCurrentDesktopChanged() { popup.dismiss(); }
        function onScreensChanged() { popup.dismiss(); }
        function onWindowRemoved(window) {
            popup.clients = popup.clients.filter(client => client !== window);
            if (!popup.clients.length) popup.dismiss();
        }
    }
    Rectangle {
        id: surface
        anchors.fill: parent
        radius: 16
        color: popup.request.surface || "#ff181818"
        border.width: 1
        border.color: popup.request.stroke || "#ff444444"
        opacity: popup.reveal
        scale: 0.98 + popup.reveal * 0.02
        transformOrigin: Item.Bottom
        Behavior on opacity { NumberAnimation { duration: popup.duration; easing.type: Easing.OutCubic } }
        Behavior on scale { SpringAnimation { spring: 4; damping: 0.5; epsilon: 0.001 } }
        Flickable {
            id: strip
            anchors.fill: parent
            contentWidth: cards.width
            contentHeight: height
            boundsBehavior: Flickable.StopAtBounds
            clip: true
            Row {
                id: cards
                spacing: 0
                Repeater {
                    model: popup.clients
                    delegate: PreviewCard {
                        required property var modelData
                        client: modelData
                        theme: popup.request
                        width: popup.cardWidth
                        height: strip.height
                        onActivated: popup.dismiss()
                        onClosed: { if (popup.clients.length === 1) popup.dismiss(); }
                    }
                }
            }
            WheelHandler {
                target: null
                onWheel: event => {
                    strip.contentX = Math.max(0, Math.min(strip.contentWidth - strip.width,
                        strip.contentX - (event.angleDelta.y || event.angleDelta.x)));
                    event.accepted = true;
                }
            }
        }
    }
}
