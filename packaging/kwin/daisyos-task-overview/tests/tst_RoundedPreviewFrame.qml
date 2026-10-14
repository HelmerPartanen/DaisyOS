import QtQuick
import QtTest
import "../contents/ui" as Overview

Item {
    id: scene
    width: 100
    height: 100

    Rectangle { anchors.fill: parent; color: "black" }
    Overview.RoundedPreviewFrame {
        id: frame
        x: 10
        y: 10
        width: 80
        height: 80
        radius: 16
        color: "white"
        Rectangle { anchors.fill: parent; color: "red" }
    }

    TestCase {
        name: "RoundedPreviewFrame"
        when: windowShown

        function test_curvedEdgeHasAntialiasedPixels() {
            wait(50);
            var image = grabImage(scene);
            var blended = 0;
            for (var y = 10; y < 26; ++y) {
                for (var x = 10; x < 26; ++x) {
                    var red = image.red(x, y);
                    if (red > 0 && red < 255) ++blended;
                }
            }
            verify(blended >= 8, "Rounded edge must have blended coverage pixels");
        }

        function test_childrenStayInsideRoundedCorners() {
            wait(50);
            var image = grabImage(scene);
            compare(image.pixel(11, 11), Qt.rgba(0, 0, 0, 1));
            compare(image.pixel(88, 11), Qt.rgba(0, 0, 0, 1));
            compare(image.pixel(11, 88), Qt.rgba(0, 0, 0, 1));
            compare(image.pixel(88, 88), Qt.rgba(0, 0, 0, 1));
            compare(image.pixel(50, 11), Qt.rgba(1, 0, 0, 1));
            compare(image.pixel(50, 50), Qt.rgba(1, 0, 0, 1));
        }
    }
}
