import QtQuick
import org.kde.kwin as KWinComponents
import "FlickMotion.js" as Flick

// KWin owns recognition, including while the pointer is over another app.
Item {
    id: gestures
    required property var overview
    property var pending: []
    property bool callPending: false
    property var gestureClock: function() { return Date.now(); }

    property var cancelledSwipe: null
    property int cancellationGeneration: 0

    function updateSwipe(handler, opening, progress) {
        if (!isFinite(progress)) return;
        if (cancelledSwipe) {
            var previous = cancelledSwipe;
            cancelledSwipe = null;
            ++cancellationGeneration;
            if (previous !== handler) {
                // KWin cancels the old direction before reporting the new one
                // when fingers cross the gesture origin. This is a reversal,
                // not a release flick followed by a second gesture.
                previous.tracking = false;
                overview.endGesture(false, 0);
                return;
            }
            // The recognizer can also cancel and reselect the same direction
            // during axis selection. Keep the current finger position live.
        }
        if (!handler.tracking && progress > 0) {
            handler.tracking = overview.beginGesture(opening);
            if (handler.tracking) handler.flick = Flick.createTracker();
        }
        if (handler.tracking) {
            Flick.update(handler.flick, progress, gestureClock());
            overview.updateGesture(progress);
        }
    }

    function finishSwipe(handler, activated) {
        if (!handler.tracking) return;
        var now = gestureClock();
        var commit = activated || Flick.shouldCommit(handler.flick, handler.progress, now);
        var speed = Flick.velocity(handler.flick, now);
        if (activated) {
            cancelledSwipe = null;
            ++cancellationGeneration;
            handler.tracking = false;
            overview.endGesture(commit, speed);
            return;
        }
        // 'cancelled' also means an ordinary below-distance finger release.
        // Wait one event turn to distinguish it from a direction/axis handoff.
        // Capture release velocity now so dispatcher delay cannot erase a flick.
        cancelledSwipe = handler;
        var generation = ++cancellationGeneration;
        Qt.callLater(function() {
            if (generation !== gestures.cancellationGeneration) return;
            gestures.cancelledSwipe = null;
            handler.tracking = false;
            overview.endGesture(commit, speed);
        });
    }

    function sendPinch(opening, progress, finished, commit) {
        if (overview.visible) return;
        var handler = opening ? pinchIn : pinchOut;
        var now = gestureClock();
        Flick.update(handler.flick, progress, now);
        var message = { opening: opening, progress: progress, finished: finished, commit: commit,
                        velocity: Flick.velocity(handler.flick, now) };
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
        property var flick: Flick.createTracker()
        onProgressChanged: {
            if (!tracking && progress > 0) { flick = Flick.createTracker(); tracking = true; }
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
        property var flick: Flick.createTracker()
        onProgressChanged: {
            if (!tracking && progress > 0) { flick = Flick.createTracker(); tracking = true; }
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
        id: swipeUp
        objectName: "swipeUp"
        fingerCount: 3
        deviceType: KWinComponents.SwipeGestureHandler.Touchpad
        direction: KWinComponents.SwipeGestureHandler.Up
        property bool tracking: false
        property var flick: Flick.createTracker()
        onProgressChanged: gestures.updateSwipe(swipeUp, true, progress)
        onActivated: gestures.finishSwipe(swipeUp, true)
        onCancelled: gestures.finishSwipe(swipeUp, false)
    }
    KWinComponents.SwipeGestureHandler {
        id: swipeDown
        objectName: "swipeDown"
        fingerCount: 3
        deviceType: KWinComponents.SwipeGestureHandler.Touchpad
        direction: KWinComponents.SwipeGestureHandler.Down
        property bool tracking: false
        property var flick: Flick.createTracker()
        onProgressChanged: gestures.updateSwipe(swipeDown, false, progress)
        onActivated: gestures.finishSwipe(swipeDown, true)
        onCancelled: gestures.finishSwipe(swipeDown, false)
    }
}
