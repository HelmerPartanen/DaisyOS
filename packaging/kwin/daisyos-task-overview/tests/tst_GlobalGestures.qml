import QtQuick
import QtTest
import "../contents/ui" as Overview

TestCase {
    id: testCase
    name: "GlobalGestures"
    property real clock: 1000
    Component {
        id: bridgeComponent
        Overview.GlobalGestures { }
    }
    QtObject {
        id: fakeOverview
        property bool visible: false
        property bool tracking: false
        property bool opening: false
        property real progress: 0
        property var committed: null
        function beginGesture(open) {
            if (tracking) return false;
            tracking = true;
            opening = open;
            return true;
        }
        function updateGesture(value) { progress = value; }
        function endGesture(commit) { tracking = false; committed = commit; }
    }
    property var bridge
    function init() {
        fakeOverview.visible = false;
        fakeOverview.tracking = false;
        fakeOverview.committed = null;
        clock = 1000;
        bridge = createTemporaryObject(bridgeComponent, this, {
            overview: fakeOverview, gestureClock: function() { return testCase.clock; }
        });
        verify(bridge !== null);
    }
    function test_pinchTracksBothDirectionsAndKeepsTerminalMessages() {
        var pinch = findChild(bridge, "pinchIn");
        var call = findChild(bridge, "pinchCall");
        compare(pinch.fingerCount, 4);
        pinch.progress = 0.2;
        compare(call.sent.length, 1);
        compare(call.sent[0].progress, 0.2);
        pinch.progress = 0.4;
        pinch.progress = 0.7;
        compare(bridge.pending.length, 1);
        pinch.cancelled(); // Release after halfway settles open.
        compare(bridge.pending.length, 2);
        call.finished([]);
        compare(call.sent[1].progress, 0.7);
        call.finished([]);
        verify(call.sent[2].finished);
        verify(call.sent[2].commit);
        call.finished([]);
        var out = findChild(bridge, "pinchOut");
        out.progress = 0.3;
        out.cancelled();
        call.finished([]);
        var terminal = call.sent[call.sent.length - 1];
        verify(!terminal.opening);
        verify(terminal.finished);
        verify(!terminal.commit);
    }
    function test_swipesTrackAndReverseBeforeRelease() {
        var up = findChild(bridge, "swipeUp");
        compare(up.fingerCount, 3);
        up.progress = 0.8;
        verify(fakeOverview.opening);
        compare(fakeOverview.progress, 0.8);
        up.progress = 0.2;
        compare(fakeOverview.progress, 0.2);
        up.cancelled();
        tryCompare(fakeOverview, "committed", false);
        var down = findChild(bridge, "swipeDown");
        down.progress = 0.65;
        verify(!fakeOverview.opening);
        down.cancelled();
        tryCompare(fakeOverview, "committed", true);
    }
    function test_smallReversalAboveHalfwayCancelsInsteadOfOpening() {
        var up = findChild(bridge, "swipeUp");
        up.progress = 0.8;
        clock += 8;
        up.progress = 0.7999;
        clock += 8;
        up.progress = 0.79;
        up.cancelled();
        tryCompare(fakeOverview, "committed", false);
        verify(!fakeOverview.tracking);
    }
    function test_shortFlickOpensAndClosesBeforeHalfway() {
        var up = findChild(bridge, "swipeUp");
        up.progress = 0.02;
        clock += 20;
        up.progress = 0.14;
        compare(fakeOverview.progress, 0.14);
        clock += 8;
        up.cancelled();
        tryCompare(fakeOverview, "committed", true);
        var down = findChild(bridge, "swipeDown");
        clock += 200;
        down.progress = 0.12;
        clock += 8;
        down.cancelled();
        verify(!fakeOverview.opening);
        tryCompare(fakeOverview, "committed", true);
    }
    function test_veryShortFlickOpensAndCloses_data() {
        return [{ tag: "open", handler: "swipeUp", opening: true },
                { tag: "close", handler: "swipeDown", opening: false }];
    }
    function test_veryShortFlickOpensAndCloses(data) {
        var swipe = findChild(bridge, data.handler);
        swipe.progress = 0.005;
        clock += 20;
        swipe.progress = 0.04;
        compare(fakeOverview.opening, data.opening);
        compare(fakeOverview.progress, 0.04);
        tryCompare(fakeOverview, "committed", null); // Stay under finger control until release.
        clock += 8;
        swipe.cancelled(); // KWin reports below-distance releases as cancelled.
        tryCompare(fakeOverview, "committed", true);
    }
    function test_pauseOrTinyMovementDoesNotBecomeAFlick() {
        var up = findChild(bridge, "swipeUp");
        up.progress = 0.15;
        clock += 200;
        up.cancelled();
        tryCompare(fakeOverview, "committed", false);
        clock += 200;
        up.progress = 0.02;
        clock += 8;
        up.cancelled();
        tryCompare(fakeOverview, "committed", false);
    }
    function test_directionHandoffCannotTurnAReversalIntoAnOpeningFlick() {
        var up = findChild(bridge, "swipeUp");
        up.progress = 0.02;
        clock += 16;
        up.progress = 0.1;
        up.cancelled();
        var down = findChild(bridge, "swipeDown");
        down.progress = 0.01;
        tryCompare(fakeOverview, "committed", false);
        verify(!up.tracking);
        verify(!down.tracking);
        wait(10);
        compare(fakeOverview.committed, false);
    }
    function test_axisReselectionDoesNotSettleWhileFingersStillMove() {
        var up = findChild(bridge, "swipeUp");
        up.progress = 0.02;
        up.cancelled();
        clock += 16;
        up.progress = 0.1;
        wait(10);
        compare(fakeOverview.committed, null);
        verify(fakeOverview.tracking);
        compare(fakeOverview.progress, 0.1);
        up.cancelled();
        tryCompare(fakeOverview, "committed", true);
    }
    function test_deferredReleaseUsesVelocityAtReleaseTime() {
        var up = findChild(bridge, "swipeUp");
        up.progress = 0.005;
        clock += 20;
        up.progress = 0.04;
        up.cancelled();
        clock += 500;
        tryCompare(fakeOverview, "committed", true);
    }
    function test_busFailureDropsStaleSamples() {
        var pinch = findChild(bridge, "pinchIn");
        var call = findChild(bridge, "pinchCall");
        pinch.progress = 0.1;
        pinch.progress = 0.3;
        call.failed();
        compare(bridge.pending.length, 0);
        verify(!bridge.callPending);
    }
    function test_pinchDoesNotOpenLauncherOverOverview() {
        fakeOverview.visible = true;
        var pinch = findChild(bridge, "pinchIn");
        pinch.progress = 0.8;
        pinch.activated();
        compare(findChild(bridge, "pinchCall").sent.length, 0);
    }
}
