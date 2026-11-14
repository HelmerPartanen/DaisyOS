import QtQuick
import QtTest
import "../contents/ui/PreviewGeometry.js" as Geometry

TestCase {
    name: "TaskbarPreviewGeometry"
    function test_nativeBottomPanelCoordinates() {
        var screen = {x:0, y:0, width:2560, height:1440};
        var anchor = Geometry.resolveAnchor(screen, {anchorX:1348/2560, anchorY:36/88,
            anchorWidth:40/2560, anchorHeight:40/88, panelHeight:88});
        compare(anchor.anchorY * screen.height, 1388);
        compare(anchor.anchorHeight * screen.height, 40);
        var result = Geometry.layout(screen, anchor, 1);
        compare(result.y + result.height + 12, 1388);
    }
    function test_selectedDockEdges_data() {
        return [{tag:"Left", edge:"Left"}, {tag:"Right", edge:"Right"}, {tag:"Top", edge:"Top"}];
    }
    function test_selectedDockEdges(data) {
        var screen = {x:0, y:0, width:1920, height:1080};
        var vertical = data.edge === "Left" || data.edge === "Right";
        var anchor = Geometry.resolveAnchor(screen, {anchorX:vertical ? 3/82 : 0.5, anchorY:vertical ? 0.5 : 3/82,
            anchorWidth:vertical ? 40/82 : 40/1920, anchorHeight:vertical ? 40/1080 : 40/82,
            panelWidth:vertical ? 82 : 1920, panelHeight:vertical ? 1080 : 82, dockEdge:data.edge});
        var preview = Geometry.layout(screen, anchor, 20);
        if (data.edge === "Left") verify(preview.x > (anchor.anchorX + anchor.anchorWidth) * screen.width);
        if (data.edge === "Right") verify(preview.x + preview.width < anchor.anchorX * screen.width);
        if (data.edge === "Top") verify(preview.y > (anchor.anchorY + anchor.anchorHeight) * screen.height);
    }
    function test_staysInsideOutput_data() {
        return [
            {tag: "left", x:0, y:0, w:1920, h:1080, ax:0, ay:0.95, count:1},
            {tag: "right", x:0, y:0, w:1920, h:1080, ax:0.98, ay:0.95, count:3},
            {tag: "many", x:0, y:0, w:1280, h:720, ax:0.5, ay:0.9, count:20},
            {tag: "negative-output", x:-1920, y:0, w:1920, h:1080, ax:0.5, ay:0.95, count:2},
            {tag: "top-panel", x:0, y:-1080, w:1920, h:1080, ax:0.5, ay:0.01, count:1},
            {tag: "small", x:0, y:0, w:320, h:240, ax:0.8, ay:0.9, count:4}
        ];
    }
    function test_staysInsideOutput(data) {
        var screen = {x:data.x, y:data.y, width:data.w, height:data.h};
        var result = Geometry.layout(screen, {anchorX:data.ax, anchorY:data.ay, anchorWidth:0.02, anchorHeight:0.04}, data.count);
        verify(result.x >= screen.x + 12);
        verify(result.y >= screen.y + 12);
        verify(result.x + result.width <= screen.x + screen.width - 12);
        verify(result.y + result.height <= screen.y + screen.height - 12);
        verify(result.cardWidth > 0);
    }
    function test_centeredAboveIcon() {
        var result = Geometry.layout({x:0, y:0, width:1920, height:1080},
            {anchorX:0.49, anchorY:0.95, anchorWidth:0.02, anchorHeight:0.04}, 1);
        compare(result.x + result.width / 2, 960);
        compare(result.y + result.height + 12, 1080 * 0.95);
    }
    function test_pointerGraceBounds() {
        var rect = {x:10, y:10, width:100, height:80};
        verify(Geometry.contains(rect, {x:7, y:50}, 4));
        verify(!Geometry.contains(rect, {x:5, y:50}, 4));
    }
}
