.pragma library

function _cell(column, row, columnSpan, rowSpan, columns, rows) {
    return {
        x: column / columns,
        y: row / rows,
        width: columnSpan / columns,
        height: rowSpan / rows
    };
}

function _logicalIndex(index, featuredIndex) {
    if (index === featuredIndex) {
        return 0;
    }
    return index < featuredIndex ? index + 1 : index;
}

function layout(count, index, featuredIndex, aspectRatio) {
    if (count <= 0 || index < 0 || index >= count) {
        return _cell(0, 0, 1, 1, 1, 1);
    }

    var logical = _logicalIndex(index, Math.max(0, featuredIndex));
    if (count === 1) {
        return _cell(0, 0, 1, 1, 1, 1);
    }
    if (count === 2) {
        return logical === 0
            ? { x: 0, y: 0, width: 0.61, height: 1 }
            : { x: 0.61, y: 0, width: 0.39, height: 1 };
    }
    if (count === 3) {
        return logical === 0
            ? { x: 0, y: 0, width: 0.59, height: 1 }
            : { x: 0.59, y: (logical - 1) * 0.5, width: 0.41, height: 0.5 };
    }

    var ratio = Math.max(1, aspectRatio || 1.6);
    var columns = Math.max(3, Math.ceil(Math.sqrt((count + 3) * ratio)));
    var rows = Math.max(2, Math.ceil((count + 3) / columns));
    while (columns * rows - 3 < count) {
        rows += 1;
    }

    if (logical === 0) {
        return _cell(0, 0, 2, 2, columns, rows);
    }

    var wanted = logical - 1;
    var seen = 0;
    for (var row = 0; row < rows; ++row) {
        for (var column = 0; column < columns; ++column) {
            if (row < 2 && column < 2) {
                continue;
            }
            if (seen === wanted) {
                return _cell(column, row, 1, 1, columns, rows);
            }
            seen += 1;
        }
    }

    return _cell(columns - 1, rows - 1, 1, 1, columns, rows);
}

function fittedRect(cell, sourceWidth, sourceHeight, areaWidth, areaHeight, spacing, titleHeight, maxScale) {
    var inset = spacing / 2;
    var cellX = cell.x * areaWidth + inset;
    var cellY = cell.y * areaHeight + inset;
    var cellWidth = Math.max(1, cell.width * areaWidth - spacing);
    var cellHeight = Math.max(1, cell.height * areaHeight - spacing);
    var availablePreviewHeight = Math.max(1, cellHeight - titleHeight);
    var sourceRatio = Math.max(0.2, sourceWidth / Math.max(1, sourceHeight));
    var previewWidth = cellWidth;
    var previewHeight = previewWidth / sourceRatio;

    if (previewHeight > availablePreviewHeight) {
        previewHeight = availablePreviewHeight;
        previewWidth = previewHeight * sourceRatio;
    }

    var scaleLimit = Math.max(0.1, maxScale === undefined ? 1 : maxScale);
    if (previewWidth > sourceWidth * scaleLimit) {
        previewWidth = sourceWidth * scaleLimit;
        previewHeight = previewWidth / sourceRatio;
    }
    if (previewHeight > sourceHeight * scaleLimit) {
        previewHeight = sourceHeight * scaleLimit;
        previewWidth = previewHeight * sourceRatio;
    }

    return {
        x: cellX + (cellWidth - previewWidth) / 2,
        y: cellY + (cellHeight - previewHeight - titleHeight) / 2,
        width: previewWidth,
        height: previewHeight,
        titleHeight: titleHeight
    };
}
