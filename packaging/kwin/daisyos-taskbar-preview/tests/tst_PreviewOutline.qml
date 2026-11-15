import QtQuick
import QtTest
import "../contents/ui" as Preview

Item {
    id: scene
    width: 100
    height: 100
    Rectangle { anchors.fill: parent; color: "black" }
    Rectangle {
        x: 10; y: 10; width: 80; height: 80; color: "gray"
        Preview.PreviewOutline { strokeColor: "white"; border.width: 2 }
        // Declared after the outline, as the thumbnail is in the actual popup.
        Rectangle { anchors.fill: parent; color: "blue" }
    }
    TestCase {
        name: "TaskbarPreviewOutline"
        when: windowShown
        function test_outlinePaintsAboveOpaquePreview() {
            wait(50);
            var image = grabImage(scene);
            verify(image.red(11, 50) > 180);
            verify(image.red(88, 50) > 180);
            verify(image.red(50, 11) > 180);
            verify(image.red(50, 88) > 180);
            compare(image.red(50, 50), 0);
            compare(image.blue(50, 50), 255);
        }
    }
}
