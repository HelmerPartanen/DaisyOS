#!/usr/bin/env python3
"""Run inside an isolated KWin bus/session; exercises only disposable windows."""
import subprocess
import tempfile
from pathlib import Path
from PySide6.QtCore import QTimer
from PySide6.QtWidgets import QApplication, QWidget

app = QApplication([])
app.setQuitOnLastWindowClosed(False)
window = QWidget()
window.setWindowTitle("DaisyOS Tiling Test Client")
window.resize(500, 350)
window.show()
root = tempfile.TemporaryDirectory(prefix="daisy-tiling-commands-")
counter = 0

def command(code):
    global counter
    counter += 1
    path = Path(root.name) / f"{counter}.js"
    path.write_text(code)
    name = f"daisy-tiling-test-{counter}"
    subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.loadScript", str(path), name], check=True, capture_output=True, timeout=3)
    subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.start"], check=True, capture_output=True, timeout=3)
    QTimer.singleShot(250, lambda: subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.unloadScript", name], capture_output=True, timeout=3))

QTimer.singleShot(600, lambda: command("workspace.showOutline(0, 0, 640, 720);"))
QTimer.singleShot(950, lambda: command("workspace.showOutline(640, 0, 640, 360);"))
QTimer.singleShot(1020, lambda: command("workspace.showOutline(0, 360, 640, 360);"))
QTimer.singleShot(1400, lambda: command("workspace.hideOutline();"))
QTimer.singleShot(1600, lambda: command("workspace.showOutline(0, 0, 1280, 720);"))
QTimer.singleShot(1900, lambda: command("workspace.hideOutline();"))
QTimer.singleShot(2250, app.quit)
app.exec()
window.close()
root.cleanup()
