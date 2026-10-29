.pragma library

function clamp01(value) {
    return Math.max(0, Math.min(1, Number(value) || 0));
}

function interpolateRect(source, target, progress) {
    var amount = clamp01(progress);
    return {
        x: source.x + (target.x - source.x) * amount,
        y: source.y + (target.y - source.y) * amount,
        width: source.width + (target.width - source.width) * amount,
        height: source.height + (target.height - source.height) * amount
    };
}

function transitionDuration(baseDuration, fromProgress, toProgress) {
    return Math.max(1, Math.round(Math.max(1, baseDuration)
                                  * Math.abs(clamp01(toProgress) - clamp01(fromProgress))));
}

function sourceRect(windowRect, targetRect, minimized, intersectsOutput) {
    if (minimized || !intersectsOutput) {
        return {
            x: targetRect.x,
            y: targetRect.y,
            width: targetRect.width,
            height: targetRect.height
        };
    }
    return {
        x: windowRect.x,
        y: windowRect.y,
        width: windowRect.width,
        height: windowRect.height
    };
}

function stableMergeIds(existingIds, candidateIds) {
    var candidateSet = {};
    var result = [];
    var index;
    for (index = 0; index < candidateIds.length; ++index) {
        candidateSet[String(candidateIds[index])] = true;
    }
    for (index = 0; index < existingIds.length; ++index) {
        var existing = String(existingIds[index]);
        if (candidateSet[existing]) {
            result.push(existing);
            delete candidateSet[existing];
        }
    }
    for (index = 0; index < candidateIds.length; ++index) {
        var candidate = String(candidateIds[index]);
        if (candidateSet[candidate]) {
            result.push(candidate);
            delete candidateSet[candidate];
        }
    }
    return result;
}

function shouldDeferReconcile(phase) {
    return phase === "gesture" || phase === "preparing" || phase === "entering" || phase === "exiting";
}
