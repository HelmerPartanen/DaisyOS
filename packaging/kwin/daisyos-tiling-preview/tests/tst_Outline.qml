import QtQuick
import QtTest
import ".."

TestCase {
    name: "DaisyTilingPreview"
    when: windowShown
    QtObject {
        id: source
        property bool active: false
        property rect geometry: Qt.rect(0, 0, 640, 720)
        property rect visualParentGeometry: Qt.rect(200, 140, 600, 400)
        // KWin emits this after geometryChanged. It must not settle the motion.
        signal unifiedGeometryChanged()
    }
    property var preview
    function init() {
        source.active = false;
        source.geometry = Qt.rect(0, 0, 640, 720);
        source.visualParentGeometry = Qt.rect(200, 140, 600, 400);
        preview = Qt.createComponent("../outline.qml").createObject(null, {outlineSource: source, themeConfigFile: Qt.resolvedUrl("defaults.ini").toString().replace("file://", "")});
        verify(preview !== null);
    }
    function cleanup() { source.active = false; preview.destroy(); wait(10); }
    function near(a, b) { verify(Math.abs(a - b) < 0.5, a + " != " + b); }
    function activate() { source.active = true; tryCompare(preview, "showing", true); }
    function test_revealStartsAtActualWindowAndSettlesAtKWinTarget() {
        activate();
        verify(preview.progress < 1);
        near(preview.fromRect.x, 200);
        near(preview.fromRect.width, 600);
        tryCompare(preview, "progress", 1);
        near(preview.currentRect.x, source.geometry.x);
        near(preview.currentRect.width, source.geometry.width);
    }
    function test_targetChangeAndUnifiedGeometryDoNotTeleport() {
        activate(); tryCompare(preview, "progress", 1);
        source.geometry = Qt.rect(640, 0, 640, 360);
        source.unifiedGeometryChanged();
        wait(35);
        verify(preview.currentRect.x > 0 && preview.currentRect.x < 640);
        var current = preview.currentRect;
        source.geometry = Qt.rect(0, 360, 640, 360);
        source.unifiedGeometryChanged();
        preview.synchronize();
        near(preview.fromRect.x, current.x);
        near(preview.currentRect.x, current.x);
        tryCompare(preview, "progress", 1);
        near(preview.currentRect.y, 360);
        near(preview.currentRect.x, 0);
    }
    function test_negativeOriginAndCrossOutputEnvelopeContainEntireSweep() {
        activate(); tryCompare(preview, "progress", 1);
        source.geometry = Qt.rect(-1280, -180, 640, 720);
        preview.synchronize();
        compare(preview.x, -1280); compare(preview.y, -180);
        verify(preview.width >= 1920 && preview.width <= 1921);
        verify(preview.height >= 900 && preview.height <= 901);
        wait(40);
        var r = preview.currentRect;
        verify(r.x >= preview.x && r.x + r.width <= preview.x + preview.width);
        verify(r.y >= preview.y && r.y + r.height <= preview.y + preview.height);
        tryCompare(preview, "progress", 1);
        near(preview.currentRect.x, -1280);
    }
    function test_duplicateShowDoesNotRestartAnimation() {
        activate(); wait(35);
        var progress = preview.progress;
        preview.synchronize();
        verify(preview.progress >= progress);
    }
    function test_reduceMotionSettlesImmediatelyAndHideLeavesNoPreview() {
        activate(); tryCompare(preview, "progress", 1);
        preview.reduceMotion = true;
        source.geometry = Qt.rect(640, 0, 640, 720);
        preview.synchronize();
        compare(preview.progress, 1);
        near(preview.currentRect.x, 640);
        source.active = false;
        compare(preview.visible, false);
        wait(200);
        compare(preview.visible, false);
    }
    function test_exportedPaletteAndReduceMotionAreReadOnActivation() {
        preview.themeConfigFile = Qt.resolvedUrl("palette.ini").toString().replace("file://", "");
        activate();
        compare(preview.accent, "#764ac9");
        compare(preview.surface, "#f2f2f2");
        compare(preview.cornerRadius, 22);
        compare(preview.reduceMotion, true);
        compare(preview.progress, 1);
        compare(findChild(preview.contentItem, "previewFrame").opacity, 1);
    }
    function test_noVisualParentAndRapidHideReopen() {
        source.visualParentGeometry = Qt.rect(0, 0, 0, 0);
        activate();
        near(preview.currentRect.width, 640);
        source.active = false;
        source.geometry = Qt.rect(640, 0, 640, 360);
        activate();
        near(preview.currentRect.x, 640);
        tryCompare(preview, "progress", 1);
    }
}
