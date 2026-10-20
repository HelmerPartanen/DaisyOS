import QtQuick
import QtQuick.Controls
import org.kde.kwin as KWinComponents
import org.kde.kirigami as Kirigami
import "BentoLayout.js" as Bento
import "TransitionGeometry.js" as Transition
import "WindowRoles.js" as WindowRoles

FocusScope {
    id: root
    focus: true

    signal sessionReady(var view)

    readonly property QtObject targetScreen: KWinComponents.SceneView.screen
    readonly property QtObject currentDesktop: KWinComponents.SceneView.currentDesktop
    readonly property bool activeOutput: root.activeFocus
    readonly property real outerPadding: Number(effect.configuration.OuterPadding || 48)
    readonly property real tileSpacing: Number(effect.configuration.TileSpacing || 20)
    readonly property real maxPreviewScale: Number(effect.configuration.MaxPreviewScale || 1)
    readonly property real hoverScale: Number(effect.configuration.HoverScale || 1.012)
    readonly property real titleHeight: Number(effect.configuration.TitleBarHeight || 32)
    readonly property real captionButtonWidth: Number(effect.configuration.CaptionButtonWidth || 46)
    readonly property real captionGlyphSize: Number(effect.configuration.CaptionGlyphSize || 10)
    readonly property real titleFontPixels: Number(effect.configuration.TitleFontPixels || 13)
    readonly property string titleFontFamily: String(effect.configuration.TitleFontFamily || "Atkinson Hyperlegible")
    property bool sessionPrepared: false
    property bool cardsCommitted: false
    property bool reconcilePending: false
    property int selectedIndex: -1
    property int candidateRevision: 0

    function eligible(window) {
        if (!window || window.internal) return false;
        if (window.desktopWindow || window.dock || window.notification || window.criticalNotification
                || window.specialWindow || window.popupWindow) return false;
        if (window.desktops && window.desktops.length
                && window.desktops.indexOf(currentDesktop) < 0) return false;
        var currentActivity = String(KWinComponents.Workspace.currentActivity || "");
        if (currentActivity && window.activities && window.activities.length
                && window.activities.indexOf(currentActivity) < 0) return false;
        var desktopName = String(window.desktopFileName || "").toLowerCase();
        var resource = String(window.resourceClass || "").toLowerCase();
        var caption = String(window.caption || "").toLowerCase();
        return desktopName.indexOf("daisyos-shell") < 0
            && resource.indexOf("daisyos-shell") < 0
            && resource.indexOf("daisyos.shell") < 0
            && caption !== "daisyos shell";
    }

    function wallpaper(window) {
        return window && WindowRoles.isDaisyWallpaper(window.desktopFileName,
                                                       window.resourceClass,
                                                       window.resourceName,
                                                       window.caption,
                                                       window.layer,
                                                       window.stackingOrder);
    }

    function idFor(window) {
        return window ? String(window.internalId) : "";
    }

    function intersectsOutput(window) {
        return window
            && window.x + window.width > targetScreen.geometry.x
            && window.x < targetScreen.geometry.x + targetScreen.geometry.width
            && window.y + window.height > targetScreen.geometry.y
            && window.y < targetScreen.geometry.y + targetScreen.geometry.height;
    }

    function candidateWindows() {
        var result = [];
        for (var index = 0; index < candidateRepeater.count; ++index) {
            var candidate = candidateRepeater.itemAt(index);
            if (candidate && eligible(candidate.window)) result.push(candidate.window);
        }
        return result;
    }

    function featuredWindowIndex(windows) {
        var active = KWinComponents.Workspace.activeWindow;
        var featured = windows.length ? 0 : -1;
        var featuredArea = -1;
        for (var index = 0; index < windows.length; ++index) {
            var area = Math.max(1, windows[index].width) * Math.max(1, windows[index].height);
            if (area > featuredArea
                || (area === featuredArea && active && idFor(windows[index]) === idFor(active))) {
                featured = index;
                featuredArea = area;
            }
        }
        return featured;
    }

    function geometryDescriptor(window, count, logicalIndex, featuredIndex) {
        var filesWindow = WindowRoles.isFilesWindow(window.desktopFileName,
                                                     window.resourceClass,
                                                     window.resourceName,
                                                     window.caption);
        var sourceInset = filesWindow && !window.fullScreen ? 12 : 0;
        var sourceTitleCrop = !filesWindow && !window.noBorder && !window.fullScreen ? titleHeight : 0;
        var contentWidth = Math.max(1, window.width - sourceInset * 2);
        var contentHeight = Math.max(1, window.height - sourceTitleCrop - sourceInset * 2);
        var cell = Bento.layout(count, logicalIndex, Math.max(0, featuredIndex), overviewArea.width / overviewArea.height);
        var fitted = Bento.fittedRect(cell, contentWidth, contentHeight,
                                     overviewArea.width, overviewArea.height,
                                     tileSpacing, titleHeight, maxPreviewScale);
        var target = { x: fitted.x, y: fitted.y, width: fitted.width, height: fitted.height + titleHeight };
        var actual = {
            x: window.x - targetScreen.geometry.x - overviewArea.x,
            y: window.y - targetScreen.geometry.y - overviewArea.y,
            width: window.width,
            height: window.height
        };
        return {
            source: Transition.sourceRect(actual, target, window.minimized, intersectsOutput(window)),
            target: target,
            sourceInset: sourceInset,
            sourceTitleCrop: sourceTitleCrop,
            sourceContentWidth: contentWidth,
            sourceContentHeight: contentHeight
        };
    }

    function appendSnapshot(window, descriptor, targetAtSource) {
        sessionModel.append({
            client: window,
            persistentId: idFor(window),
            sourceX: descriptor.source.x,
            sourceY: descriptor.source.y,
            sourceWidth: descriptor.source.width,
            sourceHeight: descriptor.source.height,
            targetX: targetAtSource ? descriptor.source.x : descriptor.target.x,
            targetY: targetAtSource ? descriptor.source.y : descriptor.target.y,
            targetWidth: targetAtSource ? descriptor.source.width : descriptor.target.width,
            targetHeight: targetAtSource ? descriptor.source.height : descriptor.target.height,
            sourceInset: descriptor.sourceInset,
            sourceTitleCrop: descriptor.sourceTitleCrop,
            sourceContentWidth: descriptor.sourceContentWidth,
            sourceContentHeight: descriptor.sourceContentHeight,
            minimizedAtCapture: Boolean(window.minimized),
            capturedCaption: String(window.caption || window.resourceClass || "Application"),
            capturedStackingOrder: Number(window.stackingOrder || 0),
            closing: false
        });
    }

    function prepareSession() {
        if (!activeOutput || effect.phase !== effect.phasePreparing) return false;
        var preparedRevision = candidateRevision;
        cardsCommitted = false;
        sessionPrepared = false;
        reconcilePending = false;
        selectedIndex = -1;
        sessionModel.clear();
        var windows = candidateWindows();
        var featured = featuredWindowIndex(windows);
        for (var index = 0; index < windows.length; ++index) {
            appendSnapshot(windows[index], geometryDescriptor(windows[index], windows.length, index, featured), false);
        }
        selectedIndex = featured;
        sessionPrepared = true;
        Qt.callLater(function() {
            if (effect.phase !== effect.phasePreparing || !sessionPrepared) return;
            if (candidateRevision !== preparedRevision) {
                prepareSession();
                return;
            }
            cardsCommitted = true;
            sessionReady(root);
        });
        return true;
    }

    function beginEntry() { cardsCommitted = true; }

    function finishEntry() {
        if (reconcilePending) requestReconcile();
        ensureSelection();
    }

    function beginExit() { reconcileTimer.stop(); }

    function reverseTransition(entering) {
        cardsCommitted = true;
        if (entering && reconcilePending && effect.transitionProgress >= 1) requestReconcile();
    }

    function finishExit() {
        reconcileTimer.stop();
        cardsCommitted = false;
        sessionPrepared = false;
        reconcilePending = false;
        selectedIndex = -1;
        sessionModel.clear();
    }

    function sessionIndexForId(persistentId) {
        for (var index = 0; index < sessionModel.count; ++index) {
            if (String(sessionModel.get(index).persistentId) === String(persistentId)) return index;
        }
        return -1;
    }

    function requestReconcile() {
        if (!sessionPrepared) return;
        if (Transition.shouldDeferReconcile(effect.phase)) {
            reconcilePending = true;
            return;
        }
        if (effect.phase === effect.phaseActive) reconcileTimer.restart();
    }

    function reconcileSession() {
        if (effect.phase !== effect.phaseActive) {
            reconcilePending = true;
            return;
        }
        reconcilePending = false;
        var windows = candidateWindows();
        var present = {};
        var additions = [];
        for (var candidateIndex = 0; candidateIndex < windows.length; ++candidateIndex) {
            var persistentId = idFor(windows[candidateIndex]);
            present[persistentId] = windows[candidateIndex];
            if (sessionIndexForId(persistentId) < 0) additions.push(windows[candidateIndex]);
        }
        for (var row = 0; row < sessionModel.count; ++row) {
            var snapshot = sessionModel.get(row);
            if (!present[String(snapshot.persistentId)] && !snapshot.closing) {
                sessionModel.setProperty(row, "closing", true);
            }
        }
        var finalClients = [];
        for (var existingIndex = 0; existingIndex < sessionModel.count; ++existingIndex) {
            if (!sessionModel.get(existingIndex).closing && sessionModel.get(existingIndex).client) {
                finalClients.push(sessionModel.get(existingIndex).client);
            }
        }
        for (var finalAddIndex = 0; finalAddIndex < additions.length; ++finalAddIndex) {
            finalClients.push(additions[finalAddIndex]);
        }
        var existingCount = finalClients.length - additions.length;
        var featured = featuredWindowIndex(finalClients);
        for (var addIndex = 0; addIndex < additions.length; ++addIndex) {
            var descriptor = geometryDescriptor(additions[addIndex], Math.max(1, finalClients.length), existingCount + addIndex, featured);
            appendSnapshot(additions[addIndex], descriptor, !additions[addIndex].minimized);
        }
        Qt.callLater(root.recomputeTargets);
    }

    function recomputeTargets() {
        if (effect.phase !== effect.phaseActive) {
            reconcilePending = true;
            return;
        }
        var rows = [];
        for (var index = 0; index < sessionModel.count; ++index) {
            if (!sessionModel.get(index).closing) rows.push(index);
        }
        var clients = [];
        for (var logical = 0; logical < rows.length; ++logical) clients.push(sessionModel.get(rows[logical]).client);
        var featured = featuredWindowIndex(clients);
        for (logical = 0; logical < rows.length; ++logical) {
            var row = rows[logical];
            var client = sessionModel.get(row).client;
            if (!client) continue;
            var descriptor = geometryDescriptor(client, rows.length, logical, featured);
            if (sessionModel.get(row).minimizedAtCapture) {
                sessionModel.setProperty(row, "sourceX", descriptor.target.x);
                sessionModel.setProperty(row, "sourceY", descriptor.target.y);
                sessionModel.setProperty(row, "sourceWidth", descriptor.target.width);
                sessionModel.setProperty(row, "sourceHeight", descriptor.target.height);
            }
            sessionModel.setProperty(row, "targetX", descriptor.target.x);
            sessionModel.setProperty(row, "targetY", descriptor.target.y);
            sessionModel.setProperty(row, "targetWidth", descriptor.target.width);
            sessionModel.setProperty(row, "targetHeight", descriptor.target.height);
        }
        ensureSelection();
    }

    function finalizeRemoval(persistentId) {
        var index = sessionIndexForId(persistentId);
        if (index < 0 || !sessionModel.get(index).closing) return;
        sessionModel.remove(index);
        if (selectedIndex === index) selectedIndex = -1;
        else if (selectedIndex > index) selectedIndex -= 1;
        Qt.callLater(root.recomputeTargets);
    }

    function ensureSelection() {
        if (sessionModel.count === 0) {
            selectedIndex = -1;
            return;
        }
        if (selectedIndex < 0 || selectedIndex >= sessionModel.count || sessionModel.get(selectedIndex).closing) {
            selectedIndex = -1;
            for (var index = 0; index < sessionModel.count; ++index) {
                if (!sessionModel.get(index).closing) {
                    selectedIndex = index;
                    break;
                }
            }
        }
    }

    function selectSpatial(dx, dy) {
        ensureSelection();
        var current = cardRepeater.itemAt(selectedIndex);
        if (!current) return;
        var currentX = current.targetX + current.targetWidth / 2;
        var currentY = current.targetY + current.targetHeight / 2;
        var best = -1;
        var bestScore = Number.POSITIVE_INFINITY;
        for (var index = 0; index < cardRepeater.count; ++index) {
            if (index === selectedIndex) continue;
            var item = cardRepeater.itemAt(index);
            if (!item || item.closing) continue;
            var xDistance = item.targetX + item.targetWidth / 2 - currentX;
            var yDistance = item.targetY + item.targetHeight / 2 - currentY;
            if ((dx < 0 && xDistance >= 0) || (dx > 0 && xDistance <= 0)
                || (dy < 0 && yDistance >= 0) || (dy > 0 && yDistance <= 0)) continue;
            var primary = dx !== 0 ? Math.abs(xDistance) : Math.abs(yDistance);
            var secondary = dx !== 0 ? Math.abs(yDistance) : Math.abs(xDistance);
            var score = primary + secondary * 2;
            if (score < bestScore) { bestScore = score; best = index; }
        }
        if (best >= 0) selectedIndex = best;
    }

    Keys.onPressed: event => {
        if (!root.activeOutput) return;
        switch (event.key) {
        case Qt.Key_Escape:
            effect.deactivate(); event.accepted = true; break;
        case Qt.Key_Left:
            selectSpatial(-1, 0); event.accepted = true; break;
        case Qt.Key_Right:
            selectSpatial(1, 0); event.accepted = true; break;
        case Qt.Key_Up:
            selectSpatial(0, -1); event.accepted = true; break;
        case Qt.Key_Down:
            selectSpatial(0, 1); event.accepted = true; break;
        case Qt.Key_Return:
        case Qt.Key_Enter:
            var selected = cardRepeater.itemAt(selectedIndex);
            if (selected && selected.client) effect.activateWindow(selected.client);
            event.accepted = true;
            break;
        case Qt.Key_Delete:
        case Qt.Key_Backspace:
            var closing = cardRepeater.itemAt(selectedIndex);
            if (closing && closing.client && closing.client.closeable) closing.client.closeWindow();
            event.accepted = true;
            break;
        }
    }

    Repeater {
        id: candidateRepeater
        // Keep a stable, complete source model and take the
        // desktop/activity/type snapshot imperatively.
        model: KWinComponents.WindowModel { }
        delegate: Item {
            required property QtObject window
            visible: false
            width: 0
            height: 0
        }
        onItemAdded: {
            root.candidateRevision += 1;
            root.requestReconcile();
        }
        onItemRemoved: {
            root.candidateRevision += 1;
            root.requestReconcile();
        }
    }

    ListModel { id: sessionModel; dynamicRoles: true }

    Timer { id: reconcileTimer; interval: 40; onTriggered: root.reconcileSession() }

    Repeater {
        model: KWinComponents.WindowModel { }
        delegate: KWinComponents.WindowThumbnail {
            required property QtObject window
            wId: window.internalId
            x: window.x - root.targetScreen.geometry.x
            y: window.y - root.targetScreen.geometry.y
            width: window.width
            height: window.height
            z: window.stackingOrder
            visible: root.intersectsOutput(window)
                     && !window.minimized
                     && (!root.activeOutput || !root.cardsCommitted || root.wallpaper(window))
        }
    }

    Rectangle {
        anchors.fill: parent
        color: "#1f000000"
        visible: root.activeOutput && root.cardsCommitted
        opacity: effect.transitionProgress
        z: 100000
        TapHandler { onTapped: effect.deactivate() }
    }

    Item {
        id: overviewArea
        x: root.outerPadding
        y: root.outerPadding
        width: Math.max(1, root.width - root.outerPadding * 2)
        height: Math.max(1, root.height - root.outerPadding * 2)
        visible: root.activeOutput && root.sessionPrepared
        z: 100001

        Repeater {
            id: cardRepeater
            model: sessionModel

            delegate: Item {
                id: tile
                required property int index
                required property var client
                required property string persistentId
                required property real sourceX
                required property real sourceY
                required property real sourceWidth
                required property real sourceHeight
                required property real targetX
                required property real targetY
                required property real targetWidth
                required property real targetHeight
                required property real sourceInset
                required property real sourceTitleCrop
                required property real sourceContentWidth
                required property real sourceContentHeight
                required property bool minimizedAtCapture
                required property string capturedCaption
                required property real capturedStackingOrder
                required property bool closing
                readonly property bool selected: root.selectedIndex === index
                readonly property var transitionRect: Transition.interpolateRect(
                    { x: sourceX, y: sourceY, width: sourceWidth, height: sourceHeight },
                    { x: targetX, y: targetY, width: targetWidth, height: targetHeight },
                    effect.transitionProgress)

                x: transitionRect.x
                y: transitionRect.y
                width: transitionRect.width
                height: transitionRect.height
                opacity: closing ? 0 : 1
                z: selected ? 10000 : capturedStackingOrder
                transformOrigin: Item.Center
                // Keyboard selection controls navigation only. A preview grows
                // exclusively while the pointer is physically over that tile.
                scale: closing ? 0.96
                    : (effect.phase === effect.phaseActive && hover.hovered ? root.hoverScale : 1)

                Behavior on x { enabled: !effect.reduceMotion && effect.phase === effect.phaseActive; NumberAnimation { duration: 220; easing.type: Easing.OutCubic } }
                Behavior on y { enabled: !effect.reduceMotion && effect.phase === effect.phaseActive; NumberAnimation { duration: 220; easing.type: Easing.OutCubic } }
                Behavior on width { enabled: !effect.reduceMotion && effect.phase === effect.phaseActive; NumberAnimation { duration: 220; easing.type: Easing.OutCubic } }
                Behavior on height { enabled: !effect.reduceMotion && effect.phase === effect.phaseActive; NumberAnimation { duration: 220; easing.type: Easing.OutCubic } }
                Behavior on opacity { enabled: effect.phase === effect.phaseActive; NumberAnimation { duration: 140; easing.type: Easing.InOutCubic } }
                Behavior on scale {
                    enabled: effect.phase === effect.phaseActive
                    NumberAnimation { duration: closing ? 140 : 120; easing.type: Easing.InOutCubic }
                }

                Timer {
                    interval: 140
                    running: tile.closing && effect.phase === effect.phaseActive
                    onTriggered: root.finalizeRemoval(tile.persistentId)
                }

                Rectangle {
                    id: outerFrame
                    anchors.fill: parent
                    radius: Number(effect.configuration.CornerRadius || 16)
                    color: effect.configuration.FrameOuter || "#99000000"
                    clip: true
                    Behavior on color { ColorAnimation { duration: 120; easing.type: Easing.InOutCubic } }

                    Rectangle {
                        anchors.fill: parent
                        anchors.margins: 1
                        radius: Math.max(0, outerFrame.radius - 1)
                        color: effect.configuration.FrameInner || "#26ffffff"

                        RoundedPreviewFrame {
                            id: contentFrame
                            anchors.fill: parent
                            anchors.margins: 1
                            radius: Math.max(0, outerFrame.radius - 2)
                            color: effect.configuration.SurfaceBase || "#ff121214"
                            clip: true

                            Rectangle {
                                id: titleBar
                                anchors.left: parent.left
                                anchors.right: parent.right
                                anchors.top: parent.top
                                height: root.titleHeight
                                color: tile.selected
                                    ? (effect.configuration.SurfaceBase || "#ff121214")
                                    : (effect.configuration.SurfaceRaised || "#ff1a1a1d")

                                Kirigami.Icon {
                                    anchors.left: parent.left
                                    anchors.leftMargin: 10
                                    anchors.verticalCenter: parent.verticalCenter
                                    width: 18
                                    height: 18
                                    source: tile.client ? tile.client.icon : ""
                                }

                                Text {
                                    anchors.left: parent.left
                                    anchors.leftMargin: 36
                                    anchors.right: closeButton.left
                                    anchors.rightMargin: 10
                                    anchors.verticalCenter: parent.verticalCenter
                                    text: tile.capturedCaption
                                    color: tile.selected
                                        ? (effect.configuration.ContentPrimary || "#fff5f5f7")
                                        : (effect.configuration.ContentDisabled || "#ff98989f")
                                    font.features: ({ "tnum": 0, "pnum": 1 })
                                    font.family: root.titleFontFamily
                                    font.pixelSize: root.titleFontPixels
                                    font.weight: Font.Medium
                                    elide: Text.ElideRight
                                }

                                Rectangle {
                                    id: closeButton
                                    anchors.right: parent.right
                                    anchors.top: parent.top
                                    width: Math.min(root.captionButtonWidth, titleBar.width)
                                    height: root.titleHeight
                                    color: closeTap.pressed || closeHover.hovered
                                        ? (effect.configuration.StatusDanger || "#ffff383c") : "transparent"
                                    visible: tile.client && tile.client.closeable && effect.phase === effect.phaseActive
                                    Behavior on color { ColorAnimation { duration: 100; easing.type: Easing.InOutCubic } }

                                    Rectangle {
                                        anchors.centerIn: parent
                                        width: root.captionGlyphSize
                                        height: 1.45
                                        radius: 0.725
                                        rotation: 45
                                        color: closeHover.hovered || closeTap.pressed
                                            ? (effect.configuration.ContentOnAction || "#ffffffff")
                                            : (effect.configuration.ContentDisabled || "#ff98989f")
                                    }
                                    Rectangle {
                                        anchors.centerIn: parent
                                        width: root.captionGlyphSize
                                        height: 1.45
                                        radius: 0.725
                                        rotation: -45
                                        color: closeHover.hovered || closeTap.pressed
                                            ? (effect.configuration.ContentOnAction || "#ffffffff")
                                            : (effect.configuration.ContentDisabled || "#ff98989f")
                                    }
                                    HoverHandler { id: closeHover }
                                    TapHandler { id: closeTap; onTapped: if (tile.client) tile.client.closeWindow() }
                                }
                            }

                            Item {
                                id: previewViewport
                                anchors.left: parent.left
                                anchors.right: parent.right
                                anchors.top: titleBar.bottom
                                anchors.bottom: parent.bottom
                                clip: true

                                KWinComponents.WindowThumbnail {
                                    readonly property real sourceScale: previewViewport.width / Math.max(1, tile.sourceContentWidth)
                                    width: (tile.sourceContentWidth + tile.sourceInset * 2) * sourceScale
                                    height: (tile.sourceContentHeight + tile.sourceTitleCrop + tile.sourceInset * 2) * sourceScale
                                    x: -(tile.sourceInset * sourceScale)
                                    y: -((tile.sourceInset + tile.sourceTitleCrop) * sourceScale)
                                    wId: tile.client ? tile.client.internalId : null
                                    smooth: true
                                }
                            }
                        }
                    }
                }

                HoverHandler {
                    id: hover
                    enabled: effect.phase === effect.phaseActive
                    onHoveredChanged: if (hovered) root.selectedIndex = tile.index
                }
                TapHandler {
                    enabled: effect.phase === effect.phaseActive
                    acceptedButtons: Qt.LeftButton
                    onTapped: if (tile.client) effect.activateWindow(tile.client)
                }
                TapHandler {
                    enabled: effect.phase === effect.phaseActive
                    acceptedButtons: Qt.MiddleButton
                    onTapped: if (tile.client && tile.client.closeable) tile.client.closeWindow()
                }
            }
        }

        Rectangle {
            anchors.centerIn: parent
            visible: sessionModel.count === 0 && effect.phase === effect.phaseActive
            width: emptyLabel.implicitWidth + 48
            height: 44
            radius: 22
            color: effect.configuration.SurfaceRaised || "#ff1a1a1d"
            border.width: 1
            border.color: effect.configuration.FrameInner || "#26ffffff"
            Text {
                id: emptyLabel
                anchors.centerIn: parent
                text: "No active windows"
                color: effect.configuration.ContentDisabled || "#ff98989f"
                font.features: ({ "tnum": 0, "pnum": 1 })
                font.family: root.titleFontFamily
                font.pixelSize: Math.max(11, root.titleFontPixels + 1)
                font.weight: Font.Medium
            }
        }
    }
}
