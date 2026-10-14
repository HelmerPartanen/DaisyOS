// SPDX-License-Identifier: GPL-3.0-or-later
import QtQuick
import QtQuick.Window
import QtCore
import org.kde.kirigami as Kirigami

Window {
    id: preview
    title: "DaisyOS Tiling Preview"
    // KWin supplies this context property and marks this window __kwin_outline.
    property QtObject outlineSource: typeof outline !== "undefined" ? outline : null
    property string themeConfigFile: StandardPaths.writableLocation(StandardPaths.ConfigLocation) + "/daisyos-window-decorationrc"
    property bool reduceMotion: false
    property color accent: Kirigami.Theme.highlightColor
    property color surface: Kirigami.Theme.backgroundColor
    property color outerEdge: "#99000000"
    property color innerEdge: "#26ffffff"
    property real cornerRadius: 16
    readonly property int motionDuration: reduceMotion ? 0 : Math.min(160, Kirigami.Units.shortDuration)
    property bool showing: false
    property rect fromRect: Qt.rect(0, 0, 0, 0)
    property rect targetRect: Qt.rect(0, 0, 0, 0)
    property real progress: 1
    readonly property rect currentRect: Qt.rect(
        fromRect.x + (targetRect.x - fromRect.x) * progress,
        fromRect.y + (targetRect.y - fromRect.y) * progress,
        fromRect.width + (targetRect.width - fromRect.width) * progress,
        fromRect.height + (targetRect.height - fromRect.height) * progress)
    flags: Qt.BypassWindowManagerHint | Qt.FramelessWindowHint | Qt.WindowDoesNotAcceptFocus | Qt.WindowTransparentForInput
    color: "transparent"
    visible: showing

    Settings { id: palette; location: "file://" + preview.themeConfigFile; category: "Decoration" }
    function refreshPalette() {
        palette.sync();
        accent = palette.value("ActionPrimary", Kirigami.Theme.highlightColor);
        surface = palette.value("SurfaceBase", Kirigami.Theme.backgroundColor);
        outerEdge = palette.value("ShellFrameOuterEdge", "#99000000");
        innerEdge = palette.value("ShellFrameInnerHighlight", "#26ffffff");
        cornerRadius = Number(palette.value("CornerRadius", 16));
        reduceMotion = String(palette.value("ReduceMotion", "false")) === "true";
    }
    function setEnvelope(a, b) {
        // Animate global coordinates, independent of the native surface's origin.
        // Cover the entire swept region so changing KWin's unified geometry can
        // neither clip the frame nor snap it to a different local coordinate.
        x = Math.floor(Math.min(a.x, b.x));
        y = Math.floor(Math.min(a.y, b.y));
        width = Math.ceil(Math.max(a.x + a.width, b.x + b.width)) - x;
        height = Math.ceil(Math.max(a.y + a.height, b.y + b.height)) - y;
    }
    function moveTo(next, initial) {
        var current = initial || currentRect;
        movement.stop();
        fromRect = current;
        targetRect = next;
        progress = 0;
        setEnvelope(current, next);
        if (motionDuration === 0) progress = 1;
        else movement.start();
    }
    function sameRect(a, b) {
        return a.x === b.x && a.y === b.y && a.width === b.width && a.height === b.height;
    }
    function synchronize() {
        if (!outlineSource || !outlineSource.active) {
            showing = false;
            movement.stop();
            reveal.stop();
            frame.opacity = 0;
            return;
        }
        if (!showing) {
            refreshPalette();
            var parentRect = outlineSource.visualParentGeometry;
            var start = parentRect.width > 0 && parentRect.height > 0 ? parentRect : outlineSource.geometry;
            moveTo(outlineSource.geometry, start);
            showing = true;
            frame.opacity = reduceMotion ? 1 : 0;
            if (!reduceMotion) reveal.start();
        } else if (!sameRect(targetRect, outlineSource.geometry)) {
            moveTo(outlineSource.geometry);
        }
    }
    Component.onCompleted: synchronize()
    Connections {
        target: preview.outlineSource
        function onActiveChanged() { preview.synchronize(); }
        function onGeometryChanged() { Qt.callLater(preview.synchronize); }
        // Deliberately do not settle on unifiedGeometryChanged: it fires on
        // every tile target change and caused the stock preview to teleport.
    }
    NumberAnimation {
        id: movement
        target: preview
        property: "progress"
        from: 0; to: 1
        duration: preview.motionDuration
        easing.type: Easing.OutCubic
    }
    NumberAnimation {
        id: reveal
        target: frame; property: "opacity"
        from: 0; to: 1
        duration: Math.min(100, preview.motionDuration)
        easing.type: Easing.OutCubic
    }
    PreviewFrame {
        id: frame
        objectName: "previewFrame"
        x: preview.currentRect.x - preview.x
        y: preview.currentRect.y - preview.y
        width: preview.currentRect.width
        height: preview.currentRect.height
        accent: preview.accent
        surface: preview.surface
        outerEdge: preview.outerEdge
        innerEdge: preview.innerEdge
        cornerRadius: preview.cornerRadius
    }
}
