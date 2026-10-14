function resolveAnchor(screen, request) {
    if (!request.panelHeight) return request;
    return {anchorX: request.anchorX,
            anchorY: (screen.height - request.panelHeight + request.anchorY * request.panelHeight) / screen.height,
            anchorWidth: request.anchorWidth,
            anchorHeight: request.anchorHeight * request.panelHeight / screen.height};
}
function layout(screen, anchor, count, aspect) {
    var margin = 12;
    var available = Math.max(1, screen.width - margin * 2);
    var cardWidth = Math.min(264, available);
    var width = Math.min(available, count * cardWidth);
    var height = Math.min(224, aspect > 0 ? 32 + cardWidth / aspect : 224, Math.max(1, screen.height - margin * 2));
    var center = screen.x + (anchor.anchorX + anchor.anchorWidth / 2) * screen.width;
    var top = screen.y + anchor.anchorY * screen.height;
    var bottom = top + anchor.anchorHeight * screen.height;
    var y = top - height - 12;
    if (y < screen.y + margin) y = bottom + 12;
    return { x: Math.max(screen.x + margin, Math.min(center - width / 2, screen.x + screen.width - margin - width)),
             y: Math.max(screen.y + margin, Math.min(y, screen.y + screen.height - margin - height)),
             width: width, height: height, cardWidth: cardWidth };
}
function contains(rect, point, margin) {
    return point.x >= rect.x - margin && point.x <= rect.x + rect.width + margin
        && point.y >= rect.y - margin && point.y <= rect.y + rect.height + margin;
}
