#!/usr/bin/env python3
"""Exercise KWin signal ordering and interruptions in Qt's actual JS engine."""
import os
from pathlib import Path
os.environ.setdefault('QT_QPA_PLATFORM', 'offscreen')
from PySide6.QtCore import QCoreApplication
from PySide6.QtQml import QJSEngine
app = QCoreApplication([])
engine = QJSEngine()
mock = '''
function signal() { return { callbacks: [], connect: function(f) { this.callbacks.push(f); },
    emit: function(...args) { this.callbacks.forEach(f => f(...args)); } }; }
var effect = { readConfig: (k,d) => d, isGrabbed: w => !!w.grabbed,
    configChanged: signal(), animationEnded: signal() };
var effects = { animationTimeFactor: 1, stackingOrder: [], hasActiveFullScreenEffect: false,
    windowAdded: signal(), windowClosed: signal(), windowDeleted: signal(),
    windowDataChanged: signal(), hasActiveFullScreenEffectChanged: signal() };
var Effect = { Scale: 1, Opacity: 2, Translation: 3, Size: 4, Forward: 1, Backward: 2,
    WindowAddedGrabRole: 5, WindowClosedGrabRole: 6, WindowMinimizedGrabRole: 7, WindowUnminimizedGrabRole: 8 };
var QEasingCurve = { OutCubic: 1, InOutCubic: 2 };
var calls = [], cancelled = [], redirects = [], alive = new Set(), nextId = 1;
function animationTime(n) { return n; }
function animate(o) { calls.push(o); return o.animations.map(() => { alive.add(nextId); return nextId++; }); }
function cancel(ids) { cancelled.push(ids); ids.forEach(id => alive.delete(id)); }
function redirect(ids, dir) { redirects.push(dir); return ids.every(id => alive.has(id)); }
function windowMock() { return { managed: true, normalWindow: true, caption: 'Files', visible: true,
    geometry: {x: 100,y: 100,width: 600,height: 400}, iconGeometry: {x: 100,y: 760,width: 40,height: 40},
    windowStartUserMovedResized: signal(),
    minimizedChanged: signal(), windowHiddenChanged: signal(), windowMaximizedStateAboutToChange: signal(),
    windowMaximizedStateChanged: signal(), windowFrameGeometryChanged: signal(), windowFullScreenChanged: signal() }; }
function check(value, message) { if (!value) throw new Error(message); }
'''
checks = '''
var w = windowMock(); effects.windowAdded.emit(w);
check(calls.length === 1 && redirects.length === 0 && calls[0].animations[1].from === 0 && calls[0].animations[1].to === 1, 'opening must advance from hidden to visible');
w.minimized = true; w.minimizedChanged.emit();
check(calls.length === 2 && calls[1].keepAlive === false, 'minimize cancels opening');
w.minimized = false; w.minimizedChanged.emit();
check(calls.length === 2 && redirects[0] === Effect.Backward, 'rapid restore reverses');
w.windowHiddenChanged.emit(w); check(calls.length === 2, 'no duplicate restore');
w.daisyMotion.slice().forEach(id => { alive.delete(id); effect.animationEnded.emit(w,id); });
check(!w.daisyMotionKind, 'completion releases state');
w.visible = false; w.windowHiddenChanged.emit(w);
check(calls.length === 3, 'hide');
w.visible = true; w.windowHiddenChanged.emit(w); check(calls.length === 3 && redirects[redirects.length-1] === Effect.Backward, 'unhide reverses');
w.windowMaximizedStateAboutToChange.emit(w);
w.geometry = {x:0,y:0,width:1200,height:800}; w.windowMaximizedStateChanged.emit(w);
check(calls[calls.length-1].animations[0].from.value1 === 600, 'maximize starts at old size');
w.windowFrameGeometryChanged.emit(w,{width:600,height:400});
check(w.daisyMotionKind === 'geometry', 'initial maximize geometry must not cancel transition');
w.geometry.width = 1100; w.windowFrameGeometryChanged.emit(w,{width:1200,height:800});
check(!w.daisyMotion, 'resize cancels stale geometry');
// Dragging cancels any remaining positional transform, even without a resize.
w.windowMaximizedStateAboutToChange.emit(w);
w.geometry = {x:0,y:0,width:1200,height:800}; w.windowMaximizedStateChanged.emit(w);
check(w.daisyMotionKind === 'geometry', 'start a transition before grabbing');
w.move = true; w.windowStartUserMovedResized.emit(w);
check(!w.daisyMotion && !w.daisyGeometrySource, 'drag cancels stale positional animation');
var dragCalls = calls.length;
w.windowMaximizedStateAboutToChange.emit(w);
w.geometry = {x:180,y:150,width:600,height:400}; w.windowMaximizedStateChanged.emit(w);
check(calls.length === dragCalls && !w.daisyMotion, 'unmaximize while dragging must follow the pointer');
w.move = false; w.resize = true; w.windowStartUserMovedResized.emit(w);
w.windowMaximizedStateAboutToChange.emit(w); w.windowMaximizedStateChanged.emit(w);
check(calls.length === dragCalls, 'interactive resize must not start geometry animation');
w.resize = false;
effects.windowClosed.emit(w); check(calls[calls.length-1].keepAlive, 'close retains deleted surface');
var panel = windowMock(); panel.normalWindow = false; var before = calls.length;
effects.windowAdded.emit(panel); check(calls.length === before, 'exclude shell surfaces');
var lock = windowMock(); lock.lockScreen = true; effects.windowAdded.emit(lock);
check(calls.length === before, 'exclude lockscreen');
effects.hasActiveFullScreenEffect = true; effects.hasActiveFullScreenEffectChanged.emit();
var overview = windowMock(); effects.windowAdded.emit(overview); check(calls.length === before, 'yield to overview');
effects.hasActiveFullScreenEffect = false;
effects.animationTimeFactor = 0; effects.windowAdded.emit(windowMock());
check(calls.length === before, 'zero compositor speed');
effects.animationTimeFactor = 0.5; effects.windowAdded.emit(windowMock());
check(calls.length === before + 1, 'fractional compositor speed');
before = calls.length; effect.readConfig = () => true; effect.configChanged.emit();
effects.windowAdded.emit(windowMock()); check(calls.length === before, 'reduce motion');
'''
source = Path(__file__).parents[1] / 'contents/code/main.js'
result = engine.evaluate(mock + source.read_text() + checks, str(source))
if result.isError():
    raise SystemExit(f"{result.toString()} at line {result.property('lineNumber').toInt()}")
print('PASS: lifecycle, reversals, geometry, exclusions, overview and reduced motion')
