.pragma library

function createTracker() {
    return { samples: [], position: 0, lastTime: NaN, direction: 0, reverseTravel: 0 };
}

function update(tracker, position, time) {
    if (!isFinite(position) || !isFinite(time)) return;
    var delta = position - tracker.position;
    if (delta === 0) return;
    if (!tracker.samples.length || time < tracker.lastTime || time - tracker.lastTime > 100)
        tracker.samples = [{ time: time - 16, position: tracker.position }];
    // Keep sensor noise from hiding a subsequent deliberate reversal.
    else
        tracker.reverseTravel = delta * tracker.direction >= 0 ? 0 : tracker.reverseTravel + delta;
    if (tracker.samples.length > 1 && Math.abs(tracker.reverseTravel) >= 0.002)
        tracker.samples = [{ time: tracker.lastTime, position: tracker.position }];
    if (tracker.samples.length === 1) {
        tracker.direction = delta > 0 ? 1 : -1;
        tracker.reverseTravel = 0;
    }
    tracker.position = position;
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
    // Match launcher paging: allow delivery latency, then decay while held still.
    return (tracker.position - first.position) /
        Math.max(8, tracker.lastTime - first.time + Math.max(0, time - tracker.lastTime - 32));
}

function shouldCommit(tracker, progress, time) {
    var speed = velocity(tracker, time);
    if (speed < -0.0005) return false;
    // A flick expresses intent through release speed, independently of the
    // halfway distance used for slow drags. Keep a small travel floor so a
    // sensor spike alone cannot open or close the overview.
    return progress >= 0.5 || progress >= 0.03 && speed >= 0.001;
}

function settleDuration(distance, speed, baseDuration) {
    return Math.max(80, Math.min(220, Math.round(Math.abs(distance) /
        Math.max(Math.abs(speed), 1 / Math.max(1, baseDuration)))));
}

// Same finite Hermite settlement as the shell's GestureSettlement.Position.
// Speed is position units per millisecond; the endpoint has zero velocity.
function settlePosition(from, to, speed, elapsed, duration) {
    if (duration <= 0 || elapsed >= duration) return to;
    if (elapsed <= 0 || from === to) return from;
    var t = Math.max(0, Math.min(1, elapsed / duration));
    var distance = to - from;
    var tangent = distance * Math.max(-1, Math.min(3, speed * duration / distance));
    return from + distance * t * t * (3 - 2 * t) + tangent * t * (1 - t) * (1 - t);
}
