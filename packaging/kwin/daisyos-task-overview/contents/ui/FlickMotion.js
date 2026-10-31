.pragma library

function createTracker() {
    return { samples: [], position: 0, lastTime: NaN, lastDelta: 0 };
}

function update(tracker, position, time) {
    if (!isFinite(position) || !isFinite(time)) return;
    var delta = position - tracker.position;
    if (delta === 0) return;
    if (!tracker.samples.length || time < tracker.lastTime || time - tracker.lastTime > 100)
        tracker.samples = [{ time: time - 16, position: tracker.position }];
    else if (delta * tracker.lastDelta < 0 && Math.abs(delta) >= 0.002)
        tracker.samples = [{ time: tracker.lastTime, position: tracker.position }];
    tracker.position = position;
    tracker.lastDelta = delta;
    tracker.lastTime = time;
    var samples = tracker.samples;
    if (samples.length > 1 && samples[samples.length - 1].time === time)
        samples[samples.length - 1] = { time: time, position: position };
    else
        samples.push({ time: time, position: position });
    while (samples.length > 2 && (time - samples[0].time > 80 || samples.length > 24))
        samples.shift();
}

function velocity(tracker, time) {
    if (!tracker.samples.length || time < tracker.lastTime || time - tracker.lastTime > 100) return 0;
    var first = tracker.samples[0];
    return (tracker.position - first.position) / Math.max(8, tracker.lastTime - first.time);
}

function shouldCommit(tracker, progress, time) {
    var speed = velocity(tracker, time);
    if (speed < -0.0005) return false;
    return progress >= 0.5 ||
        progress >= 0.06 && speed >= 0.0015 && progress + speed * 180 >= 0.5;
}

function settleDuration(distance, speed, baseDuration) {
    return Math.max(80, Math.min(220, Math.round(Math.abs(distance) /
        Math.max(Math.abs(speed), 1 / Math.max(1, baseDuration)))));
}
