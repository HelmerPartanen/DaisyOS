import QtQuick
import QtTest
import "../contents/ui/FlickMotion.js" as Flick

TestCase {
    name: "FlickMotion"
    function test_singleSampleFlickAndPause() {
        var tracker = Flick.createTracker();
        Flick.update(tracker, 0.12, 1000);
        verify(Flick.shouldCommit(tracker, 0.12, 1010));
        verify(!Flick.shouldCommit(tracker, 0.12, 1200));
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
    function test_fastFlickSettlesSoonerWithBoundedDuration() {
        verify(Flick.settleDuration(0.8, 0.008, 280) < Flick.settleDuration(0.8, 0.002, 280));
        compare(Flick.settleDuration(1, 0, 280), 220);
        compare(Flick.settleDuration(0.01, 1, 280), 80);
    }
}
