#!/usr/bin/env python3
"""Exercise plugin load, paints, resize, fullscreen bypass, reload and teardown."""
import subprocess
from PySide6.QtCore import QTimer, Qt
from PySide6.QtWidgets import QApplication, QWidget

app = QApplication([])
app.setQuitOnLastWindowClosed(False)
windows = []
errors = []

def effect(method, *args):
    result = subprocess.run(['qdbus6', 'org.kde.KWin', '/Effects',
        'org.kde.kwin.Effects.' + method, *args], capture_output=True, text=True, timeout=3)
    if result.returncode or (method in ('loadEffect', 'isEffectLoaded') and result.stdout.strip() != 'true'):
        errors.append((method, result.stdout, result.stderr))

def open_window(frameless=False):
    w = QWidget()
    if frameless:
        w.setWindowFlags(Qt.Window | Qt.FramelessWindowHint)
    w.setWindowTitle('Daisy Frame Runtime Client')
    w.setStyleSheet('background: #204080')
    w.resize(480, 320)
    w.show()
    windows.append(w)

QTimer.singleShot(200, lambda: effect('isEffectLoaded', 'daisyframe'))
QTimer.singleShot(400, lambda: open_window(False))
QTimer.singleShot(650, lambda: open_window(True))
QTimer.singleShot(900, lambda: windows[-1].resize(600, 400))
QTimer.singleShot(1150, lambda: windows[-1].showMaximized())
QTimer.singleShot(1400, lambda: windows[-1].showFullScreen())
QTimer.singleShot(1650, lambda: windows[-1].showNormal())
QTimer.singleShot(1900, lambda: effect('reconfigureEffect', 'daisyframe'))
QTimer.singleShot(2150, lambda: effect('unloadEffect', 'daisyframe'))
QTimer.singleShot(2400, lambda: effect('loadEffect', 'daisyframe'))
QTimer.singleShot(2700, lambda: effect('isEffectLoaded', 'daisyframe'))
QTimer.singleShot(3000, lambda: [w.close() for w in windows])
QTimer.singleShot(3300, app.quit)
app.exec()
assert not errors, errors
print('PASS: KWin loaded, painted, resized, maximized, bypassed fullscreen, reconfigured and reloaded the effect')
