import QtQuick
import QtTest
import "../contents/ui/FlickMotion.js" as Flick

TestCase {
    name: "FlickMotion"
    function test_settlementPreservesVelocityAndHasAnExactEndpoint() {
        var dt = 0.001;
        var speed = (Flick.settlePosition(0, 1, 0.002, dt, 200)) / dt;
        fuzzyCompare(speed, 0.002, 0.000001);
        compare(Flick.settlePosition(0, 1, 0.002, 200, 200), 1);
        compare(Flick.settlePosition(1, 0, -0.002, 200, 200), 0);
        verify(Flick.settlePosition(0.5, 1, -0.001, 1, 200) < 0.5);
    }
    function test_singleSampleFlickAndPause() {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.12, 1000);
        verify(Flick.shouldCommit(tracker, 0.12, 1010));
        verify(!Flick.shouldCommit(tracker, 0.12, 1200));
    }
    function test_shortFlickUsesSpeedRatherThanHalfwayProjection_data() {
        return [
            { tag: "short-quick", distance: 0.04, elapsed: 20, commit: true },
            { tag: "short-slow", distance: 0.04, elapsed: 80, commit: false },
            { tag: "moderate-quick", distance: 0.09, elapsed: 60, commit: true },
            { tag: "moderate-slow", distance: 0.09, elapsed: 95, commit: false },
            { tag: "tiny-spike", distance: 0.02, elapsed: 1, commit: false }
        ];
    }
    function test_shortFlickUsesSpeedRatherThanHalfwayProjection(data) {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.005, 1000);
        Flick.update(tracker, data.distance, 1000 + data.elapsed);
        compare(Flick.shouldCommit(tracker, data.distance, 1008 + data.elapsed), data.commit);
        if (data.commit) {
            verify(!Flick.shouldCommit(tracker, data.distance, 1200 + data.elapsed));
            Flick.update(tracker, data.distance - 0.01, 1016 + data.elapsed);
            verify(!Flick.shouldCommit(tracker, data.distance - 0.01, 1020 + data.elapsed));
        }
    }
    function test_slowDragAndReversal() {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.02, 1000);
        Flick.update(tracker, 0.08, 1080);
        verify(!Flick.shouldCommit(tracker, 0.08, 1090));
        Flick.update(tracker, 0.65, 1100);
        Flick.update(tracker, 0.6, 1110);
        verify(!Flick.shouldCommit(tracker, 0.6, 1120));
    }
    function test_batchedSamplesPreserveVelocity() {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.06, 1000);
        Flick.update(tracker, 0.14, 1000);
        verify(Flick.shouldCommit(tracker, 0.14, 1010));
    }
    function test_smallReverseSamplesDiscardOutwardMomentum() {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.8, 1000);
        Flick.update(tracker, 0.7999, 1008);
        verify(Flick.velocity(tracker, 1008) > 0);
        Flick.update(tracker, 0.79, 1016);
        verify(Flick.velocity(tracker, 1016) < 0);
        verify(!Flick.shouldCommit(tracker, 0.79, 1016));
        tracker = Flick.createTracker();
        Flick.update(tracker, 0.8, 1000);
        for (var i = 1; i <= 6; ++i)
            Flick.update(tracker, 0.8 - i * 0.0005, 1000 + i * 8);
        verify(Flick.velocity(tracker, 1048) < 0);
    }
    function test_velocityDecaysDuringPause() {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.12, 1000);
        verify(Flick.velocity(tracker, 1080) < Flick.velocity(tracker, 1010));
        compare(Flick.velocity(tracker, 1101), 0);
    }
    function test_fastFlickSettlesSoonerWithBoundedDuration() {
        verify(Flick.settleDuration(0.8, 0.008, 280) < Flick.settleDuration(0.8, 0.002, 280));
        compare(Flick.settleDuration(1, 0, 280), 220);
        compare(Flick.settleDuration(0.01, 1, 280), 80);
    }
}
