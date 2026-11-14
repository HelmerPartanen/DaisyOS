function resolveAnchor(screen, request) {
    if (!request.panelHeight) return request;
    var edge = request.dockEdge || "Bottom";
    var x = request.anchorX, y = request.anchorY, w = request.anchorWidth, h = request.anchorHeight;
    if (edge === "Left" || edge === "Right") {
        x = ((edge === "Right" ? screen.width - request.panelWidth : 0) + x * request.panelWidth) / screen.width;
        w = w * request.panelWidth / screen.width;
    } else {
        y = ((edge === "Bottom" ? screen.height - request.panelHeight : 0) + y * request.panelHeight) / screen.height;
        h = h * request.panelHeight / screen.height;
    }
    return {anchorX:x, anchorY:y, anchorWidth:w, anchorHeight:h, dockEdge:edge};
}
function layout(screen, anchor, count, aspect) {
    var margin = 12;
    var available = screen.width - margin * 2;
    if (anchor.dockEdge === "Left") available = screen.width * (1 - anchor.anchorX - anchor.anchorWidth) - margin * 2;
    if (anchor.dockEdge === "Right") available = screen.width * anchor.anchorX - margin * 2;
    available = Math.max(1, available);
    var cardWidth = Math.min(264, available);
    var width = Math.min(available, count * cardWidth);
    var height = Math.min(224, aspect > 0 ? 32 + cardWidth / aspect : 224, Math.max(1, screen.height - margin * 2));
    var center = screen.x + (anchor.anchorX + anchor.anchorWidth / 2) * screen.width;
    var top = screen.y + anchor.anchorY * screen.height;
    var bottom = top + anchor.anchorHeight * screen.height;
    var y = top - height - 12;
    if (y < screen.y + margin) y = bottom + 12;
    var x = center - width / 2;
    if (anchor.dockEdge === "Left" || anchor.dockEdge === "Right") {
        x = screen.x + anchor.anchorX * screen.width;
        x += anchor.dockEdge === "Left" ? anchor.anchorWidth * screen.width + 12 : -width - 12;
        y = top + anchor.anchorHeight * screen.height / 2 - height / 2;
    }
    return { x: Math.max(screen.x + margin, Math.min(x, screen.x + screen.width - margin - width)),
             y: Math.max(screen.y + margin, Math.min(y, screen.y + screen.height - margin - height)),
             width: width, height: height, cardWidth: cardWidth };
}
function contains(rect, point, margin) {
    return point.x >= rect.x - margin && point.x <= rect.x + rect.width + margin
        && point.y >= rect.y - margin && point.y <= rect.y + rect.height + margin;
}
