#!/usr/bin/env python3
"""Run exclusively inside the isolated compositor created by the test runner."""
import json
import subprocess
from PySide6.QtCore import Qt, QTimer, QEventLoop
from PySide6.QtWidgets import QApplication, QWidget

app = QApplication([])
windows = []
for title in ('Daisy Snap Test Moving', 'Daisy Snap Test Neighbor'):
    w = QWidget()
    w.setWindowFlags(Qt.Window | Qt.FramelessWindowHint)
    w.setWindowTitle(title)
    w.resize(300, 200)
    w.show()
    windows.append(w)

def settle():
    loop = QEventLoop()
    QTimer.singleShot(100, loop.quit)
    loop.exec()

def command(op='read', caption='Daisy Snap Test Moving', **args):
    result = subprocess.run(['qdbus6', 'org.kde.KWin', '/Effects',
        'org.kde.kwin.Effects.debug', 'daisysnaptest',
        json.dumps(dict(op=op, caption=caption, **args))], capture_output=True, text=True, check=True, timeout=3)
    data = json.loads(result.stdout)
    assert 'frame' in data, (op, result.stdout)
    return data

errors = []
def run():
    try:
        command('place', x=100, y=100)
        command('place', caption='Daisy Snap Test Neighbor', x=600, y=100)
        settle()
        assert command()['frame'] == dict(x=100, y=100, width=300, height=200)
        # Neighbor edges used to attract the moving window in this range.
        for x, y in [(296, 100), (304, 100), (596, 100), (604, 100), (304, 350), (493, 263)]:
            position = command('snap', x=x, y=y)
            assert (position['x'], position['y']) == (x, y), (x, y, position)
        area = command()['area']
        left, top = area['x'], area['y']
        right, bottom = left + area['width'] - 300, top + area['height'] - 200
        # Light edge resistance starts only after crossing the work-area edge.
        for x, y, ex, ey in [(left-4, 150, left, 150), (right+4, 150, right, 150),
                (400, top-4, 400, top), (400, bottom+4, 400, bottom),
                (left+4, 150, left+4, 150), (left-12, 150, left-12, 150)]:
            position = command('snap', x=x, y=y)
            assert (position['x'], position['y']) == (ex, ey), (x, y, position)
        # Native tiling still produces exact half/quarter work-area targets.
        for mode, x, y, width, height in [
                ('left', left, top, area['width']/2, area['height']),
                ('right', left+area['width']/2, top, area['width']/2, area['height']),
                ('right-bottom', left+area['width']/2, top+area['height']/2, area['width']/2, area['height']/2)]:
            command('tile', mode=mode)
            settle()
            frame = command()['frame']
            assert frame == dict(x=x, y=y, width=width, height=height), (mode, frame)
        print('PASS: real KWin geometry: free movement, no window/center magnets, bounded screen edges, half/quarter tiling', flush=True)
    except Exception as exc:
        errors.append(exc)
    finally:
        app.quit()
QTimer.singleShot(600, run)
app.exec()
assert not errors, errors
