/*
 * DaisyOS task overview. KWin owns the scene, live thumbnails, input and all
 * motion; the Avalonia shell only asks this effect to toggle.
 */
import QtQuick
import org.kde.kwin as KWinComponents
import "TransitionGeometry.js" as Transition
import "FlickMotion.js" as Flick

KWinComponents.SceneEffect {
    id: effect

    readonly property string phaseIdle: "idle"
    readonly property string phasePreparing: "preparing"
    readonly property string phaseEntering: "entering"
    readonly property string phaseActive: "active"
    readonly property string phaseGesture: "gesture"
    property bool gestureTracking: false
    property bool gestureOpening: true
    property real gestureProgress: 0
    property real gestureStartProgress: 0
    property real settleFraction: 0
    property real settleFrom: 0
    property real settleTo: 1
    property real settleVelocity: 0
    onSettleFractionChanged: {
        if (transitionAnimation.running)
            transitionProgress = Math.max(0, Math.min(1, Flick.settlePosition(
                settleFrom, settleTo, settleVelocity, settleFraction * settleDuration, settleDuration)));
    }
    property int settleDuration: animationDuration
    property int gestureTarget: -1
    property real gestureReleaseVelocity: 0
    readonly property string phaseExiting: "exiting"
    property string phase: phaseIdle
    readonly property bool activated: phase === phaseEntering || phase === phaseActive || phase === phaseGesture
    readonly property bool reduceMotion: Boolean(configuration.ReduceMotion)
    property real transitionProgress: 0
    property QtObject pendingActivation: null
    readonly property int animationDuration: Math.max(1, Number(configuration.AnimationDuration || 280))

    GlobalGestures { objectName: "overviewGestures"; overview: effect }

    delegate: ScreenView {
        onSessionReady: view => effect.handleSessionReady(view)
    }

    // Own the settle animation explicitly. An animation nested in a Behavior
    // cannot be stopped directly when fingers take control of the same value.
    NumberAnimation {
        id: transitionAnimation
        target: effect
        property: "settleFraction"
        from: 0
        to: 1
        duration: effect.settleDuration
        easing.type: Easing.Linear
        onFinished: {
            effect.transitionProgress = effect.settleTo;
            effect.finishTransition();
        }
    }

    Timer {
        id: prepareTimer
        interval: 1
        repeat: true
        onTriggered: {
            if (effect.phase !== effect.phasePreparing) {
                stop();
                return;
            }
            var view = effect.activeView ? effect.activeView.rootItem : null;
            if (view && view.prepareSession()) {
                stop();
            }
        }
    }

    KWinComponents.ShortcutHandler {
        name: "DaisyOS Task Overview"
        text: "Toggle DaisyOS Task Overview"
        sequence: "Meta+Tab"
        onActivated: {
            console.warn("[daisy-overview-input] Meta+Tab shortcut activated by KWin; visible=" + effect.visible + "; activated=" + effect.activated)
            effect.toggle()
        }
    }

    function beginGesture(opening) {
        if (gestureTracking) return false;
        if (opening ? phase !== phaseIdle && phase !== phaseExiting
                    : phase !== phaseActive && phase !== phaseEntering) return false;
        gestureOpening = opening;
        gestureStartProgress = transitionProgress;
        gestureProgress = 0;
        gestureTarget = -1;
        gestureTracking = true;
        if (phase === phaseIdle) {
            activate();
        } else {
            // Stop autonomous motion at its displayed position before tracking.
            var displayed = transitionProgress;
            phase = phaseGesture;
            transitionAnimation.stop();
            transitionProgress = displayed;
            var view = activeRoot();
            if (view) view.reverseTransition(opening);
        }
        return true;
    }

    function updateGesture(progress) {
        if (!gestureTracking) return;
        if (!isFinite(progress)) return;
        gestureProgress = Math.max(0, Math.min(1, progress));
        if (phase === phaseGesture)
            transitionProgress = Transition.gesturePosition(gestureStartProgress, gestureOpening, gestureProgress);
    }

    function endGesture(commit, releaseVelocity) {
        gestureReleaseVelocity = isFinite(releaseVelocity)
            ? (gestureOpening ? 1 : -1) * releaseVelocity : 0;
        if (!gestureTracking) return;
        gestureTracking = false;
        var target = commit ? (gestureOpening ? 1 : 0) : (gestureOpening ? 0 : 1);
        if (phase === phasePreparing) {
            gestureTarget = target;
            return;
        }
        phase = target ? phaseEntering : phaseExiting;
        var view = activeRoot();
        if (view && !target) view.beginExit();
        animateTo(target, gestureReleaseVelocity);
        if (transitionProgress === target && !transitionAnimation.running) finishTransition();
    }

    function animateTo(targetProgress, releaseVelocity) {
        var displayed = transitionProgress;
        transitionAnimation.stop();
        var unchanged = displayed === targetProgress;
        settleDuration = isFinite(releaseVelocity)
            ? Flick.settleDuration(targetProgress - transitionProgress, releaseVelocity, animationDuration)
            : Math.max(80, Math.round(animationDuration * Math.abs(targetProgress - transitionProgress)));
        if (reduceMotion || unchanged) {
            transitionProgress = targetProgress;
            finishTransition();
        } else {
            settleFrom = displayed;
            settleTo = targetProgress;
            // Discrete actions retain the existing cubic-out curve. Gestures
            // carry their measured velocity into the first settling frame.
            settleVelocity = isFinite(releaseVelocity) ? releaseVelocity
                : 3 * (targetProgress - displayed) / settleDuration;
            settleFraction = 0;
            transitionAnimation.start();
        }
    }

    function activeRoot() {
        return activeView ? activeView.rootItem : null;
    }

    function handleSessionReady(view) {
        if (phase !== phasePreparing || view !== activeRoot()) {
            return;
        }
        view.beginEntry();
        if (gestureTracking) {
            phase = phaseGesture;
            transitionProgress = Transition.gesturePosition(gestureStartProgress, gestureOpening, gestureProgress);
        } else if (gestureTarget >= 0) {
            // Preparation is asynchronous. Start settling at the last finger
            // position, including when release arrived before the first frame.
            phase = phaseGesture;
            transitionProgress = Transition.gesturePosition(gestureStartProgress, gestureOpening, gestureProgress);
            var target = gestureTarget;
            gestureTarget = -1;
            phase = target ? phaseEntering : phaseExiting;
            if (!target) view.beginExit();
            animateTo(target, gestureReleaseVelocity);
        } else {
            phase = phaseEntering;
            animateTo(1);
        }
    }

    function finishTransition() {
        var view = activeRoot();
        if (phase === phaseEntering) {
            phase = phaseActive;
            if (view) view.finishEntry();
            return;
        }
        if (phase !== phaseExiting) {
            return;
        }
        if (view) view.finishExit();
        var activation = pendingActivation;
        pendingActivation = null;
        phase = phaseIdle;
        transitionProgress = 0;
        if (activation) {
            KWinComponents.Workspace.activeWindow = activation;
        }
        visible = false;
    }

    function toggle() {
        if (gestureTracking) { endGesture(false); return; }
        if (phase === phaseActive || phase === phaseEntering) {
            deactivate();
        } else if (phase === phaseExiting) {
            activate();
        } else if (phase === phasePreparing) {
            deactivate();
        } else {
            activate();
        }
    }

    function activate() {
        pendingActivation = null;
        if (phase === phaseActive || phase === phaseEntering || phase === phasePreparing) {
            return;
        }
        if (phase === phaseExiting) {
            phase = phaseEntering;
            var reversingView = activeRoot();
            if (reversingView) reversingView.reverseTransition(true);
            animateTo(1);
            return;
        }
        phase = phasePreparing;
        transitionProgress = 0;
        visible = true;
        prepareTimer.restart();
        console.warn("[daisy-overview-input] overview entered")
    }

    function deactivate() {
        gestureTracking = false;
        gestureTarget = -1;
        if (phase === phaseIdle || phase === phaseExiting) {
            return;
        }
        if (phase === phasePreparing) {
            prepareTimer.stop();
            var preparingView = activeRoot();
            if (preparingView) preparingView.finishExit();
            phase = phaseIdle;
            transitionProgress = 0;
            visible = false;
            return;
        }
        phase = phaseExiting;
        var view = activeRoot();
        if (view) view.beginExit();
        console.warn("[daisy-overview-input] overview exit requested")
        animateTo(0);
    }

    function activateWindow(window) {
        if (!window) {
            return;
        }
        pendingActivation = window;
        deactivate();
    }
}
