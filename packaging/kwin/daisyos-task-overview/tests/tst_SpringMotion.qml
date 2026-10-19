import QtQuick
import QtTest

TestCase {
    name: "DaisyEasedMotion"
    when: windowShown
    width: 200
    height: 200
    Item {
        id: moving
        property real progress: 0
        Behavior on progress {
            NumberAnimation { id: easedMotion; duration: 280; easing.type: Easing.OutCubic }
        }
    }
    function test_settles_and_reverses_without_jump() {
        moving.progress = 1
        wait(80)
        var before = moving.progress
        verify(before > 0 && before < 1)
        moving.progress = 0
        verify(Math.abs(moving.progress - before) < 0.01)
        tryCompare(easedMotion, "running", false, 2000)
        fuzzyCompare(moving.progress, 0, 0.001)
        moving.progress = 1
        for (var i = 0; i < 50; ++i) {
            wait(16)
            verify(moving.progress >= 0 && moving.progress <= 1.001)
        }
        tryCompare(easedMotion, "running", false, 2000)
        fuzzyCompare(moving.progress, 1, 0.001)
    }
}
