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
var QEasingCurve = { OutCubic: 1, InOutCubic: 2, InCubic: 3 };
var calls = [], cancelled = [], redirects = [], retargets = [], alive = new Set(), nextId = 1;
function animationTime(n) { return n; }
function animate(o) { calls.push(o); return o.animations.map(() => { alive.add(nextId); return nextId++; }); }
function cancel(ids) { cancelled.push(ids); ids.forEach(id => alive.delete(id)); }
function retarget(id, target, duration) { retargets.push({id: id, target: target, duration: duration}); return alive.has(id); }
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
check(calls.length === 1 && redirects.length === 0 && calls[0].animations.find(a => a.type === Effect.Opacity).from === 0 && calls[0].animations.find(a => a.type === Effect.Opacity).to === 1, 'opening must advance from hidden to visible');
w.minimized = true; w.minimizedChanged.emit();
check(calls.length === 1 && cancelled.length === 0 && retargets.length === 3, 'minimize preserves active opening transform');
w.minimized = false; w.minimizedChanged.emit();
check(calls.length === 1 && retargets.length === 6 && retargets[5].target === 1, 'rapid restore retargets from current painted state');
w.windowHiddenChanged.emit(w); check(calls.length === 1, 'no duplicate restore');
w.daisyMotion.slice().forEach(id => { alive.delete(id); effect.animationEnded.emit(w,id); });
check(!w.daisyMotionKind, 'completion releases state');
w.visible = false; w.windowHiddenChanged.emit(w);
check(calls.length === 2, 'hide');
w.visible = true; w.windowHiddenChanged.emit(w); check(calls.length === 2 && retargets[retargets.length-1].target === 1, 'unhide retargets without a reset');
w.windowMaximizedStateAboutToChange.emit(w);
w.geometry = {x:0,y:0,width:1200,height:800}; w.windowMaximizedStateChanged.emit(w);
check(calls[calls.length-1].animations[0].from.value1 === 0.5, 'maximize starts at old size');
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
var exitWindow = windowMock(); effects.windowAdded.emit(exitWindow);
exitWindow.daisyMotion.slice().forEach(id => { alive.delete(id); effect.animationEnded.emit(exitWindow,id); });
var exitGeometry = JSON.stringify(exitWindow.geometry);
effects.windowClosed.emit(exitWindow);
var close = calls[calls.length-1];
check(close.animations[0].from.value1 === 1 && close.animations[0].to.value1 === 1, 'close does not shift edges with a scale pulse');
check(close.animations[1].from.value1 === 0 && close.animations[1].to.value1 === 0, 'close does not translate');
check(JSON.stringify(exitWindow.geometry) === exitGeometry, 'close never writes client geometry');
var openingClose = windowMock(); effects.windowAdded.emit(openingClose);
var cancelCount = cancelled.length, closeCalls = calls.length;
effects.windowClosed.emit(openingClose);
check(cancelled.length === cancelCount && calls.length === closeCalls, 'close retargets opening without exposing reset geometry');
check(retargets[retargets.length-1].target === 0, 'interrupted close fades to hidden');
var minimizeWindow = windowMock(); effects.windowAdded.emit(minimizeWindow);
minimizeWindow.daisyMotion.slice().forEach(id => { alive.delete(id); effect.animationEnded.emit(minimizeWindow,id); });
var minimizeGeometry = JSON.stringify(minimizeWindow.geometry);
minimizeWindow.minimized = true; minimizeWindow.minimizedChanged.emit();
var minimize = calls[calls.length-1];
check(minimize.animations[0].from.value1 === 1 && minimize.animations[0].from.value2 === 1, 'minimize starts at the existing window size');
check(minimize.animations[1].from.value1 === 0 && minimize.animations[1].from.value2 === 0, 'minimize has no positional pre-jump');
check(JSON.stringify(minimizeWindow.geometry) === minimizeGeometry, 'minimize never moves native client geometry');
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
