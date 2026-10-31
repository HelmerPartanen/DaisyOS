import QtQuick
import org.kde.kwin as KWinComponents

// KWin owns recognition, including while the pointer is over another app.
Item {
    id: gestures
    required property var overview
    property var pending: []
    property bool callPending: false

    function sendPinch(opening, progress, finished, commit) {
        if (overview.visible) return;
        var message = { opening: opening, progress: progress, finished: finished, commit: commit };
        // Keep terminal messages in order, but replace stale motion samples.
        var tail = pending.length ? pending[pending.length - 1] : null;
        if (tail && !tail.finished && !finished && tail.opening === opening)
            pending[pending.length - 1] = message;
        else
            pending.push(message);
        flush();
    }

    function flush() {
        if (callPending || !pending.length) return;
        callPending = true;
        pinchCall.arguments = [JSON.stringify(pending.shift())];
        pinchCall.call();
    }

    KWinComponents.DBusCall {
        id: pinchCall
        objectName: "pinchCall"
        service: "org.daisyos.Shell.WindowBridge"
        path: "/org/daisyos/Shell/WindowBridge"
        dbusInterface: "org.daisyos.Shell.WindowBridge"
        method: "UpdateLauncherPinch"
        onFinished: { gestures.callPending = false; gestures.flush(); }
        onFailed: { gestures.callPending = false; gestures.pending = []; }
    }

    KWinComponents.PinchGestureHandler {
        id: pinchIn
        objectName: "pinchIn"
        fingerCount: 4
        direction: KWinComponents.PinchGestureHandler.Contracting
        property bool tracking: false
        onProgressChanged: {
            if (progress > 0) tracking = true;
            if (tracking) gestures.sendPinch(true, progress, false, false);
        }
        onActivated: {
            if (tracking) gestures.sendPinch(true, progress, true, true);
            tracking = false;
        }
        onCancelled: {
            if (tracking) gestures.sendPinch(true, progress, true, progress >= 0.5);
            tracking = false;
        }
    }
    KWinComponents.PinchGestureHandler {
        id: pinchOut
        objectName: "pinchOut"
        fingerCount: 4
        direction: KWinComponents.PinchGestureHandler.Expanding
        property bool tracking: false
        onProgressChanged: {
            if (progress > 0) tracking = true;
            if (tracking) gestures.sendPinch(false, progress, false, false);
        }
        onActivated: {
            if (tracking) gestures.sendPinch(false, progress, true, true);
            tracking = false;
        }
        onCancelled: {
            if (tracking) gestures.sendPinch(false, progress, true, progress >= 0.5);
            tracking = false;
        }
    }
    // A stationary held pinch is still live; let the shell distinguish it from
    // an effect unload or a lost bus connection without animating on a timer.
    Timer {
        interval: 500
        repeat: true
        running: pinchIn.tracking || pinchOut.tracking
        onTriggered: {
            if (pinchIn.tracking) gestures.sendPinch(true, pinchIn.progress, false, false);
            if (pinchOut.tracking) gestures.sendPinch(false, pinchOut.progress, false, false);
        }
    }
    KWinComponents.SwipeGestureHandler {
        objectName: "swipeUp"
        fingerCount: 3
        deviceType: KWinComponents.SwipeGestureHandler.Touchpad
        direction: KWinComponents.SwipeGestureHandler.Up
        property bool tracking: false
        onProgressChanged: {
            if (!tracking && progress > 0) tracking = overview.beginGesture(true);
            if (tracking) overview.updateGesture(progress);
        }
        onActivated: { if (tracking) overview.endGesture(true); tracking = false; }
        onCancelled: { if (tracking) overview.endGesture(progress >= 0.5); tracking = false; }
    }
    KWinComponents.SwipeGestureHandler {
        objectName: "swipeDown"
        fingerCount: 3
        deviceType: KWinComponents.SwipeGestureHandler.Touchpad
        direction: KWinComponents.SwipeGestureHandler.Down
        property bool tracking: false
        onProgressChanged: {
            if (!tracking && progress > 0) tracking = overview.beginGesture(false);
            if (tracking) overview.updateGesture(progress);
        }
        onActivated: { if (tracking) overview.endGesture(true); tracking = false; }
        onCancelled: { if (tracking) overview.endGesture(progress >= 0.5); tracking = false; }
    }
}
