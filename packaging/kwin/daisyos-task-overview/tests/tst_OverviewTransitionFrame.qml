import QtQuick
import QtTest
import "../contents/ui" as Overview

TestCase {
    name: "OverviewTransitionFrame"
    width: 1600
    height: 1000
    Overview.OverviewTransitionFrame {
        id: frame
        x: 20
        y: 30
        width: 400
        height: 250
        presentationWidth: 400
        presentationHeight: 250
        Item { id: textureContent; anchors.fill: parent }
    }
    function test_tracksAndReversesWithoutResizingTextureContent() {
        var widths = [1200, 800, 600, 950, 400];
        var heights = [800, 550, 400, 650, 250];
        for (var i = 0; i < widths.length; ++i) {
            frame.presentationWidth = widths[i];
            frame.presentationHeight = heights[i];
            var corner = frame.mapToItem(this, frame.width, frame.height);
            verify(Math.abs(corner.x - frame.x - widths[i]) < 0.001);
            verify(Math.abs(corner.y - frame.y - heights[i]) < 0.001);
            compare(textureContent.width, 400);
            compare(textureContent.height, 250);
        }
    }
}
