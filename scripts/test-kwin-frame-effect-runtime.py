#!/usr/bin/env python3
"""Exercise native frame state and lifecycle in an isolated real KWin."""
import json
import subprocess
import time
from PySide6.QtCore import QEventLoop, QTimer, Qt
from PySide6.QtWidgets import QApplication, QWidget

app = QApplication([])
app.setQuitOnLastWindowClosed(False)
windows = []
errors = []
original_radii = {}

def effect(method, *args):
    result = subprocess.run(['qdbus6', 'org.kde.KWin', '/Effects',
        'org.kde.kwin.Effects.' + method, *args], capture_output=True, text=True, check=True, timeout=3)
    if method in ('loadEffect', 'isEffectLoaded'):
        assert result.stdout.strip() == 'true', (method, result.stdout, result.stderr)
    return result.stdout

def settle():
    loop = QEventLoop()
    QTimer.singleShot(100, loop.quit)
    loop.exec()

def open_window(frameless=False):
    w = QWidget()
    if frameless:
        w.setWindowFlags(Qt.Window | Qt.FramelessWindowHint)
    w.setWindowTitle('Daisy Frame CSD' if frameless else 'Daisy Frame SSD')
    w.setStyleSheet('background: #204080')
    w.resize(480, 320)
    w.show()
    windows.append(w)

def check_frames(count=2, square=False, resized=False):
    deadline = time.monotonic() + 3
    while True:
        settle()
        frames = json.loads(effect('debug', 'daisyframe', 'frames'))
        try:
            assert len(frames) == count, frames
            for frame in frames:
                assert frame['bands'] == 2, frame
                caption = frame['caption']
                original_radii.setdefault(caption, frame['originalRadius'])
                assert frame['originalRadius'] == original_radii[caption], frame
                is_csd = caption == 'Daisy Frame CSD'
                expected_square = square and is_csd
                assert frame['radius'] == (0 if expected_square else 16), frame
                assert frame['outerWidth'] == (0 if expected_square else 1), frame
                assert frame['innerWidth'] == (0 if expected_square else 1), frame
                if resized and is_csd and not square:
                    assert frame['width'] == 600 and frame['height'] == 400, frame
            return
        except AssertionError:
            if time.monotonic() >= deadline:
                raise

def run():
    try:
        effect('isEffectLoaded', 'daisyframe')
        open_window(False)
        check_frames(count=1)
        open_window(True)
        check_frames()
        windows[-1].resize(600, 400)
        check_frames(resized=True)
        windows[-1].showMaximized()
        check_frames(square=True)
        windows[-1].showFullScreen()
        check_frames(count=1)
        windows[-1].showNormal()
        check_frames()
        effect('reconfigureEffect', 'daisyframe')
        check_frames()
        effect('unloadEffect', 'daisyframe')
        settle()
        effect('loadEffect', 'daisyframe')
        check_frames()
        for window in windows:
            window.close()
        check_frames(count=0)
    except Exception as error:
        errors.append(repr(error))
    finally:
        app.quit()

QTimer.singleShot(200, run)
app.exec()
assert not errors, errors
print('PASS: KWin native outlines: SSD/CSD, dimensions, radius, maximize, fullscreen bypass, reconfigure, reload and teardown')
