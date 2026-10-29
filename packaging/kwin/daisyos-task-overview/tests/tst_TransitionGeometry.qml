import QtQuick 2.15
import QtTest 1.3
import "../contents/ui/TransitionGeometry.js" as Transition

TestCase {
    name: "TransitionGeometry"

    readonly property var source: ({ x: 20, y: 40, width: 1200, height: 800 })
    readonly property var target: ({ x: 200, y: 140, width: 640, height: 420 })

    function test_exactEndpoints() {
        compare(JSON.stringify(Transition.interpolateRect(source, target, 0)), JSON.stringify(source))
        compare(JSON.stringify(Transition.interpolateRect(source, target, 1)), JSON.stringify(target))
    }

    function test_midpoint() {
        var result = Transition.interpolateRect(source, target, 0.5)
        compare(result.x, 110)
        compare(result.y, 90)
        compare(result.width, 920)
        compare(result.height, 610)
    }

    function test_reversalDurationUsesRemainingDistance() {
        compare(Transition.transitionDuration(280, 1, 0), 280)
        compare(Transition.transitionDuration(280, 0.25, 1), 210)
        compare(Transition.transitionDuration(280, 0.75, 0), 210)
    }

    function test_minimizedWindowStaysAtOverviewSlot() {
        compare(JSON.stringify(Transition.sourceRect(source, target, true, true)), JSON.stringify(target))
        compare(JSON.stringify(Transition.sourceRect(source, target, false, false)), JSON.stringify(target))
        compare(JSON.stringify(Transition.sourceRect(source, target, false, true)), JSON.stringify(source))
    }

    function test_modelReorderKeepsExistingIdentityOrder() {
        compare(JSON.stringify(Transition.stableMergeIds(["a", "b", "c"], ["c", "a", "b", "d"])),
                JSON.stringify(["a", "b", "c", "d"]))
        compare(JSON.stringify(Transition.stableMergeIds(["a", "b", "c"], ["c", "a"])),
                JSON.stringify(["a", "c"]))
    }

    function test_reconcileDeferral() {
        verify(Transition.shouldDeferReconcile("gesture"))
        verify(Transition.shouldDeferReconcile("preparing"))
        verify(Transition.shouldDeferReconcile("entering"))
        verify(Transition.shouldDeferReconcile("exiting"))
        verify(!Transition.shouldDeferReconcile("active"))
    }
}
