/*
 * DaisyOS task overview. KWin owns the scene, live thumbnails, input and all
 * motion; the Avalonia shell only asks this effect to toggle.
 */
import QtQuick
import org.kde.kwin as KWinComponents
import "TransitionGeometry.js" as Transition

KWinComponents.SceneEffect {
    id: effect

    readonly property string phaseIdle: "idle"
    readonly property string phasePreparing: "preparing"
    readonly property string phaseEntering: "entering"
    readonly property string phaseActive: "active"
    readonly property string phaseExiting: "exiting"
    property string phase: phaseIdle
    readonly property bool activated: phase === phaseEntering || phase === phaseActive
    readonly property bool reduceMotion: Boolean(configuration.ReduceMotion)
    property real transitionProgress: 0
    property QtObject pendingActivation: null
    readonly property int animationDuration: Math.max(1, Number(configuration.AnimationDuration || 280))

    delegate: ScreenView {
        onSessionReady: view => effect.handleSessionReady(view)
    }

    Behavior on transitionProgress {
        enabled: !effect.reduceMotion && (effect.phase === effect.phaseEntering || effect.phase === effect.phaseExiting)
        NumberAnimation {
            id: transitionAnimation
            duration: effect.animationDuration
            easing.type: effect.phase === effect.phaseExiting ? Easing.InCubic : Easing.OutCubic
            onRunningChanged: {
                if (!running) effect.finishTransition();
            }
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

    function animateTo(targetProgress) {
        transitionProgress = targetProgress;
        if (reduceMotion) finishTransition();
    }

    function activeRoot() {
        return activeView ? activeView.rootItem : null;
    }

    function handleSessionReady(view) {
        if (phase !== phasePreparing || view !== activeRoot()) {
            return;
        }
        phase = phaseEntering;
        view.beginEntry();
        animateTo(1);
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
